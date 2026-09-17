#!/usr/bin/env python3
"""Sequential evidence capture. No network, no mutation of fixtures, fails on reused output.
Run from any cwd. DOTNET -> PATH -> Linux-only local SDK. Python standard library only.
"""
import argparse, hashlib, json, os, pathlib, platform, shutil, subprocess, time
ROOT = pathlib.Path(__file__).resolve().parents[1]
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--seeds', required=True, help='comma-separated uint seeds')
    ap.add_argument('--cli', default='src/TennisSim.Cli/bin/Debug/net10.0/TennisSim.Cli.dll')
    ap.add_argument('--source-id', required=True)
    ap.add_argument('--diagnostic-cli', help='Same analyzer binary for both engine versions')
    ap.add_argument('--seconds', type=int, default=1800)
    args = ap.parse_args()
    dotnet = os.environ.get('DOTNET') or shutil.which('dotnet')
    if not dotnet and platform.system() == 'Linux': dotnet = str(ROOT / '.tools/dotnet/dotnet')
    if not dotnet or not shutil.which(dotnet): ap.error('Set DOTNET to an installed SDK executable or put dotnet on PATH (Mac needs a Mac SDK).')
    seeds = [int(s) for s in args.seeds.split(',')]
    if len(set(seeds)) != len(seeds) or any(s < 0 or s > 4294967295 for s in seeds): ap.error('unique uint seeds required')
    out = pathlib.Path(args.out).resolve()
    if out.exists(): ap.error('output must not exist; preserve earlier runs')
    out.mkdir(parents=True); (out/'logs').mkdir(); (out/'replays').mkdir(); (out/'inputs').mkdir()
    cli = pathlib.Path(args.cli).resolve()
    if not cli.is_file(): ap.error('Build CLI first; specified DLL missing')
    manifest = {'sourceId':args.source_id,'seeds':seeds,'targetPopulation':'UNSPECIFIED','status':'PARTIAL','runnerHash':digest(pathlib.Path(__file__)),'diagnosticBinaryHash':digest(pathlib.Path(args.diagnostic_cli).resolve()) if args.diagnostic_cli else digest(cli),'secondsBudget':args.seconds,'commands':[], 'environment':{'platform':platform.platform(),'architecture':platform.machine(),'dotnet':subprocess.check_output([dotnet,'--info'],text=True)}, 'binaryHashes':{p.name:digest(p) for p in cli.parent.glob('*.dll')}}
    rows=[]; started=time.monotonic()
    def save():
        (out/'manifest.json').write_text(json.dumps(manifest,indent=2)); (out/'summary.json').write_text(json.dumps(rows,indent=2))
    def run(name, arguments, allowed=(0,)):
        tool=pathlib.Path(args.diagnostic_cli).resolve() if args.diagnostic_cli and arguments[0]=='diagnose' else cli
        cmd=[dotnet,str(tool)]+arguments; remaining=args.seconds-(time.monotonic()-started)
        if remaining <= 0: raise TimeoutError('batch budget exhausted')
        with (out/'logs'/f'{name}.log').open('w') as log:
            try: p=subprocess.run(cmd,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,timeout=remaining)
            except subprocess.TimeoutExpired:
                manifest['commands'].append({'name':name,'argv':cmd,'exitCode':None,'status':'TIMEOUT'}); raise
        manifest['commands'].append({'name':name,'argv':cmd,'exitCode':p.returncode}); save()
        if p.returncode not in allowed: raise RuntimeError(f'{name}: exit {p.returncode}, see log')
    try:
        for population in ['existing','symmetric']:
            for server in [0,1]:
                for end in [-1,1]:
                    config=out/'inputs'/f'config-{server}-{end}.json'; config.write_text(json.dumps({'firstServer':server,'initialEndA':end}))
                    for seed in seeds:
                        key=f'{population}-server{server}-end{end}-seed{seed}'
                        replay=out/'replays'/f'{key}.json'; audit=out/f'{key}.audit.json'
                        run(key,['match','--seed',str(seed),'--config',str(config),'--player-b','baseline' if population=='symmetric' else 'defender','--quiet','--out',str(replay)])
                        run(key+'-diagnose',['diagnose','--input',str(replay),'--out',str(audit),'--source-id',args.source_id],(0,1))
                        report=json.loads(audit.read_text()); record=json.loads(replay.read_text())
                        row={'key':key,'seed':seed,'population':population,'firstServer':server,'endA':end,'input':record['input'],'inputHash':hashlib.sha256(json.dumps(record['input'],sort_keys=True).encode()).hexdigest(),'replayHash':digest(replay),'v1NormalizedHash':hashlib.sha256(replay.read_bytes().replace(b'tennissim-mvp-2',b'tennissim-mvp-1')).hexdigest(),'match':report['match'],'checks':report['checks'],'issueCount':len(report['issues']),'metrics':{k:{kk:vv for kk,vv in v.items() if kk!='values'} for k,v in report['metrics'].items()}}
                        # Keep representative and failing replays only; all input settings and hashes survive.
                        keep=seed==seeds[0] or row['issueCount']>0
                        if keep: row['replayFile']=str(replay); row['auditFile']=str(audit)
                        else: replay.unlink(); audit.unlink()
                        rows.append(row); save()
                        print(key,'issues='+str(row['issueCount']),record['finalScore']['display'],flush=True)
        manifest['status']='COMPLETE'
    finally:
        manifest['elapsedSeconds']=time.monotonic()-started
        manifest['fileHashes']={str(p.relative_to(out)):digest(p) for p in out.rglob('*') if p.is_file() and p.name!='manifest.json'}
        save()
    return 0
if __name__=='__main__': raise SystemExit(main())
