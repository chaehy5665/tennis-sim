using System;
using System.Collections.Generic;
using TennisSim.Core;

namespace TennisSim.Coach
{
    public enum CoachPhase { PreMatch, Playing, Changeover, Finished }

    // One completed point, collected once from the recorded events.
    public sealed class PointSummary
    {
        public int Point;
        public int Winner;
        public int Server;
        public string Reason = "";
        // The last hit of the point: its player (-1 for a double fault), shot kind, stroke and candidate name.
        public int LastHitter = -1;
        public string LastShotKind = "";
        public string LastStroke = "";
        public string LastChoice = "";
        public int GamesA;
        public int GamesB;
    }

    // A stretch of points between changeovers with the tactics that were in force during it.
    public sealed class CoachSegment
    {
        public int FromPoint;
        public int ToPoint;
        public int FirstGame;
        public int LastGame;
        public Tactic TacticA;
        public Tactic TacticB;
        public CoachDecision OpponentChangeAtEnd;
        // What the opponent coach noticed at the end without changing its tactic (notes only), or null.
        public CoachDecision OpponentNoteAtEnd;
    }

    // Drives one coached set: pre-match tactic, play until each changeover, take the user's change, finish.
    // The opponent (B) is coached by OpponentCoach from the same view the user gets. Everything the screens show is
    // read from the engine record; this class never decides an outcome.
    public sealed class CoachSession
    {
        public MatchInput Input { get; private set; }
        public bool AdaptiveOpponent { get; }
        public CoachPhase Phase { get; private set; } = CoachPhase.PreMatch;
        public MatchEngine Engine { get; private set; }
        public List<PointSummary> Points { get; } = new List<PointSummary>();
        public List<CoachSegment> Segments { get; } = new List<CoachSegment>();
        public CoachSegment Current => Segments.Count == 0 ? null : Segments[Segments.Count - 1];
        // The opponent's change taken at the current changeover, or null.
        public CoachDecision OpponentChange { get; private set; }
        // What the opponent coach noticed at the current changeover while keeping its tactic (notes only), or null.
        public CoachDecision OpponentNote { get; private set; }
        double pendingTicks;
        int scanned;
        int[] games = new int[2];

        public CoachSession(MatchInput input, bool adaptiveOpponent = true)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            AdaptiveOpponent = adaptiveOpponent;
        }

        public void Start(Tactic initial)
        {
            if (Phase != CoachPhase.PreMatch) throw new InvalidOperationException("Match already started");
            var input = new MatchInput { Seed = Input.Seed, Config = Input.Config.Copy(), Players = new[] { Input.Players[0].Copy(), Input.Players[1].Copy() },
                Tactics = new[] { initial.Copy(), Input.Tactics[1].Copy() }, Surface = Input.Surface };
            Engine = new MatchEngine(input);
            Input = input;
            Segments.Add(new CoachSegment { FromPoint = 1, FirstGame = 1, TacticA = initial.Copy(), TacticB = Input.Tactics[1].Copy() });
            Phase = CoachPhase.Playing;
        }

        // Plays simSeconds of match time (tick quantised, remainder carried over) or until a changeover or the end.
        public ChangeoverStop Advance(double simSeconds)
        {
            if (Phase != CoachPhase.Playing) throw new InvalidOperationException("Not playing");
            if (double.IsNaN(simSeconds) || simSeconds < 0) throw new ArgumentException("Invalid time step");
            pendingTicks += simSeconds / Input.Config.TickSeconds;
            long ticks = (long)Math.Floor(pendingTicks);
            pendingTicks -= ticks;
            return ticks == 0 ? ChangeoverStop.TickBudget : Stop(Engine.AdvanceUntilChangeover(ticks));
        }

        // Skips straight to the next changeover or the end of the match.
        public ChangeoverStop AdvanceToNextStop()
        {
            if (Phase != CoachPhase.Playing) throw new InvalidOperationException("Not playing");
            return Stop(Engine.AdvanceUntilChangeover(long.MaxValue));
        }

