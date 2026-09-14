using System;

namespace TennisSim.Viewer
{
    public struct Position
    {
        public double X, Y, Z;
        public Position(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Position Lerp(Position a, Position b, double t) => new Position(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }
    public sealed class ReplayScore
    {
        public string Display;
        public int PointsPlayed, Winner;
        public int[] Games;
        public bool Complete;
    }
    public sealed class ReplayState
    {
        public double Time;
        public int Point;
        public string Phase;
        public Position Ball, A, B;
        public ReplayScore Score;
    }
    public sealed class ReplayEvent
    {
        public int Sequence, Point;
        public double Time;
        public string Kind, PlayerId, Reason;
        public ReplayState Before, State;
        public bool IsMarker => Kind == "BallHit" || Kind == "BallBounced" || Kind == "NetTouched";
        public override string ToString() => "#" + Sequence + " P" + Point + " " + Kind + " @ " + Time.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "s " + PlayerId + " " + Reason;
    }
    public sealed class ReplayData
    {
        public string FileName, Status, Diagnostic, EngineVersion;
        public uint Seed;
        public double TickSeconds;
        public string[] PlayerIds;
        public ReplayState[] Frames;
        public ReplayEvent[] Events;
        public ReplayScore FinalScore;
    }
    // Values bound to the supported tennissim-mvp-1 contract (Core/Geometry.cs).
    // Engine axes already are X lateral, Y up, Z longitudinal, in metres.
    public static class CoordinateMapper
    {
        public const double HalfWidth = 4.115, HalfLength = 11.885, ServiceLine = 6.4, BallRadius = .0335;
        public static Position Map(Position engine) => new Position(engine.X, engine.Y, engine.Z);
    }
}
