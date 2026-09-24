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
    // Why OpponentCoach chose a setting. A and B carry the numbers behind the rule (rates, profile sums), so a UI
    // can explain the change in its own language without re-deriving the rule.
    public enum CoachReasonKind
    {
        TargetMeasuredErrors,   // A = opponent backhand error rate, B = forehand error rate
        TargetScouting,         // A = opponent forehand power+control, B = backhand power+control
        CounterSafe,            // opponent plays Safe
        CounterAggressive,      // opponent plays Aggressive
        SteadyPlayer,           // own mean control >= .85 against a Balanced opponent
        NeutralStyle,           // Balanced against a Balanced opponent
        BigServerWide,          // A = own serve power
        ServeRead               // A = serve points won, B = serve points in the segment
    }
    public sealed class CoachReason
    {
        public CoachReasonKind Kind { get; set; }
        public double A { get; set; }
        public double B { get; set; }
    }
    public sealed class CoachDecision
    {
        public Tactic Tactic { get; set; } = new Tactic();
        // Reasons for the settings that differ from the current tactic, in target, aggression, serve order.
        public List<CoachReason> Reasons { get; set; } = new List<CoachReason>();
    }

    public static class OpponentCoach
    {
        const int MinStrokes = 12;

        public static Tactic? Decide(int self, MatchRecord record, int fromPoint, int toPoint, Tactic[] current, PlayerProfile[] players) =>
            DecideWithReasons(self, record, fromPoint, toPoint, current, players)?.Tactic;

        public static CoachDecision? DecideWithReasons(int self, MatchRecord record, int fromPoint, int toPoint, Tactic[] current, PlayerProfile[] players)
        {
            int other = 1 - self;
            CoachReason? target = null, aggression = null, serve = null;
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
                target = new CoachReason { Kind = CoachReasonKind.TargetMeasuredErrors, A = bh, B = fh };
            }
            else
            {
                var p = players[other];
                double gap = (p.ForehandPower + p.ForehandControl) - (p.BackhandPower + p.BackhandControl);
                next.Target = gap > .1 ? TargetStyle.TargetBackhand : TargetStyle.Balanced;
                target = new CoachReason { Kind = CoachReasonKind.TargetScouting, A = p.ForehandPower + p.ForehandControl, B = p.BackhandPower + p.BackhandControl };
            }

            // Counter the opponent's visible style (tennissim-mvp-4 balance grid): attack a passive opponent, stay
            // balanced against an aggressive one. A high-control player keeps a steady Safe game otherwise.
            var mine = players[self];
            bool steady = (mine.ForehandControl + mine.BackhandControl) / 2 >= .85;
            switch (current[other].Aggression)
            {
                case Aggression.Safe: next.Aggression = Aggression.Aggressive; aggression = new CoachReason { Kind = CoachReasonKind.CounterSafe }; break;
                case Aggression.Aggressive: next.Aggression = Aggression.Balanced; aggression = new CoachReason { Kind = CoachReasonKind.CounterAggressive }; break;
                default:
                    next.Aggression = steady ? Aggression.Safe : Aggression.Balanced;
                    aggression = new CoachReason { Kind = steady ? CoachReasonKind.SteadyPlayer : CoachReasonKind.NeutralStyle, A = (mine.ForehandControl + mine.BackhandControl) / 2 };
                    break;
            }
            var me = segment[self];

            // A big server commits to the wide serve at the first changeover; any fixed direction that loses most of its
            // serve points in a segment goes back to Mixed, and Mixed is never left again after that first decision.
            if (fromPoint == 1 && mine.ServePower >= .9 && current[self].Serve == ServeDirection.Mixed)
            { next.Serve = ServeDirection.Wide; serve = new CoachReason { Kind = CoachReasonKind.BigServerWide, A = mine.ServePower }; }
            else if (current[self].Serve != ServeDirection.Mixed && me.ServePoints >= 3 && me.ServePointsWon * 2 < me.ServePoints)
            { next.Serve = ServeDirection.Mixed; serve = new CoachReason { Kind = CoachReasonKind.ServeRead, A = me.ServePointsWon, B = me.ServePoints }; }

            var decision = new CoachDecision { Tactic = next };
            if (next.Target != current[self].Target) decision.Reasons.Add(target!);
            if (next.Aggression != current[self].Aggression) decision.Reasons.Add(aggression!);
            if (next.Serve != current[self].Serve) decision.Reasons.Add(serve!);
            return decision.Reasons.Count > 0 ? decision : null;
        }
    }
}
