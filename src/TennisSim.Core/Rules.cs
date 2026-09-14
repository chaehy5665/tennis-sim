using System;

namespace TennisSim.Core
{
    public sealed class ScoreState
    {
        public int[] Games { get; set; } = new int[2];
        public int[] Points { get; set; } = new int[2];
        public int Server { get; set; }
        public bool TieBreak { get; set; }
        public bool Complete { get; set; }
        public int Winner { get; set; } = -1;
        public int EndA { get; set; } = -1;
        public int PointsPlayed { get; set; }
        public string Display { get; set; } = "";
    }

    public sealed class Scoring
    {
        private readonly int[] games = new int[2], points = new int[2];
        private int gameServer, tieFirstServer;
        public int EndA { get; private set; }
        public bool TieBreak { get; private set; }
        public bool Complete { get; private set; }
        public int Winner { get; private set; } = -1;
        public int PointsPlayed { get; private set; }
        public int PointsInGame => points[0] + points[1];
        public bool DeuceSide => PointsInGame % 2 == 0;
        public int Server => !TieBreak ? gameServer : (PointsInGame == 0 ? tieFirstServer : (tieFirstServer + (PointsInGame + 1) / 2) % 2);
        public Scoring(int firstServer = 0, int endA = -1)
        {
            if (firstServer < 0 || firstServer > 1 || Math.Abs(endA) != 1) throw new ArgumentException("Invalid server/end");
            gameServer = firstServer; EndA = endA;
        }
        public bool Award(int player)
        {
            if (Complete || player < 0 || player > 1) throw new InvalidOperationException("Cannot award point");
            int previousEnd = EndA;
            points[player]++; PointsPlayed++;
            if (TieBreak)
            {
                if (PointsInGame % 6 == 0) EndA = -EndA;
                if (points[player] >= 7 && points[player] - points[1 - player] >= 2)
                { games[player]++; Complete = true; Winner = player; }
            }
            else if (points[player] >= 4 && points[player] - points[1 - player] >= 2)
            {
                games[player]++; points[0] = points[1] = 0; gameServer = 1 - gameServer;
                if ((games[0] + games[1]) % 2 == 1) EndA = -EndA;
                if (games[player] >= 6 && games[player] - games[1 - player] >= 2) { Complete = true; Winner = player; }
                else if (games[0] == 6 && games[1] == 6) { TieBreak = true; tieFirstServer = gameServer; }
            }
            return previousEnd != EndA;
        }
        public ScoreState Snapshot() => new ScoreState { Games = (int[])games.Clone(), Points = (int[])points.Clone(), Server = Server,
            TieBreak = TieBreak, Complete = Complete, Winner = Winner, EndA = EndA, PointsPlayed = PointsPlayed, Display = Display() };
        private string Display()
        {
            string p;
            if (TieBreak) p = "TB " + points[0] + "-" + points[1];
            else if (points[0] >= 3 && points[1] >= 3) p = points[0] == points[1] ? "Deuce" : "Advantage " + (points[0] > points[1] ? "A" : "B");
            else { string[] labels = { "0", "15", "30", "40" }; p = labels[Math.Min(3, points[0])] + "-" + labels[Math.Min(3, points[1])]; }
            return games[0] + "-" + games[1] + " " + p + (Complete ? " Complete" : "");
        }
    }

    public enum ServeDecision { Fault, DoubleFault, Let, In }
    public sealed class ServeRules
    {
        public int Attempt { get; private set; } = 1;
        public ServeDecision Resolve(bool inBox, bool touchedNet)
        {
            if (inBox && touchedNet) return ServeDecision.Let;
            if (inBox) return ServeDecision.In;
            if (Attempt == 2) return ServeDecision.DoubleFault;
            Attempt = 2; return ServeDecision.Fault;
        }
    }
}
