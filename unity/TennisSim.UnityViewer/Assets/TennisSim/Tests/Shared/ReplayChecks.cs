using System;
using System.Linq;

namespace TennisSim.Viewer.Tests
{
    // The exact loader/sampler/controller exercised here also run in Unity.
    public static class ReplayChecks
    {
        public static void Check(bool ok, string message = "Assertion failed") { if (!ok) throw new Exception(message); }
        public static void Near(Position a, Position b, double tolerance = 1e-7)
        { Check(Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance && Math.Abs(a.Z - b.Z) <= tolerance, "Position mismatch (metres)"); }
        static void Same(ReplayState a, ReplayState b) { Near(a.Ball, b.Ball); Near(a.A, b.A); Near(a.B, b.B); Check(a.Point == b.Point && a.Score.Display == b.Score.Display, "Point/score mismatch"); }
        static void Reject(Action action) { try { action(); } catch (FormatException) { return; } throw new Exception("Expected explicit FormatException"); }
        static string ReplaceFirst(string text, string from, string to) { int index = text.IndexOf(from, StringComparison.Ordinal); Check(index >= 0, "Missing fixture token " + from); return text.Substring(0, index) + to + text.Substring(index + from.Length); }
        public static void Run(string json, Action<string, Action> test)
        {
            var d = ReplayLoader.Parse(json); var timeline = new ReplayTimeline(d); var sampler = new ReplayStateSampler(timeline);
            test("Actual seed-42 sample and terminal result", () => { Check(d.Seed == 42 && d.Events.Length == 2309 && d.Frames.Length == 6118); Check(timeline.TotalPoints == 27 && d.FinalScore.Winner == 1 && d.FinalScore.Games.SequenceEqual(new[] { 0, 6 })); });
            test("Unsupported schema and engine", () => { Reject(() => ReplayLoader.Parse(ReplaceFirst(json, "\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"9\""))); Reject(() => ReplayLoader.Parse(json.Replace("tennissim-mvp-1", "unknown"))); });
            test("Required fields never default to zero", () => {
                foreach (var key in new[] { "seed", "time", "x", "y", "z", "point", "players", "ball", "before", "display", "games", "complete", "winner", "sequence", "frames", "finalScore", "config" })
                    Reject(() => ReplayLoader.Parse(ReplaceFirst(json, "\"" + key + "\":", "\"missing_" + key + "\":")));
            });
            test("Malformed JSON, overflow, duplicate keys and wrong types", () => {
                Reject(() => ReplayLoader.Parse(json + "garbage")); Reject(() => ReplayLoader.Parse(json.Substring(0, json.Length - 4)));
                foreach (var value in new[] { "1e999", "NaN", "null", "true", "\"0\"", "01", "-1", "0.5" }) Reject(() => ReplayLoader.Parse(ReplaceFirst(json, "\"seed\":42", "\"seed\":" + value)));
                Reject(() => ReplayLoader.Parse(ReplaceFirst(json, "\"seed\":42", "\"seed\":42,\"seed\":42")));
            });
            test("Invalid time order, event sequence and nonfinite coordinates", () => {
                var copy = ReplayLoader.Parse(json); copy.Frames[1].Time = -1; Reject(() => ReplayValidator.Validate(copy));
                copy = ReplayLoader.Parse(json); copy.Events[2].Sequence = 1; Reject(() => ReplayValidator.Validate(copy));
                copy = ReplayLoader.Parse(json); copy.Events[2].Time = -1; Reject(() => ReplayValidator.Validate(copy));
                copy = ReplayLoader.Parse(json); copy.Frames[0].Ball = new Position(double.NaN, 0, 0); Reject(() => ReplayValidator.Validate(copy));
                copy = ReplayLoader.Parse(json); copy.Frames[0].Ball = new Position(0, double.PositiveInfinity, 0); Reject(() => ReplayValidator.Validate(copy));
            });
            test("First, last and no extrapolation", () => { Same(sampler.Sample(-10), timeline.Keys[0].After); Same(sampler.Sample(timeline.Duration + 10), d.Frames.Last()); });
            test("All recorded snapshot times and explicit same-time precedence", () => {
                foreach (var f in d.Frames) {
                    var lastEvent = d.Events.LastOrDefault(e => e.Time == f.Time);
                    Same(sampler.Sample(f.Time), lastEvent == null ? f : lastEvent.State);
                }
            });
            test("All hit/bounce/net boundaries and before approach", () => {
                foreach (var e in d.Events.Where(e => e.IsMarker)) {
                    var lastEvent = d.Events.Last(x => x.Time == e.Time); Same(sampler.Sample(e.Time), lastEvent.State);
                    var keyIndex = Array.FindIndex(timeline.Keys, k => k.Time == e.Time);
                    if (keyIndex > 0) {
                        var a = timeline.Keys[keyIndex - 1]; var b = timeline.Keys[keyIndex]; double mid = (a.Time + b.Time) / 2;
                        if (!b.Reset && a.After.Point == b.Before.Point && a.After.Phase != "BetweenPoints" && a.After.Phase != "ServeRetry") Near(sampler.Sample(mid).Ball, Position.Lerp(a.After.Ball, b.Before.Ball, (mid - a.Time) / (b.Time - a.Time)));
                    }
                }
            });
            test("Every reset holds previous location until recorded reset time", () => {
                for (int i = 1; i < timeline.Keys.Length; i++) if (timeline.Keys[i].Reset) {
                    var a = timeline.Keys[i - 1]; var b = timeline.Keys[i]; Same(sampler.Sample((a.Time + b.Time) / 2), a.After); Same(sampler.Sample(b.Time), b.After);
                }
            });
            test("Same-time stable event order and one-frame full event traversal", () => {
                var c = new ReplayController(timeline); c.SetPlaying(true); c.Advance(timeline.Duration + 1);
                Check(c.CrossedEvents.Select(e => e.Sequence).SequenceEqual(d.Events.Select(e => e.Sequence))); Check(!c.Playing); Same(c.State, d.Frames.Last());
                c.Advance(1); Check(c.CrossedEvents.Length == 0);
            });
            test("Pause, speed, restart and bidirectional seek restore state", () => {
                var c = new ReplayController(timeline); c.Advance(1); Check(c.Time == 0);
                foreach (double speed in new[] { .25, 1.0, 2.0 }) { c.Restart(); c.SetSpeed(speed); c.SetPlaying(true); c.Advance(1); Check(c.Time == speed); }
                c.Seek(120); var expected = c.State; c.Seek(300); c.Seek(1); c.Seek(120); Same(expected, c.State); Check(c.CrossedEvents.Length == 0);
                c.Restart(); Same(c.State, sampler.Sample(0)); c.SetPlaying(true); c.Advance(0); Check(c.CrossedEvents.Length == d.Events.Count(e => e.Time == 0)); c.Advance(0); Check(c.CrossedEvents.Length == 0);
            });
            test("Event navigation preserves individual same-time events", () => {
                var c = new ReplayController(timeline); c.StepEvent(-1); Check(c.SelectedEvent == 0);
                for (int i = 1; i < d.Events.Length; i++) { c.StepEvent(1); Check(c.SelectedEvent == i && c.Time == d.Events[i].Time); }
                for (int i = d.Events.Length - 2; i >= 0; i--) { c.StepEvent(-1); Check(c.SelectedEvent == i); }
            });
            test("Different frame intervals, complete traversal and identical fixed-time sample", () => {
                foreach (double dt in new[] { 1.0 / 30, 1.0 / 144, .73 }) {
                    var c = new ReplayController(timeline); c.SetPlaying(true); int count = 0;
                    while (c.Playing) { c.Advance(dt); foreach (var e in c.CrossedEvents) Check(e.Sequence == count++); }
                    Check(count == d.Events.Length); c.Seek(71.234567); Same(c.State, sampler.Sample(71.234567));
                }
            });
            test("Coordinate mapping keeps metres, signs and height", () => { var p = new Position(-4.115, 2.65, 11.885); Near(CoordinateMapper.Map(p), p, 0); Check(CoordinateMapper.BallRadius == .0335); });
        }
    }
}
