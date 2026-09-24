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
                }
            }
            result.MeanRallyLength = result.Points == 0 ? 0 : (double)rallyTotal / result.Points;
            return result;
        }
    }
}