        // Leaves the changeover. A tactic equal to the current one is not sent, so the record holds real changes only.
        public void Resume(Tactic change)
        {
            if (Phase != CoachPhase.Changeover) throw new InvalidOperationException("Not at a changeover");
            var now = Engine.State.Tactics;
            var nextA = now[0].Copy();
            if (change != null && !Same(change, now[0])) { Engine.QueueTactics(0, change); nextA = change.Copy(); }
            var nextB = OpponentChange != null ? OpponentChange.Tactic.Copy() : now[1].Copy();
            Segments.Add(new CoachSegment { FromPoint = Current.ToPoint + 1, FirstGame = Current.LastGame + 1, TacticA = nextA, TacticB = nextB });
            OpponentChange = null;
            OpponentNote = null;
            Phase = CoachPhase.Playing;
        }

        public static bool Same(Tactic a, Tactic b) => a.Target == b.Target && a.Aggression == b.Aggression && a.Serve == b.Serve;

        ChangeoverStop Stop(ChangeoverStop stop)
        {
            Collect();
            if (stop == ChangeoverStop.Changeover)
            {
                pendingTicks = 0;
                var state = Engine.State;
                Current.ToPoint = state.Score.PointsPlayed;
                Current.LastGame = state.Score.Games[0] + state.Score.Games[1];
                var assessed = AdaptiveOpponent
                    ? OpponentCoach.Assess(1, Engine.Record, Current.FromPoint, Current.ToPoint, state.Tactics, Input.Players)
                    : null;
                // Only a real change is queued; a note alone leaves the record untouched.
                OpponentChange = assessed != null && assessed.Changed ? assessed : null;
                OpponentNote = assessed != null && !assessed.Changed && assessed.Notes.Count > 0 ? assessed : null;
                if (OpponentChange != null) Engine.QueueTactics(1, OpponentChange.Tactic);
                Current.OpponentChangeAtEnd = OpponentChange;
                Current.OpponentNoteAtEnd = OpponentNote;
                Phase = CoachPhase.Changeover;
            }
            else if (stop == ChangeoverStop.Finished)
            {
                var final = Engine.Record.FinalScore;
                Current.ToPoint = final.PointsPlayed;
                Current.LastGame = final.Games[0] + final.Games[1];
                Phase = CoachPhase.Finished;
            }
            return stop;
        }

        // Reads new events once: each PointEnded becomes a PointSummary with the games score after it.
        void Collect()
        {
            var events = Engine.Record.Events;
            MatchEvent lastHit = null;
            for (int i = scanned; i < events.Count; i++)
            {
                var e = events[i];
                if (e.Kind == "PointStarted") lastHit = null;
                else if (e.Kind == "BallHit") lastHit = e;
                else if (e.Kind == "PointEnded")
                {
                    // A point never spans two Collect calls without its hits: search back if the scan started mid-point.
                    if (lastHit == null) for (int j = i - 1; j >= 0 && events[j].Point == e.Point; j--) if (events[j].Kind == "BallHit") { lastHit = events[j]; break; }
                    var summary = new PointSummary { Point = e.Point, Winner = e.PlayerId == Engine.Record.Stats.Players[0].PlayerId ? 0 : 1, Server = e.State.Score.Server, Reason = e.Reason };
                    if (e.Reason != "DoubleFault" && lastHit != null)
                    {
                        summary.LastHitter = lastHit.PlayerId == Engine.Record.Stats.Players[0].PlayerId ? 0 : 1;
                        summary.LastShotKind = lastHit.ShotKind; summary.LastStroke = lastHit.Stroke; summary.LastChoice = lastHit.Reason;
                    }
                    Points.Add(summary);
                }
                else if (e.Kind == "ScoreChanged")
                {
                    games = (int[])e.State.Score.Games.Clone();
                    var last = Points[Points.Count - 1];
                    last.GamesA = games[0]; last.GamesB = games[1];
                }
            }
            scanned = events.Count;
        }
    }
}
