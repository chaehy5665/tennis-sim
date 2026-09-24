using TennisSim.Core;

namespace TennisSim.Cli;

// Text prototype of the coaching loop: the user coaches player A, the match pauses at every changeover, shows the
// games since the previous changeover and takes a tactic change. Instructions go through MatchEngine.QueueTactics,
// so the saved replay records them and resimulates exactly. Player B keeps its initial tactic.
public static class Coach
{
    public static MatchRecord Run(MatchInput input, TextReader commands, TextWriter output, bool echo)
    {
        var engine = new MatchEngine(input);
        var names = input.Players.Select(p => p.Name).ToArray();
        output.WriteLine($"You coach A ({names[0]}) against B ({names[1]}). One set. Seed {input.Seed}.");
        output.WriteLine("At each changeover enter a tactic change, e.g. \"t=backhand a=safe s=wide\", or press Enter to keep it.");
        output.WriteLine("t=balanced|backhand   a=safe|balanced|aggressive   s=mixed|wide|body|t   q=keep for the rest of the match");
        int from = 1; bool auto = false;
        while (engine.AdvanceToChangeover())
        {
            var state = engine.State;
            int to = state.Score.PointsPlayed;
            if (!auto)
            {
                Report(output, SegmentStats.Compute(engine.Record, from, to), state, names);
                while (true)
                {
                    output.Write("> ");
                    string? line = commands.ReadLine();
                    if (echo) output.WriteLine(line ?? "");
                    if (line == null || line.Trim() == "q") { auto = true; break; }
                    if (line.Trim().Length == 0) break;
                    if (TryParse(line, state.Tactics[0], out var tactic, out string error))
                    {
                        engine.QueueTactics(0, tactic);
                        output.WriteLine("  applied from the next point: " + Describe(tactic));
                        break;
                    }
                    output.WriteLine("  " + error);
                    if (commands != Console.In) throw new ArgumentException("Invalid coaching command in script: " + line);
                }
            }
            from = to + 1;
        }
        var final = engine.Record;
        if (final.Status == "Completed")
        {
            output.WriteLine();
            Report(output, SegmentStats.Compute(final, 1, final.FinalScore.PointsPlayed), final.Frames[^1], names, whole: true);
            output.WriteLine($"FINAL {final.FinalScore.Display} winner={(final.FinalScore.Winner == 0 ? "A (you)" : "B")} instructions={final.Input.Instructions.Count}");
        }
        else output.WriteLine($"STATUS={final.Status} {final.Diagnostic}");
        return final;
    }

    static void Report(TextWriter o, SegmentStats s, FrameState state, string[] names, bool whole = false)
    {
        o.WriteLine();
        o.WriteLine(whole ? $"=== Match summary ({s.Points} points) ===" : $"=== Changeover: games A {state.Score.Games[0]}-{state.Score.Games[1]} B, points {s.FromPoint}-{s.ToPoint} ===");
        o.WriteLine($"{"",-22}{"A " + names[0],14}{"B " + names[1],14}");
        string Row(string label, Func<SegmentPlayerStats, string> value) => $"{label,-22}{value(s.Players[0]),14}{value(s.Players[1]),14}";
        o.WriteLine(Row("Points won", p => p.PointsWon.ToString()));
        o.WriteLine(Row("Serve points won", p => $"{p.ServePointsWon}/{p.ServePoints}"));
        o.WriteLine(Row("First serves in", p => $"{p.FirstServesIn}/{p.ServePoints}"));
        o.WriteLine(Row("Double faults", p => p.DoubleFaults.ToString()));
        o.WriteLine(Row("Winners", p => p.Winners.ToString()));
        o.WriteLine(Row("Errors FH/BH", p => $"{p.ForehandErrors}/{p.BackhandErrors}"));
        o.WriteLine(Row("Strokes FH/BH", p => $"{p.Forehands}/{p.Backhands}"));
        o.WriteLine($"Mean rally length {s.MeanRallyLength:F1} shots");
        if (!whole) o.WriteLine($"Your tactic: {Describe(state.Tactics[0])}   Opponent: {Describe(state.Tactics[1])}");
    }

    public static string Describe(Tactic t) =>
        $"t={(t.Target == TargetStyle.TargetBackhand ? "backhand" : "balanced")} a={t.Aggression.ToString().ToLowerInvariant()} s={t.Serve.ToString().ToLowerInvariant()}";

    public static bool TryParse(string line, Tactic current, out Tactic tactic, out string error)
    {
        tactic = current.Copy(); error = "";
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split('=');
            if (parts.Length != 2) { error = $"expected key=value, got '{token}'"; return false; }
            string key = parts[0].ToLowerInvariant(), value = parts[1].ToLowerInvariant();
            switch (key)
            {
                case "t" when value == "balanced": tactic.Target = TargetStyle.Balanced; break;
                case "t" when value == "backhand": tactic.Target = TargetStyle.TargetBackhand; break;
                case "a" when Enum.TryParse<Aggression>(value, true, out var a) && Enum.IsDefined(a): tactic.Aggression = a; break;
                case "s" when Enum.TryParse<ServeDirection>(value, true, out var d) && Enum.IsDefined(d): tactic.Serve = d; break;
                default: error = $"unknown setting '{token}'"; return false;
            }
        }
        return true;
    }
}
