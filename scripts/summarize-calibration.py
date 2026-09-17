#!/usr/bin/env python3
"""Summarize a completed before/after calibration run, preserving seed-level clusters."""
import collections, csv, json, pathlib, sys
root = pathlib.Path(sys.argv[1])
def read(path): return json.loads(path.read_text())
def save(name, data):
    path = root/name
    if path.exists(): raise SystemExit('Refusing existing output: '+str(path))
    path.write_text(json.dumps(data,indent=2))
phases=['baseline-dev-final','candidate-dev','baseline-heldout','candidate-heldout']
data={}
for phase in phases:
    manifest=read(root/phase/'manifest.json')
    if manifest['status']!='COMPLETE': raise SystemExit('PARTIAL: '+phase)
    data[phase]=read(root/phase/'summary.json')
def summary(rows):
    result={}
    for population in ['existing','symmetric']:
        group=[r for r in rows if r['population']==population]
        cells=[]
        for server in [0,1]:
            for end in [-1,1]:
                rs=[r for r in group if r['firstServer']==server and r['endA']==end]
                cells.append({'firstServer':server,'endA':end,'matches':len(rs),'winsA':sum(r['match']['finalScore']['winner']==0 for r in rs),'points':sum(r['match']['finalScore']['pointsPlayed'] for r in rs)})
        result[population]={'matches':len(group),'completed':sum(r['match']['status']=='Completed' for r in group),'winsA':sum(r['match']['finalScore']['winner']==0 for r in group),'points':sum(r['match']['finalScore']['pointsPlayed'] for r in group),'issues':sum(r['issueCount'] for r in group),'cells':cells,'seedClusters':[{'seed':seed,'matches':sum(r['seed']==seed for r in group),'winsA':sum(r['seed']==seed and r['match']['finalScore']['winner']==0 for r in group)} for seed in sorted({r['seed'] for r in group})]}
    return result
for phase,rows in data.items(): save(phase+'-aggregate.json',summary(rows))
comparisons={}
for split,before,after in [('development','baseline-dev-final','candidate-dev'),('heldOut','baseline-heldout','candidate-heldout')]:
    old={r['key']:r for r in data[before]};differences=[];normalized=0;available=0
    for new in data[after]:
        base=old[new['key']]
        if new['input']!=base['input']: raise SystemExit('Unpaired input '+new['key'])
        fields=[field for field in ['match','metrics','checks'] if base[field]!=new[field]]
        if fields: differences.append({'key':new['key'],'changed':fields})
        if 'v1NormalizedHash' in new: available+=1;normalized+=new['v1NormalizedHash']==base['replayHash']
    comparisons[split]={'pairs':len(old),'changedSummaries':differences,'normalizedReplayHashSamples':available,'identicalExceptEngineVersion':normalized,'baseline':summary(data[before]),'candidate':summary(data[after])}
comparisons['inference']='Descriptive paired seeds; no shot/point independence or population confidence claim. Server/end variants within seed are a cluster.'
save('comparison.json',comparisons)
save('baseline-summary.json',{'development':summary(data['baseline-dev-final']),'heldOut':summary(data['baseline-heldout'])})
save('candidate-summary.json',{'development':summary(data['candidate-dev']),'heldOut':summary(data['candidate-heldout'])})
with (root/'metrics.csv').open('x',newline='') as f:
    w=csv.writer(f);w.writerow(['phase','key','seedCluster','metric','unit','quality','samples','eligible','validRatio','mean','p50','p90','p99','max','excluded'])
    for phase,rows in data.items():
        for row in rows:
            for key,m in row['metrics'].items(): w.writerow([phase,row['key'],row['seed'],key,m['unit'],m['quality'],m['samples'],m['eligible'],m['validRatio'],m['mean'],m['p50'],m['p90'],m['p99'],m['max'],json.dumps(m['excluded'])])
print(json.dumps({k:{'pairs':v['pairs'],'changedSummaries':len(v['changedSummaries']),'normalizedReplayHashSamples':v['normalizedReplayHashSamples'],'identicalExceptEngineVersion':v['identicalExceptEngineVersion']} for k,v in comparisons.items() if isinstance(v,dict)},indent=2))
