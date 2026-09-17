using TennisSim.Core;
using TennisSim.Core.Bounce;
using TennisSim.Cli;

try { return Cli.Run(args); }
catch (Exception ex) when (ex is ArgumentException or IOException or System.Text.Json.JsonException or OverflowException or FormatException)
{ Console.Error.WriteLine("ERROR: " + ex.Message); return 2; }

internal static class Cli
{
    private static readonly HashSet<string> Known = new() { "seed", "player-a", "player-b", "tactics-a", "tactics-b", "instructions", "config", "out", "quiet", "chunk", "seeds", "count", "input", "source-id", "surface-model", "profile", "ball", "ball-condition" };
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help")
        {
            Console.WriteLine("TennisSim: match | compare | points | bounce | replay | resimulate | diagnose | scenarios\n" +
                "match --seed 42 --player-a baseline --player-b defender --tactics-a backhand --out artifacts/match.json\n" +
                "compare --seeds 11,22,33,44,55 --out artifacts/comparison.json\npoints --count 1000 --seed 100 --out artifacts/points.json\n" +
                "bounce --input impact.json --surface-model impulse --profile profiles/pair.json --out artifacts/bounce.json\n" +
                "diagnose --input artifacts/match.json --out artifacts/audit.json --source-id SOURCE\nscenarios --out artifacts/scenarios.json\n" +
                "replay --input artifacts/match.json\nresimulate --input artifacts/match.json --out artifacts/resimulated.json\n" +
                "Players: server|baseline|defender|JSON path; tactics: balanced|backhand|safe|aggressive|JSON path.\n" +
                "Also: --config JSON --instructions JSON --chunk 120 --quiet. --surface-model legacy|impulse selects the bounce model;\n" +
                "impulse additionally accepts --profile/--ball/--ball-condition JSON. Core outcomes are uncalibrated.");
            return 0;
        }
        var options = new Dictionary<string, string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--") || !Known.Contains(args[i][2..])) throw new ArgumentException("Unknown option: " + args[i]);
            string key = args[i][2..];
            if (options.ContainsKey(key)) throw new ArgumentException("Duplicate option: " + key);
            if (key == "quiet") options[key] = "true";
            else { if (++i >= args.Length || args[i].StartsWith("--")) throw new ArgumentException("Missing option value: " + key); options[key] = args[i]; }
        }
        string Get(string name, string fallback) => options.GetValueOrDefault(name, fallback);
        int chunk = int.Parse(Get("chunk", "4096")); if (chunk < 1) throw new ArgumentException("chunk must be positive");
        switch (args[0])
        {
            case "diagnose":
            {
                string file = Get("input", "artifacts/match.json");
                string output = Get("out", file + ".audit.json");
                if (Path.GetFullPath(file) == Path.GetFullPath(output) || File.Exists(output)) throw new ArgumentException("Diagnostic output must be a new sidecar path");
                var report = Diagnostics.Analyze(Diagnostics.Parse(File.ReadAllText(file)), file, Diagnostics.Hash(File.ReadAllBytes(file)), Get("source-id", "UNKNOWN: use producing run manifest"));
                ReplayJson.Save(output, report);
                Console.WriteLine($"DIAGNOSTICS issues={report.Issues.Count} sidecar={output}");
                return report.Issues.Count == 0 ? 0 : 1;
            }
            case "scenarios":
            {
                var results = AuditScenarios.Run();
                ReplayJson.Save(Get("out", "artifacts/scenarios.json"), results);
                foreach (var result in results) Console.WriteLine($"{result.Status} {result.Name}: {result.Observed} {result.Unit}");
                return results.Any(r => r.Status == "FAIL" && r.Category == "CORRECTNESS_BUG") ? 1 : 0;
            }
            case "bounce":
            {
                var impact = ReplayJson.Deserialize<ImpactState>(File.ReadAllText(Get("input", "artifacts/impact.json")));
                var environment = ImpulseEnvironment(options);
                var sample = BounceModel.SampleAtContact(impact, environment.Ball, environment.Profile.SurfaceId, "cli-flat-contact");
                var result = BounceModel.Resolve(impact, environment.Ball, environment.Condition, sample, environment.Profile, environment.Tolerances);
                if (options.TryGetValue("out", out var path)) { ReplayJson.Save(path, result); Console.WriteLine("BOUNCE_FILE=" + Path.GetFullPath(path)); }
                Console.WriteLine($"BOUNCE status={result.Status} limit={result.ActiveImpulseLimit} en={result.NormalCorUsed:F9} mu={result.MuEffectiveUsed:F9} beta={result.BetaGripUsed:F9} outOfDomain={result.OutOfDomain} " +
                    $"Jn={result.NormalImpulseNs:F12} |Jt|={result.TangentImpulseNs.Length:F12} betaEffective={(result.BetaEffective.HasValue ? result.BetaEffective.Value.ToString("F9") : "NA")} " +
                    $"vAfter=({result.PostState.VelocityMS.X:F9},{result.PostState.VelocityMS.Y:F9},{result.PostState.VelocityMS.Z:F9}) warnings={string.Join(",", result.Warnings)}");
                return result.Status == BounceStatus.NUMERICAL_FAILURE || result.Status == BounceStatus.DEEP_INITIAL_PENETRATION ? 1 : 0;
            }
            case "match":
            {
                var record = RunMatch(Input(options), chunk);
                Print(record, !options.ContainsKey("quiet"));
                if (options.TryGetValue("out", out var path)) { ReplayJson.Save(path, record); Console.WriteLine("REPLAY_FILE=" + Path.GetFullPath(path)); }
                return record.Status == "Completed" ? 0 : 1;
            }
            case "replay":
                Print(ReplayJson.Load(Get("input", "artifacts/match.json")), true); return 0;
            case "resimulate":
            {
                var saved = ReplayJson.Load(Get("input", "artifacts/match.json"));
                if (saved.EngineVersion != new MatchRecord().EngineVersion) throw new ArgumentException("Engine version mismatch: old replay display is supported, old-engine resimulation requires its original binary");
                var replayed = RunMatch(saved.Input, chunk);
                bool same = ReplayJson.Serialize(saved) == ReplayJson.Serialize(replayed);
                if (options.TryGetValue("out", out var path)) ReplayJson.Save(path, replayed);
                Console.WriteLine("RESIMULATION_IDENTICAL=" + same.ToString().ToLowerInvariant()); return same ? 0 : 1;
            }
            case "compare":
            {
                var seeds = Get("seeds", "11,22,33,44,55").Split(',').Select(uint.Parse).ToArray();
                var rows = new List<object>();
                string[] policies = { "balanced", "backhand", "safe", "aggressive" };
                bool failed = false;
                foreach (var policy in policies)
                {
                    int won = 0, completed = 0, backhand = 0, selections = 0, shots = 0, points = 0, attempts = 0, servesIn = 0, doubleFaults = 0;
                    var targets = new List<Vec3>(); var landings = new List<Vec3>(); var speeds = new List<double>();
                    var reasons = new Dictionary<string, int>(); var choices = new Dictionary<string, int>(); var matches = new List<object>();
                    var fixedInput = Input(options, samePlayers: true); fixedInput.Tactics[0] = Tactics(policy);
                    foreach (var seed in seeds)
                    {
                        var input = ReplayJson.Deserialize<MatchInput>(ReplayJson.Serialize(fixedInput)); input.Seed = seed; input.Tactics[0] = Tactics(policy);
                        var record = RunMatch(input, chunk); var a = record.Stats.Players[0];
                        if (record.Status == "Completed") { completed++; if (record.FinalScore.Winner == 0) won++; } else failed = true;
                        backhand += a.BackhandTargetSelection.Numerator; selections += a.BackhandTargetSelection.Denominator; shots += a.Shots; points += a.PointsWon;
                        attempts += a.ServeAttempts; servesIn += a.ServesIn; doubleFaults += a.DoubleFaults;
                        targets.AddRange(a.IntendedTargets); landings.AddRange(a.FirstLandings); speeds.AddRange(a.ShotSpeeds);
                        foreach (var pair in record.Stats.EndReasons) reasons[pair.Key] = reasons.GetValueOrDefault(pair.Key) + pair.Value;
                        foreach (var pair in a.Choices) choices[pair.Key] = choices.GetValueOrDefault(pair.Key) + pair.Value;
                        matches.Add(new { seed, record.Status, score = record.FinalScore, record.Diagnostic });
                    }
                    rows.Add(new { policy, input = fixedInput, seeds, matches, wins = new Ratio { Numerator = won, Denominator = completed }, backhandSelection = new Ratio { Numerator = backhand, Denominator = selections }, shots, points, serveSuccess = new Ratio { Numerator = servesIn, Denominator = attempts }, doubleFaults, meanShotSpeed = speeds.Count == 0 ? 0 : speeds.Average(), intendedTargets = targets, actualFirstLandings = landings, choices, endReasons = reasons });
                    Console.WriteLine($"{policy,-10} sets={completed}/{seeds.Length} wins={won}/{completed} backhand={backhand}/{selections} ({(selections == 0 ? 0 : 100.0 * backhand / selections):F1}%) speed={(speeds.Count == 0 ? 0 : speeds.Average()):F2} m/s");
                }
                ReplayJson.Save(Get("out", "artifacts/comparison.json"), new { schemaVersion = "1.0", realismCalibrated = false, rows });
                return failed ? 1 : 0;
            }
            case "points":
            {
                int count = int.Parse(Get("count", "1000")); if (count < 1 || count > 100000) throw new ArgumentException("count must be 1..100000");
                var input = Input(options); var initialInput = ReplayJson.Deserialize<MatchInput>(ReplayJson.Serialize(input)); uint initialSeed = input.Seed; var failures = new List<object>(); var reasons = new Dictionary<string, int>();
                int shots = 0; long ticks = 0;
                for (int i = 0; i < count; i++)
                {
                    input.Seed = unchecked(initialSeed + (uint)i); input.Config.FirstServer = i % 2; input.Config.InitialEndA = (i / 2) % 2 == 0 ? -1 : 1;
                    var engine = new MatchEngine(input, 1); var record = engine.Run(); ticks += engine.Tick;
                    if (record.Status != "PointBatchComplete") failures.Add(new { input.Seed, record.Status, record.Diagnostic });
                    shots += record.Stats.Players.Sum(p => p.Shots);
                    foreach (var pair in record.Stats.EndReasons) reasons[pair.Key] = reasons.GetValueOrDefault(pair.Key) + pair.Value;
                }
                ReplayJson.Save(Get("out", "artifacts/points.json"), new { count, initialSeed, seedRule = "initialSeed+i modulo uint32", firstServerRule = "i%2", endARule = "(i/2)%2 == 0 ? -1 : +1", input = initialInput, shots, ticks, failures, endReasons = reasons });
                Console.WriteLine($"POINT_BATCH count={count} completed={count - failures.Count} failures={failures.Count} shots={shots} ticks={ticks}");
                return failures.Count == 0 ? 0 : 1;
            }
            default: throw new ArgumentException("Unknown command: " + args[0]);
        }
    }
    private static MatchRecord RunMatch(MatchInput input, int chunk)
    { var engine = new MatchEngine(input); while (!engine.Finished) engine.AdvanceTicks(chunk); return engine.Record; }
    private static MatchInput Input(Dictionary<string, string> o, bool samePlayers = false)
    {
        var input = new MatchInput { Seed = uint.Parse(o.GetValueOrDefault("seed", "42")) };
        input.Players = new[] { Player(o.GetValueOrDefault("player-a", "baseline"), "A"), Player(o.GetValueOrDefault("player-b", samePlayers ? "baseline" : "defender"), "B") };
        input.Tactics = new[] { Tactics(o.GetValueOrDefault("tactics-a", "balanced")), Tactics(o.GetValueOrDefault("tactics-b", "balanced")) };
        if (o.TryGetValue("config", out var config)) input.Config = ReplayJson.Deserialize<SimConfig>(File.ReadAllText(config));
        if (o.TryGetValue("instructions", out var instructions)) input.Instructions = ReplayJson.Deserialize<List<TacticInstruction>>(File.ReadAllText(instructions));
        input.Surface = o.GetValueOrDefault("surface-model", "legacy") == "legacy" ? null : ImpulseEnvironment(o);
        return input;
    }
    // Legacy (null) preserves the previous multiplicative bounce. Impulse selects the explicit model.
    private static SurfaceEnvironment ImpulseEnvironment(Dictionary<string, string> o)
    {
        string model = o.GetValueOrDefault("surface-model", "impulse");
        if (model != "impulse" && model != "legacy") throw new ArgumentException("Unknown surface model: " + model);
        var environment = new SurfaceEnvironment { Model = BounceModelKind.ImpulseV1 };
        if (o.TryGetValue("profile", out var profilePath)) environment.Profile = ReplayJson.Deserialize<InteractionProfile>(File.ReadAllText(profilePath));
        if (o.TryGetValue("ball", out var ballPath)) environment.Ball = ReplayJson.Deserialize<BallSpec>(File.ReadAllText(ballPath));
        else if (environment.Profile.BallSpecId.Length > 0 && environment.Profile.BallSpecId != environment.Ball.Id)
            throw new ArgumentException("Profile is bound to ball spec '" + environment.Profile.BallSpecId + "'; pass --ball with the matching ball spec");
        if (o.TryGetValue("ball-condition", out var conditionPath)) environment.Condition = ReplayJson.Deserialize<BallCondition>(File.ReadAllText(conditionPath));
        environment.Validate();
        return environment;
    }
    private static PlayerProfile Player(string name, string id)
    {
        var p = File.Exists(name) ? ReplayJson.Deserialize<PlayerProfile>(File.ReadAllText(name)) : PlayerProfile.Preset(name, id);
        p.Id = id; return p;
    }
    private static Tactic Tactics(string name)
    {
        if (File.Exists(name)) return ReplayJson.Deserialize<Tactic>(File.ReadAllText(name));
        return name.ToLowerInvariant() switch
        {
            "balanced" => new Tactic(), "backhand" => new Tactic { Target = TargetStyle.TargetBackhand },
            "safe" => new Tactic { Aggression = Aggression.Safe }, "aggressive" => new Tactic { Aggression = Aggression.Aggressive },
            _ => throw new ArgumentException("Unknown tactic: " + name)
        };
    }
    private static void Print(MatchRecord r, bool points)
    {
        if (points)
            foreach (var e in r.Events.Where(e => e.Kind == "PointEnded"))
            {
                var changed = r.Events.FirstOrDefault(s => s.Sequence > e.Sequence && s.Kind == "ScoreChanged");
                Console.WriteLine($"Point {e.Point,3} t={e.Time,8:F3}s winner={e.PlayerId} reason={e.Reason,-15} score={changed?.State.Score.Display}");
            }
        Console.WriteLine($"STATUS={r.Status} SCORE={r.FinalScore.Display} POINTS={r.FinalScore.PointsPlayed} EVENTS={r.Events.Count} REALISM_CALIBRATED=false");
        if (r.Diagnostic.Length > 0) Console.WriteLine(r.Diagnostic);
    }
}
