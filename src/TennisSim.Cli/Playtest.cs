using TennisSim.Core;

namespace TennisSim.Cli;

// Automated version of the Mac coaching playtest. Player A is coached by a policy, player B by OpponentCoach (as in
// the coach UI), for full sets over paired seeds. It answers the playtest questions with numbers instead of a person:
// does deciding at changeovers beat keeping one tactic, is one static tactic always best, does a tactic change show
// in the next segment above segment noise, and how often does the opponent AI reverse itself.
public static class Playtest
{
    public sealed record PolicyRow(string PlayerA, string PlayerB, string Policy, int Sets, int SetsWonA, int PointsWonA,
        int Points, int ChangesA, int ChangesB, int TargetChangesB, int SetsWithTargetReversalB, int Failures);
    // One changeover: what A's policy changed and how the following segment differed from the one before it.
    public sealed record Shift(string Policy, string Axis, string Change, double Before, double After, int PointsBefore, int PointsAfter);
    public sealed record Result(List<PolicyRow> Rows, List<Shift> Shifts, List<int> SegmentPoints);

    // Target x aggression x serve: every tactic a coach can pick at a changeover.
    static readonly Tactic[] AllTactics =
        (from t in new[] { TargetStyle.Balanced, TargetStyle.TargetBackhand }
         from a in new[] { Aggression.Safe, Aggression.Balanced, Aggression.Aggressive }
         from s in new[] { ServeDirection.Mixed, ServeDirection.Wide, ServeDirection.Body, ServeDirection.T }
         select new Tactic { Target = t, Aggression = a, Serve = s }).ToArray();

    // static:* keep one tactic; reader applies the OpponentCoach rules to A (decides from the same screen numbers);
    // random changes to a uniformly drawn tactic at half of the changeovers, which also samples every kind of change.
    public static readonly string[] Policies = BalanceGrid.Rally.Concat(BalanceGrid.Serves).Select(t => "static:" + t.Name)
        .Concat(new[] { "static:strong", "reader", "random" }).ToArray();

    static Tactic Initial(string policy) => policy switch
    {
        "static:strong" => new Tactic { Target = TargetStyle.TargetBackhand, Aggression = Aggression.Aggressive, Serve = ServeDirection.Wide },
        _ when policy.StartsWith("static:") => BalanceGrid.Rally.Concat(BalanceGrid.Serves).Single(t => "static:" + t.Name == policy).Value.Copy(),
        _ => new Tactic()
    };

