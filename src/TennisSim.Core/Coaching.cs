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
        // Rally shots by where this player aimed them: the opponent's backhand side (shot choice Backhand), forehand
        // side (Forehand), or anything else (OpenCourt, Attack, SafeDeep).
        public AimStats BackhandAim { get; set; } = new AimStats();
        public AimStats ForehandAim { get; set; } = new AimStats();
        public AimStats OtherAim { get; set; } = new AimStats();
        // This player's serve points by the course of the point's first serve (serve choice Wide/Body/T).
        public ServeCourseStats WideServe { get; set; } = new ServeCourseStats();
        public ServeCourseStats BodyServe { get; set; } = new ServeCourseStats();
        public ServeCourseStats TServe { get; set; } = new ServeCourseStats();
        public ServeCourseStats? ServeCourse(string course) => course == "Wide" ? WideServe : course == "Body" ? BodyServe : course == "T" ? TServe : null;
    }
    // What happened to shots aimed at one side. Every shot ends exactly one way: Shots = Winners + Errors +
    // ReplyForehands + ReplyBackhands, where a reply is the opponent's next hit. ReplyErrors counts replies that ended
    // the point in the net or out, so they are a subset of the replies.
    public sealed class AimStats
    {
        public int Shots { get; set; }
        public int Winners { get; set; }
        public int Errors { get; set; }
        public int ReplyForehands { get; set; }
        public int ReplyBackhands { get; set; }
        public int ReplyErrors { get; set; }
    }
    public sealed class ServeCourseStats
    {
        public int Points { get; set; }
        public int FirstServesIn { get; set; }
        public int Won { get; set; }
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
            MatchEvent? lastHit = null, firstServe = null; bool serveFault = false; int rallyTotal = 0, rallyHits = 0;
            var rally = new List<MatchEvent>();
            foreach (var e in record.Events)
            {
                if (e.Point < fromPoint || e.Point > toPoint) continue;
                if (e.Kind == "PointStarted") { lastHit = null; firstServe = null; serveFault = false; rallyHits = 0; rally.Clear(); }
                else if (e.Kind == "ServeFault") serveFault = true;
                else if (e.Kind == "BallHit")
                {
                    lastHit = e;
                    if (e.ShotKind == "Serve") { if (firstServe == null) firstServe = e; } else rally.Add(e);
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
                    var course = firstServe == null ? null : server.ServeCourse(firstServe.Reason);
                    if (course != null) { course.Points++; if (!serveFault) course.FirstServesIn++; if (server == winner) course.Won++; }
                    // Each rally shot ends as a winner, an own error, or the opponent's next hit (a reply).
                    for (int k = 0; k < rally.Count; k++)
                    {
                        var shot = rally[k];
                        var hitter = result.Players[Index(shot.PlayerId)];
                        var aim = shot.Reason == "Backhand" ? hitter.BackhandAim : shot.Reason == "Forehand" ? hitter.ForehandAim : hitter.OtherAim;
                        aim.Shots++;
                        if (k + 1 < rally.Count)
                        {
                            if (rally[k + 1].Stroke == "Backhand") aim.ReplyBackhands++; else aim.ReplyForehands++;
                            if (k + 2 == rally.Count && (e.Reason == "Out" || e.Reason == "Net")) aim.ReplyErrors++;
                        }
                        else if (e.Reason == "UnreturnedBall") aim.Winners++;
                        else aim.Errors++;
                    }
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
        SteadyPlayer,           // no longer chosen: own mean control >= .85 against a Balanced opponent
        NeutralStyle,           // Balanced against a Balanced opponent
        BigServerWide,          // A = own serve power
        ServeRead,              // A = serve points won, B = serve points in the segment
        HoldStyle,              // the opponent changed its aggression at the last changeover; Balanced until it holds
        TryStyle,               // From = the rule's aggression, To = the one tried; A = own point share with From, B = its points
        MeasuredStyle           // From = the rule's aggression, To = the chosen one; A = own point share with To, B = with From
    }
    public sealed class CoachReason
    {
        public CoachReasonKind Kind { get; set; }
        public double A { get; set; }
        public double B { get; set; }
        // The aggressions a style reason compares (TryStyle, MeasuredStyle).
        public Aggression From { get; set; }
        public Aggression To { get; set; }
    }
    public sealed class CoachDecision
    {
        public Tactic Tactic { get; set; } = new Tactic();
        // Reasons for the settings that differ from the current tactic, in target, aggression, serve order.
        public List<CoachReason> Reasons { get; set; } = new List<CoachReason>();
    }

    public static class OpponentCoach
    {
        // Target: measured error rates count only with enough strokes on both sides and enough errors in total. The
        // target then changes only when the rates differ by more than one standard error against the current setting,
        // so a single error cannot flip it back and forth.
        const int MinStrokes = 12, MinErrors = 6;
        const double SwitchZ = 1.0;
        // Aggression trials: when the rule's style has lost clearly over enough points against the opponent's current
        // style, try each other style for as many points and keep one that measured clearly better.
        const int TrialPoints = 10;
        const double TrialLosing = .42, TrialMargin = .1;

        public static Tactic? Decide(int self, MatchRecord record, int fromPoint, int toPoint, Tactic[] current, PlayerProfile[] players) =>
            DecideWithReasons(self, record, fromPoint, toPoint, current, players)?.Tactic;

        public static CoachDecision? DecideWithReasons(int self, MatchRecord record, int fromPoint, int toPoint, Tactic[] current, PlayerProfile[] players)
        {
            int other = 1 - self;
            CoachReason? target = null, aggression = null, serve = null;
            var match = SegmentStats.Compute(record, 1, toPoint).Players;
            var segment = SegmentStats.Compute(record, fromPoint, toPoint).Players;
            var next = current[self].Copy();

            // Target the side where the opponent actually errs more; before enough evidence, trust the scouting profile.
            var o = match[other];
            int errors = o.ForehandErrors + o.BackhandErrors;
            if (o.Forehands >= MinStrokes && o.Backhands >= MinStrokes && errors >= MinErrors)
            {
                double fh = (double)o.ForehandErrors / o.Forehands, bh = (double)o.BackhandErrors / o.Backhands;
                double pooled = (double)errors / (o.Forehands + o.Backhands);
                double se = Math.Sqrt(pooled * (1 - pooled) * (1.0 / o.Forehands + 1.0 / o.Backhands));
                double z = se > 0 ? (bh - fh) / se : 0;
                if (current[self].Target == TargetStyle.TargetBackhand) { if (z < -SwitchZ) next.Target = TargetStyle.Balanced; }
                else if (z > SwitchZ) next.Target = TargetStyle.TargetBackhand;
                target = new CoachReason { Kind = CoachReasonKind.TargetMeasuredErrors, A = bh, B = fh };
            }
            else
            {
                var p = players[other];
                double gap = (p.ForehandPower + p.ForehandControl) - (p.BackhandPower + p.BackhandControl);
                next.Target = gap > .1 ? TargetStyle.TargetBackhand : TargetStyle.Balanced;
                target = new CoachReason { Kind = CoachReasonKind.TargetScouting, A = p.ForehandPower + p.ForehandControl, B = p.BackhandPower + p.BackhandControl };
            }

            // Counter the opponent's visible style (balance grid): attack a passive opponent, stay balanced against an
            // aggressive one. A style the opponent took only at the last changeover is not countered yet: the human
            // decides after this coach, so countering it at once would let them switch again and punish the counter.
            var mine = players[self];
            var own = AggressionByPoint(record, self, toPoint);
            var theirs = AggressionByPoint(record, other, toPoint);
            var shown = current[other].Aggression;
            if (fromPoint > 1 && theirs[fromPoint - 1] != shown)
            { next.Aggression = Aggression.Balanced; aggression = new CoachReason { Kind = CoachReasonKind.HoldStyle }; }
            else switch (shown)
            {
                case Aggression.Safe: next.Aggression = Aggression.Aggressive; aggression = new CoachReason { Kind = CoachReasonKind.CounterSafe }; break;
                case Aggression.Aggressive: next.Aggression = Aggression.Balanced; aggression = new CoachReason { Kind = CoachReasonKind.CounterAggressive }; break;
                default: next.Aggression = Aggression.Balanced; aggression = new CoachReason { Kind = CoachReasonKind.NeutralStyle }; break;
            }

            // The rule does not know which style suits its own player (a slow touch player must attack, a heavy but
            // erratic hitter must stay safe), so it measures: own point share per own style, over the points where the
            // opponent played its current style.
            var won = new int[3]; var played = new int[3]; string selfId = record.Stats.Players[self].PlayerId;
            foreach (var e in record.Events)
                if (e.Kind == "PointEnded" && e.Point <= toPoint && theirs[e.Point] == shown)
                { int a = (int)own[e.Point]; played[a]++; if (e.PlayerId == selfId) won[a]++; }
            double Share(int a) => played[a] == 0 ? 0 : (double)won[a] / played[a];
            int rule = (int)next.Aggression, now = (int)current[self].Aggression;
            if (played[rule] >= TrialPoints)
            {
                int best = rule;
                for (int a = 0; a < 3; a++) if (played[a] >= TrialPoints && Share(a) > Share(best)) best = a;
                if (best != rule && Share(best) > Share(rule) + TrialMargin)
                {
                    next.Aggression = (Aggression)best;
                    aggression = new CoachReason { Kind = CoachReasonKind.MeasuredStyle, From = (Aggression)rule, To = (Aggression)best, A = Share(best), B = Share(rule) };
                }
                else if (Share(rule) < TrialLosing)
                {
                    // Finish a running trial before starting the next one.
                    if (now != rule && played[now] < TrialPoints) next.Aggression = (Aggression)now;
                    else foreach (var a in new[] { Aggression.Balanced, Aggression.Aggressive, Aggression.Safe })
                            if ((int)a != rule && played[(int)a] < TrialPoints)
                            {
                                next.Aggression = a;
                                aggression = new CoachReason { Kind = CoachReasonKind.TryStyle, From = (Aggression)rule, To = a, A = Share(rule), B = played[rule] };
                                break;
                            }
                }
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

        // The aggression a player used in each point 1..toPoint (index 0 unused), from the input and the applied
        // instructions in the record.
        static Aggression[] AggressionByPoint(MatchRecord record, int player, int toPoint)
        {
            var result = new Aggression[toPoint + 1];
            var value = record.Input.Tactics[player].Aggression; int point = 1;
            foreach (var i in record.InstructionHistory)
            {
                if (i.Player != player || i.AppliedPoint <= 0) continue;
                for (; point < i.AppliedPoint && point <= toPoint; point++) result[point] = value;
                value = i.Value.Aggression;
            }
            for (; point <= toPoint; point++) result[point] = value;
            return result;
        }
    }
}
