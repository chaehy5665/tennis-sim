using System;
using System.Linq;

namespace TennisSim.Viewer
{
    public static class ReplayValidator
    {
        static void Require(bool ok, string reason) { if (!ok) throw new FormatException("Invalid replay: " + reason); }
        static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        static void PositionValid(Position p) => Require(Finite(p.X) && Finite(p.Y) && Finite(p.Z) && Math.Abs(p.X) <= float.MaxValue && Math.Abs(p.Y) <= float.MaxValue && Math.Abs(p.Z) <= float.MaxValue, "non-finite/unrenderable position");
        static void Score(ReplayScore s)
        {
            Require(s != null && !string.IsNullOrEmpty(s.Display), "score/display missing");
            Require(s.PointsPlayed >= 0 && s.Games != null && s.Games.Length == 2 && s.Games.All(g => g >= 0), "score values");
            Require(s.Complete ? s.Winner == 0 || s.Winner == 1 : s.Winner == -1, "winner/completion mismatch");
        }
        static void State(ReplayState s)
        {
            Require(s != null, "missing state");
            Require(Finite(s.Time) && s.Time >= 0 && s.Point >= 1, "state time/point");
            Require(new[] { "BetweenPoints", "ServePreparation", "Rally", "ServeRetry", "Complete" }.Contains(s.Phase), "unknown phase");
            PositionValid(s.Ball); PositionValid(s.A); PositionValid(s.B); Score(s.Score);
        }
        public static void Validate(ReplayData d)
        {
            Require(d != null && d.Frames != null && d.Events != null && d.Frames.Length > 0 && d.Events.Length > 0, "frames/events missing");
            Require(Finite(d.TickSeconds) && d.TickSeconds > 0, "tickSeconds"); Score(d.FinalScore);
            Require(new[] { "Completed", "PointBatchComplete", "SimulationLimitExceeded" }.Contains(d.Status), "unfinished/unknown status");
            double previous = -1;
            foreach (var f in d.Frames) { State(f); Require(f.Time >= previous, "frames not ordered"); previous = f.Time; }
            previous = -1; int starts = 0, ends = 0, changes = 0;
            var kinds = new[] { "PlayersRepositioned", "PointStarted", "ServeStarted", "ContactPrepared", "ShotPlanned", "BallHit", "BallBounced", "NetTouched", "ServeFault", "ServeLet", "PointEnded", "ScoreChanged", "EndsChanged", "TacticsApplied", "SimulationLimitExceeded" };
            for (int i = 0; i < d.Events.Length; i++)
            {
                var e = d.Events[i]; Require(e != null, "null event"); State(e.State);
                Require(Finite(e.Time) && e.Time >= previous && e.Time == e.State.Time && e.Point == e.State.Point && e.Sequence == i, "event time/sequence/state mismatch at " + i);
                Require(kinds.Contains(e.Kind), "unknown event kind " + e.Kind);
                if (e.Before != null) { State(e.Before); Require(e.Before.Time == e.Time && e.Before.Point == e.Point, "before mismatch"); }
                Require(!e.IsMarker || e.Before != null, "contact boundary needs before state");
                if (e.Kind == "PointStarted") { starts++; Require(e.Point == starts && starts == ends + 1, "point start order"); }
                if (e.Kind == "PointEnded") { ends++; Require(e.Point == ends && ends == starts, "point end order"); }
                if (e.Kind == "ScoreChanged") { changes++; Require(changes == ends && e.State.Score.PointsPlayed == changes, "recorded score order"); }
                previous = e.Time;
            }
            Require(starts > 0 && ends == changes && ends == d.FinalScore.PointsPlayed, "point/score count mismatch");
            Require(d.Frames[d.Frames.Length - 1].Time >= previous, "missing terminal frame");
            Require(d.Status != "Completed" || (starts == ends && d.FinalScore.Complete), "completed result mismatch");
            var last = d.Frames[d.Frames.Length - 1].Score;
            Require(last.Display == d.FinalScore.Display && last.PointsPlayed == d.FinalScore.PointsPlayed && last.Complete == d.FinalScore.Complete && last.Winner == d.FinalScore.Winner && last.Games.SequenceEqual(d.FinalScore.Games), "final frame/result mismatch");
        }
    }
}