    // Without a matchup, every preset A plays every balance-grid opponent B.
    public static Result Run(int sets, uint seed, (string A, string B)? matchup = null)
    {
        var jobs = new List<(string A, string B, string Policy)>();
        var pairs = matchup.HasValue ? new[] { matchup.Value } : (from a in BalanceGrid.Presets from b in BalanceGrid.Opponents select (a, b)).ToArray();
        foreach (var (a, b) in pairs) foreach (var p in Policies) jobs.Add((a, b, p));
        var rows = new PolicyRow[jobs.Count]; var shifts = new List<Shift>[jobs.Count]; var segments = new List<int>[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, j =>
        {
            var (pa, pb, policy) = jobs[j];
            int setsA = 0, pointsA = 0, points = 0, changesA = 0, changesB = 0, targetB = 0, reversals = 0, failures = 0;
            shifts[j] = new List<Shift>(); segments[j] = new List<int>();
            for (int i = 0; i < sets; i++)
            {
                uint s = unchecked(seed + (uint)i);
                var input = new MatchInput { Seed = s, Players = new[] { BalanceGrid.Player(pa, "A"), BalanceGrid.Player(pb, "B") }, Tactics = new[] { Initial(policy), new Tactic() } };
                input.Config.FirstServer = i % 2; input.Config.InitialEndA = (i / 2) % 2 == 0 ? -1 : 1;
                // The policy's own draws never touch the engine's random stream.
                var choices = new SeedRandom(s ^ 0x9E3779B9u);
                var engine = new MatchEngine(input); int from = 1, targetChangesThisSet = 0;
                SegmentStats? previous = null; (string Axis, string Change)[] pending = Array.Empty<(string, string)>();
                while (engine.AdvanceToChangeover())
                {
                    var state = engine.State; int to = state.Score.PointsPlayed;
                    var segment = SegmentStats.Compute(engine.Record, from, to);
                    segments[j].Add(segment.Points);
                    if (previous != null)
                        foreach (var (axis, change) in pending)
                        {
                            // A segment without serves or B strokes has no value on that axis.
                            var shift = Measure(policy, axis, change, previous, segment);
                            if (!double.IsNaN(shift.Before) && !double.IsNaN(shift.After)) shifts[j].Add(shift);
                        }
                    var b = OpponentCoach.Decide(1, engine.Record, from, to, state.Tactics, input.Players);
                    if (b != null)
                    {
                        engine.QueueTactics(1, b); changesB++;
                        if (b.Target != state.Tactics[1].Target) { targetB++; targetChangesThisSet++; }
                    }
                    Tactic? a = policy switch
                    {
                        "reader" => OpponentCoach.Decide(0, engine.Record, from, to, state.Tactics, input.Players),
                        "random" => choices.Next() < .5 ? AllTactics[(int)(choices.Next() * AllTactics.Length)].Copy() : null,
                        _ => null
                    };
                    var current = state.Tactics[0];
                    if (a != null && (a.Target != current.Target || a.Aggression != current.Aggression || a.Serve != current.Serve)) { engine.QueueTactics(0, a); changesA++; }
                    else a = current;
                    pending = new[]
                    {
                        ("target", a.Target == current.Target ? "none" : a.Target == TargetStyle.TargetBackhand ? "to-backhand" : "to-balanced"),
                        ("aggression", a.Aggression == current.Aggression ? "none" : a.Aggression > current.Aggression ? "more-aggressive" : "less-aggressive"),
                        ("serve", a.Serve == current.Serve ? "none" : "to-" + a.Serve.ToString().ToLowerInvariant())
                    };
                    previous = segment; from = to + 1;
                }
                var r = engine.Record;
                if (r.Status != "Completed") { failures++; continue; }
                if (targetChangesThisSet >= 2) reversals++;
                if (r.FinalScore.Winner == 0) setsA++;
                pointsA += r.Stats.Players[0].PointsWon; points += r.FinalScore.PointsPlayed;
            }
            rows[j] = new PolicyRow(pa, pb, policy, sets, setsA, pointsA, points, changesA, changesB, targetB, reversals, failures);
        });
        return new Result(rows.ToList(), shifts.SelectMany(x => x).ToList(), segments.SelectMany(x => x).ToList());
    }

    // The number a coach would watch for each axis: B's backhand share for A's target, the mean rally length for A's
    // aggression, A's serve points won share for A's serve direction.
    static Shift Measure(string policy, string axis, string change, SegmentStats before, SegmentStats after)
    {
        double Value(SegmentStats s) => axis switch
        {
            "target" => Share(s.Players[1].Backhands, s.Players[1].Forehands + s.Players[1].Backhands),
            "aggression" => s.MeanRallyLength,
            _ => Share(s.Players[0].ServePointsWon, s.Players[0].ServePoints)
        };
        return new Shift(policy, axis, change, Value(before), Value(after), before.Points, after.Points);
    }
    static double Share(int k, int n) => n == 0 ? double.NaN : (double)k / n;

