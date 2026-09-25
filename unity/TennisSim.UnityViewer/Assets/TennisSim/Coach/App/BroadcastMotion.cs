using System;
using System.Collections.Generic;
using TennisSim.Core;

namespace TennisSim.Coach
{
    // The nine motion states of the broadcast view (design system broadcast.md, "동작 상태").
    public enum MotionState { Idle, Serve, Move, Prepare, Strike, Miss, Recover, AfterPoint, Reposition }

    // One stretch of a player's motion. End is +infinity while the stretch is still open.
    public sealed class MotionSpan
    {
        public MotionState State;
        public double Start, End = double.PositiveInfinity;
        public int ActionId;
        // Prepare/Move carry the stroke announced by ContactPrepared (engine v6+, empty before); Strike carries the
        // stroke actually played. ContactTime is the BallHit time for Strike, NaN otherwise.
        public string PlannedStroke = "", Stroke = "";
        public double ContactTime = double.NaN;
        public MotionSpan Copy() => (MotionSpan)MemberwiseClone();
    }

    // Per-player motion over a match, plus the instants the renderer must cut instead of interpolating.
    public sealed class MotionTrack
    {
        public readonly List<MotionSpan>[] Spans = { new List<MotionSpan>(), new List<MotionSpan>() };
        public readonly List<double> Cuts = new List<double>();

        public MotionSpan SpanAt(int player, double time)
        {
            MotionSpan found = null;
            foreach (var s in Spans[player]) if (s.Start <= time && (time < s.End || (s.End == s.Start && s.Start == time && found == null))) found = s;
            return found;
        }
        public MotionState StateAt(int player, double time) => SpanAt(player, time)?.State ?? MotionState.Idle;
    }

    // Reads recorded events in order and derives each player's motion. It only decides the shape and length of a
    // motion: when a ball is struck, whether it is reached and who wins come from the events as recorded. Every
    // transition is decided from events at or before its time, so a live renderer gets the same states as a replay.
    public static class BroadcastMotion
    {
        sealed class Pending { public double Time; public MotionState State; }

        // upTo lets a live renderer ask for the motion at the current playback time: timed transitions up to it
        // are applied even when no event has arrived since. A finished record needs no argument.
        public static MotionTrack Classify(IReadOnlyList<MatchEvent> events, double upTo = double.PositiveInfinity)
        {
            var track = new MotionTrack();
            var pending = new[] { new List<Pending>(), new List<Pending>() };

            int Index(MatchEvent e, string id)
            {
                var players = e.State.Players;
                for (int i = 0; i < players.Length; i++) if (players[i].Id == id) return i;
                return -1;
            }
            MotionSpan Current(int p) => track.Spans[p].Count == 0 ? null : track.Spans[p][track.Spans[p].Count - 1];
            MotionSpan Begin(int p, MotionState state, double time, MotionSpan template = null)
            {
                var cur = Current(p);
                if (cur != null && double.IsPositiveInfinity(cur.End)) cur.End = Math.Max(cur.Start, time);
                var span = template?.Copy() ?? new MotionSpan();
                span.State = state; span.Start = time; span.End = double.PositiveInfinity;
                if (state != MotionState.Strike) span.ContactTime = double.NaN;
                track.Spans[p].Add(span);
                return span;
            }
            // Timed transitions (prepare lead, follow-through end, a missed contact) fire before any later event.
            void Flush(double until)
            {
                for (int p = 0; p < 2; p++)
                {
                    pending[p].Sort((a, b) => a.Time.CompareTo(b.Time));
                    while (pending[p].Count > 0 && pending[p][0].Time <= until)
                    {
                        var next = pending[p][0]; pending[p].RemoveAt(0);
                        Begin(p, next.State, next.Time, Current(p));
                    }
                }
            }
            void Schedule(int p, MotionState state, double time) => pending[p].Add(new Pending { Time = time, State = state });

            foreach (var e in events)
            {
                Flush(e.Time);
                if (track.Spans[0].Count == 0 && e.Kind != "PlayersRepositioned") { Begin(0, MotionState.Idle, e.Time); Begin(1, MotionState.Idle, e.Time); }
                int who = string.IsNullOrEmpty(e.PlayerId) ? -1 : Index(e, e.PlayerId);
                switch (e.Kind)
                {
                    case "PlayersRepositioned":
                        // A reset, not a walk: the renderer cuts here. The server then winds up for the serve.
                        track.Cuts.Add(e.Time);
                        int server = e.State.Score.Server;
                        for (int p = 0; p < 2; p++)
                        {
                            pending[p].Clear();
                            var cut = Begin(p, MotionState.Reposition, e.Time);
                            cut.End = e.Time;
                            Begin(p, p == server ? MotionState.Serve : MotionState.Idle, e.Time);
                        }
                        break;
                    case "ContactPrepared":
                        if (who < 0) break;
                        pending[who].Clear();
                        var move = Begin(who, MotionState.Move, e.Time);
                        move.ActionId = e.ActionId; move.PlannedStroke = e.Stroke ?? "";
                        double predicted = e.PredictedContactTime ?? e.Time;
                        double lead = Math.Max(e.Time, predicted - BroadcastSpec.PrepareLead);
                        if (e.Reason == "UnreachableContact") Schedule(who, MotionState.Miss, lead);
                        else { Schedule(who, MotionState.Prepare, lead); Schedule(who, MotionState.Miss, predicted + BroadcastSpec.MissGrace); }
                        break;
                    case "BallHit":
                        if (who < 0) break;
                        pending[who].Clear();
                        var before = Current(who);
                        var strike = Begin(who, MotionState.Strike, e.Time);
                        strike.ActionId = e.ActionId; strike.Stroke = e.Stroke; strike.ContactTime = e.Time;
                        strike.PlannedStroke = before != null && before.ActionId == e.ActionId ? before.PlannedStroke : "";
                        Schedule(who, MotionState.Recover, e.Time + BroadcastSpec.FollowThrough);
                        break;
                    case "ServeFault":
                        // The receiver lets a fault go; both wait for the reset that follows.
                        for (int p = 0; p < 2; p++) { pending[p].Clear(); if (Current(p).State != MotionState.Idle) Begin(p, MotionState.Idle, e.Time); }
                        break;
                    case "PointEnded":
                        for (int p = 0; p < 2; p++) { pending[p].Clear(); Begin(p, MotionState.AfterPoint, e.Time); }
                        break;
                }
            }
            Flush(upTo);
            return track;
        }
    }
}
