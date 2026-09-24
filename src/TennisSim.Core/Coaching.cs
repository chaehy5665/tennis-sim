using System;
using System.Collections.Generic;
using System.Linq;

namespace TennisSim.Core
{
    // What a coach sees at a changeover: one player's results over a range of points, derived only from recorded events.
    public sealed class SegmentPlayerStats
    {
        public string PlayerId { get; set; } = "";
        public int PointsWon { get; set; }
        public int ServePoints { get; set; }
        public int ServePointsWon { get; set; }
        public int FirstServesIn { get; set; }
        public int DoubleFaults { get; set; }
        // Points ended by this player's shot: the opponent could not return it (Winners) or it went out/into the net.
        public int Winners { get; set; }
        public int ForehandErrors { get; set; }
        public int BackhandErrors { get; set; }
        public int Forehands { get; set; }
        public int Backhands { get; set; }
        // Energy in [.15,1] after the last point of the range; -1 when the range holds no completed point.
        public double EnergyAtEnd { get; set; } = -1;
    }
    public sealed class SegmentStats
    {
        public int FromPoint { get; set; }
        public int ToPoint { get; set; }
        public int Points { get; set; }
        public double MeanRallyLength { get; set; }
        public SegmentPlayerStats[] Players { get; set; } = Array.Empty<SegmentPlayerStats>();

        // Points are numbered from 1 as in MatchEvent.Point; both bounds are inclusive.
        public static SegmentStats Compute(MatchRecord record, int fromPoint, int toPoint)
        {
            var ids = record.Stats.Players.Select(p => p.PlayerId).ToArray();
            var result = new SegmentStats { FromPoint = fromPoint, ToPoint = toPoint, Players = ids.Select(id => new SegmentPlayerStats { PlayerId = id }).ToArray() };
            int Index(string id) => Array.IndexOf(ids, id);
            MatchEvent? lastHit = null; bool serveFault = false; int rallyTotal = 0, rallyHits = 0;
            foreach (var e in record.Events)
            {
                if (e.Point < fromPoint || e.Point > toPoint) continue;
                if (e.Kind == "PointStarted") { lastHit = null; serveFault = false; rallyHits = 0; }
                else if (e.Kind == "ServeFault") serveFault = true;
                else if (e.Kind == "BallHit")
                {
                    lastHit = e;
                    if (e.ShotKind != "Serve")
                    {
                        rallyHits++;
                        var hitter = result.Players[Index(e.PlayerId)];
                        if (e.Stroke == "Forehand") hitter.Forehands++; else hitter.Backhands++;
                    }
                }
                else if (e.Kind == "PointEnded")
                {
                    result.Points++;
                    var winner = result.Players[Index(e.PlayerId)];
                    winner.PointsWon++;
                    var server = result.Players[e.State.Score.Server];
                    server.ServePoints++;
                    if (server == winner) server.ServePointsWon++;
                    if (!serveFault) server.FirstServesIn++;
                    if (e.Reason == "DoubleFault") server.DoubleFaults++;
                    else if (lastHit != null)
                    {
                        var shooter = result.Players[Index(lastHit.PlayerId)];
                        if (e.Reason == "UnreturnedBall") shooter.Winners++;
                        else if (lastHit.Stroke == "Forehand") shooter.ForehandErrors++;
                        else if (lastHit.Stroke == "Backhand") shooter.BackhandErrors++;
                    }
                    // Legal serve plus rally hits, matching MatchStats.RallyLengths.
                    rallyTotal += e.Reason == "DoubleFault" ? 0 : rallyHits + 1;
                    for (int i = 0; i < result.Players.Length; i++) result.Players[i].EnergyAtEnd = e.State.Players[i].Energy;
                }
            }
            result.MeanRallyLength = result.Points == 0 ? 0 : (double)rallyTotal / result.Points;
            return result;
        }
    }

    // A rule-based coach for the player the user does not control. It sees only what a human coach sees at a
    // changeover: both profiles, both current tactics and the recorded segment and match statistics. It consumes no
    // randomness; its changes go through MatchEngine.QueueTactics and are recorded like a human's.
    public static class OpponentCoach
    {
        const int MinStrokes = 12;

        public static Tactic? Decide(int self, MatchRecord record, int fromPoint, int toPoint, Tactic[] current, PlayerProfile[] players)
        {
            int other = 1 - self;
            var match = SegmentStats.Compute(record, 1, toPoint).Players;
            var segment = SegmentStats.Compute(record, fromPoint, toPoint).Players;
            var next = current[self].Copy();

            // Target the side where the opponent actually errs more; before enough strokes, trust the scouting profile.
            var o = match[other];
            if (o.Forehands >= MinStrokes && o.Backhands >= MinStrokes)
            {
                double fh = (double)o.ForehandErrors / o.Forehands, bh = (double)o.BackhandErrors / o.Backhands;
                if (bh > fh * 1.25) next.Target = TargetStyle.TargetBackhand;
                else if (fh > bh * 1.25) next.Target = TargetStyle.Balanced;
            }
            else
            {
                var p = players[other];
                double gap = (p.ForehandPower + p.ForehandControl) - (p.BackhandPower + p.BackhandControl);
                next.Target = gap > .1 ? TargetStyle.TargetBackhand : TargetStyle.Balanced;
            }

            // Counter the opponent's visible style (tennissim-mvp-4 balance grid): attack a passive opponent, stay
            // balanced against an aggressive one. A high-control player keeps a steady Safe game otherwise.
            var mine = players[self];
            bool steady = (mine.ForehandControl + mine.BackhandControl) / 2 >= .85;
            switch (current[other].Aggression)
            {
                case Aggression.Safe: next.Aggression = Aggression.Aggressive; break;
                case Aggression.Aggressive: next.Aggression = Aggression.Balanced; break;
                default: next.Aggression = steady ? Aggression.Safe : Aggression.Balanced; break;
            }
            var me = segment[self];

            // A big server commits to the wide serve at the first changeover; any fixed direction that loses most of its
            // serve points in a segment goes back to Mixed, and Mixed is never left again after that first decision.
            if (fromPoint == 1 && mine.ServePower >= .9 && current[self].Serve == ServeDirection.Mixed) next.Serve = ServeDirection.Wide;
            else if (current[self].Serve != ServeDirection.Mixed && me.ServePoints >= 3 && me.ServePointsWon * 2 < me.ServePoints) next.Serve = ServeDirection.Mixed;

            bool changed = next.Target != current[self].Target || next.Aggression != current[self].Aggression || next.Serve != current[self].Serve;
            return changed ? next : null;
        }
    }
}