    public static void Print(Result result, TextWriter o)
    {
        var rows = result.Rows;
        double Pct(int k, int n) => n == 0 ? 0 : 100.0 * k / n;
        o.WriteLine($"PLAYTEST matchups={rows.Select(r => (r.PlayerA, r.PlayerB)).Distinct().Count()} policies={Policies.Length} setsPerCell={rows[0].Sets} failures={rows.Sum(r => r.Failures)}");
        var seg = result.SegmentPoints.OrderBy(x => x).ToList();
        o.WriteLine($"Segment points: median {seg[seg.Count / 2]}, 10-90% {seg[seg.Count / 10]}-{seg[seg.Count * 9 / 10]}");

        o.WriteLine("\n1. Decision value: A point win % (B coached by OpponentCoach)");
        o.WriteLine($"{"A vs B",-28}{"balanced",9}{"best static",29}{"reader",9}{"random",9}{"reader-bal",11}{"reader-best",12}");
        double sumRb = 0, sumRbest = 0; int n = 0; var bestCount = new Dictionary<string, int>();
        foreach (var g in rows.GroupBy(r => (r.PlayerA, r.PlayerB)))
        {
            double P(string p) { var r = g.Single(x => x.Policy == p); return Pct(r.PointsWonA, r.Points); }
            var best = g.Where(r => r.Policy.StartsWith("static:")).OrderByDescending(r => Pct(r.PointsWonA, r.Points)).First();
            string bestName = best.Policy[7..]; bestCount[bestName] = bestCount.GetValueOrDefault(bestName) + 1;
            double bal = P("static:balanced"), bs = P(best.Policy), rd = P("reader");
            sumRb += rd - bal; sumRbest += rd - bs; n++;
            o.WriteLine($"{g.Key.PlayerA + " vs " + g.Key.PlayerB,-28}{bal,9:F1}{bestName,21}{bs,8:F1}{rd,9:F1}{P("random"),9:F1}{rd - bal,11:+0.0;-0.0}{rd - bs,12:+0.0;-0.0}");
        }
        o.WriteLine($"Mean reader - balanced {sumRb / n:+0.0;-0.0} pp, reader - best static {sumRbest / n:+0.0;-0.0} pp");

        o.WriteLine("\n2. Dominant static tactic: matchups where each static tactic scored best");
        o.WriteLine("   " + string.Join(", ", bestCount.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}")));

        o.WriteLine("\n3. Visibility: next segment minus previous segment, by A's change (all policies)");
        foreach (var axis in new[] { "target", "aggression", "serve" })
        {
            var valid = result.Shifts.Where(s => s.Axis == axis).ToList();
            var noise = valid.Where(s => s.Change == "none").Select(s => s.After - s.Before).ToList();
            double sd = Math.Sqrt(noise.Select(d => d * d).Average() - Math.Pow(noise.Average(), 2));
            string unit = axis == "aggression" ? " shots" : "";
            double scale = axis == "aggression" ? 1 : 100;
            o.WriteLine($"   {axis} (watching {(axis == "target" ? "B backhand share %" : axis == "aggression" ? "mean rally length" : "A serve points won %")}): unchanged n={noise.Count} mean {noise.Average() * scale:+0.0;-0.0}{unit}, noise SD {sd * scale:F1}{unit}");
            foreach (var group in valid.Where(s => s.Change != "none").GroupBy(s => s.Change).OrderBy(x => x.Key))
            {
                var d = group.Select(s => s.After - s.Before).ToList(); double mean = d.Average();
                // A change is visible when the shift goes the expected way by more than one noise SD.
                int visible = d.Count(x => Math.Sign(x) == Math.Sign(mean) && Math.Abs(x) > sd);
                o.WriteLine($"     {group.Key,-16} n={d.Count,5}  mean {mean * scale,6:+0.0;-0.0}{unit}  signal/noise {Math.Abs(mean) / sd,4:F2}  beyond 1 SD {Pct(visible, d.Count),5:F1}%");
            }
        }

        o.WriteLine("\n4. Opponent AI stability (B coached, all policies)");
        int sets = rows.Sum(r => r.Sets - r.Failures);
        o.WriteLine($"   changes per set {rows.Sum(r => r.ChangesB) / (double)sets:F2}, target changes per set {rows.Sum(r => r.TargetChangesB) / (double)sets:F2}, sets with a target reversal {Pct(rows.Sum(r => r.SetsWithTargetReversalB), sets):F1}%");
    }
}
