using TennisSim.Core;

namespace TennisSim.Cli;

// Tactic balance diagnosis. Each cell plays runs of RunPoints consecutive points through actual Core, so fatigue and
// pattern reading build up within a run. Every cell reuses the same seed list (common random numbers), so differences
// between cells come from the tactic or player change rather than from different draws.
public static class BalanceGrid
{
    public sealed record Cell(string Experiment, string PlayerA, string PlayerB, string TacticA, string TacticB,
        int Points, int PointsWonA, double WinRateA, double WilsonLow, double WilsonHigh,
        int ServePoints, int ServeWonA, int ReturnPoints, int ReturnWonA, double MeanShots,
        Dictionary<string, int> EndReasons, int RallyLimits, int Failures);

    public const int RunPoints = 10;
    // Every archetype plays every archetype (docs/PLAYER_TYPES.md). The legacy server and defender presets are out of
    // the roster's strength band, so tactics barely matter in their matchups; they are no longer in the grid.
    internal static readonly string[] Presets = PlayerProfile.Archetypes;
    internal static readonly string[] Opponents = PlayerProfile.Archetypes;
    // Any preset name, plus strong-backhand: the older name of the backhander (same numbers, different display name).
    internal static bool Known(string name) => name == "strong-backhand" || PlayerProfile.Archetypes.Contains(name) || PlayerProfile.LegacyPresets.Contains(name);
    public static PlayerProfile Player(string name, string id)
    {
        if (name != "strong-backhand") return PlayerProfile.Preset(name, id);
        var p = PlayerProfile.Preset("baseline", id); p.Name = "StrongBackhand";
        (p.ForehandPower, p.BackhandPower) = (p.BackhandPower, p.ForehandPower);
        (p.ForehandControl, p.BackhandControl) = (p.BackhandControl, p.ForehandControl);
        return p;
    }
    internal static readonly (string Name, Tactic Value)[] Rally =
    {
        ("safe", new Tactic { Aggression = Aggression.Safe }),
        ("balanced", new Tactic()),
        ("aggressive", new Tactic { Aggression = Aggression.Aggressive }),
        ("backhand-safe", new Tactic { Target = TargetStyle.TargetBackhand, Aggression = Aggression.Safe }),
        ("backhand", new Tactic { Target = TargetStyle.TargetBackhand }),
        ("backhand-aggressive", new Tactic { Target = TargetStyle.TargetBackhand, Aggression = Aggression.Aggressive }),
    };
    internal static readonly (string Name, Tactic Value)[] Serves =
    {
        ("serve-wide", new Tactic { Serve = ServeDirection.Wide }),
        ("serve-body", new Tactic { Serve = ServeDirection.Body }),
        ("serve-t", new Tactic { Serve = ServeDirection.T }),
    };

    public static List<Cell> Run(int count, uint seed)
    {
        var jobs = new List<(string Experiment, string A, string B, string TacticAName, Tactic TacticA, string TacticBName, Tactic TacticB)>();
        // 1. Every tactic of A against a balanced opponent, for every player matchup.
        foreach (var a in Presets) foreach (var b in Opponents) foreach (var t in Rally.Concat(Serves))
            jobs.Add(("vs-balanced", a, b, t.Name, t.Value, "balanced", new Tactic()));
        // 2. Mirror players, rally tactic against rally tactic: does a counter structure exist?
        foreach (var t in Rally) foreach (var u in Rally)
            jobs.Add(("mirror-payoff", "baseline", "baseline", t.Name, t.Value, u.Name, u.Value));

        var cells = new Cell[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, j =>
        {
            var job = jobs[j];
            int won = 0, serve = 0, serveWon = 0, ret = 0, retWon = 0, shots = 0, limits = 0, failures = 0;
            var reasons = new Dictionary<string, int>();
            int runs = (count + RunPoints - 1) / RunPoints;
            for (int i = 0; i < runs; i++)
            {
                var input = new MatchInput
                {
                    Seed = unchecked(seed + (uint)i),
                    Players = new[] { Player(job.A, "A"), Player(job.B, "B") },
                    Tactics = new[] { job.TacticA.Copy(), job.TacticB.Copy() },
                };
                input.Config.FirstServer = i % 2; input.Config.InitialEndA = (i / 2) % 2 == 0 ? -1 : 1;
                var record = new MatchEngine(input, RunPoints).Run();
                foreach (var e in record.Events.Where(e => e.Kind == "PointEnded"))
                {
                    bool aWon = e.PlayerId == "A";
                    if (aWon) won++;
                    if (e.State.Score.Server == 0) { serve++; if (aWon) serveWon++; } else { ret++; if (aWon) retWon++; }
                }
                shots += record.Stats.RallyLengths.Sum();
                foreach (var pair in record.Stats.EndReasons) reasons[pair.Key] = reasons.GetValueOrDefault(pair.Key) + pair.Value;
                // A point still in play at MaxPointTicks (200 s) is a rally that neither player can end: a balance finding,
                // not a crash. It ends the run, is counted separately and is excluded from the win rate.
                if (record.Status == "SimulationLimitExceeded" && record.Diagnostic.Contains("Maximum point duration")) limits++;
                else if (record.Status != "PointBatchComplete" && record.Status != "Completed") failures++;
            }
            int n = serve + ret;
            var (low, high) = Wilson(won, n);
            cells[j] = new Cell(job.Experiment, job.A, job.B, job.TacticAName, job.TacticBName, n, won, n == 0 ? 0 : (double)won / n,
                low, high, serve, serveWon, ret, retWon, n == 0 ? 0 : (double)shots / n, reasons, limits, failures);
        });
        return cells.ToList();
    }

    // 95% Wilson score interval for a binomial proportion.
    static (double Low, double High) Wilson(int k, int n)
    {
        if (n == 0) return (0, 0);
        const double z = 1.959963984540054;
        double p = (double)k / n, d = 1 + z * z / n, c = p + z * z / (2 * n), m = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n));
        return ((c - m) / d, (c + m) / d);
    }
}
