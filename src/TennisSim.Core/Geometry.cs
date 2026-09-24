using System;

namespace TennisSim.Core
{
    public struct Vec3
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double GroundLength => Math.Sqrt(X * X + Z * Z);
        public bool IsFinite => Finite(X) && Finite(Y) && Finite(Z);
        public static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double b) => new Vec3(a.X * b, a.Y * b, a.Z * b);
        public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;
        public static double GroundDistance(Vec3 a, Vec3 b) => (a - b).GroundLength;
    }

    public static class Court
    {
        public const double HalfLength = 11.885, HalfWidth = 4.115, ServiceLine = 6.4;
        public const double BallRadius = 0.0335;
        public static double NetHeight(double x) => 0.914 + 0.156 * Math.Min(1, Math.Abs(x) / 5.029);
        // Circular footprint touching the rectangle's outside line edges is in.
        // Separate axis expansion falsely admits centres diagonally beyond a corner.
        private static bool FootprintIn(double x, double z, double minX, double maxX, double maxZ)
        {
            double dx = Math.Max(0, Math.Max(minX - x, x - maxX));
            double dz = Math.Max(0, Math.Max(-z, z - maxZ));
            // <4 ulps of court-scale subtraction (~16m); preserves exact edge tangency.
            double radius = BallRadius + 1e-14;
            return dx * dx + dz * dz <= radius * radius;
        }
        public static bool SinglesIn(Vec3 p, int receivingEnd) =>
            FootprintIn(p.X, p.Z * receivingEnd, -HalfWidth, HalfWidth, HalfLength);
        public static bool ServiceIn(Vec3 p, int serverEnd, bool deuce) =>
            FootprintIn(p.X * (deuce ? serverEnd : -serverEnd), p.Z * -serverEnd, 0, HalfWidth, ServiceLine);
        // A player facing the net has world-right = +X at the negative end, -X at the positive end.
        public static double RightX(int end) => -end;
        public static bool IsForehand(Vec3 body, Vec3 ball, int end, bool leftHanded) =>
            (ball.X - body.X) * RightX(end) * (leftHanded ? -1 : 1) >= 0;
        public static double BackhandX(Vec3 body, int end, bool leftHanded) =>
            body.X - RightX(end) * (leftHanded ? -1 : 1) * 2.5;
    }

    public sealed class SeedRandom
    {
        public uint State { get; private set; }
        // xorshift32 seeded directly returns about seed * 2^-19 first, so a small seed biased every first draw toward
        // the first candidate. The seed is spread over the full state range first (lowbias32 integer hash).
        public SeedRandom(uint seed) { uint mixed = Mix(seed); State = mixed == 0 ? 0x6D2B79F5u : mixed; }
        private SeedRandom() { }
        // Unmixed seeding as used before tennissim-mvp-4. Only the offline calibration tool uses it, so that its
        // committed synthetic data and bootstrap results stay reproducible; match simulation must not.
        public static SeedRandom Legacy(uint seed) => new SeedRandom { State = seed == 0 ? 0x6D2B79F5u : seed };
        public static uint Mix(uint x)
        {
            x ^= x >> 16; x = unchecked(x * 0x7feb352dU); x ^= x >> 15; x = unchecked(x * 0x846ca68bU); x ^= x >> 16;
            return x;
        }
        public uint NextUInt() { uint x = State; x ^= x << 13; x ^= x >> 17; x ^= x << 5; State = x; return x; }
        public double Next() => NextUInt() / 4294967296.0;
        public double Symmetric() => (Next() + Next() + Next() - 1.5) * 2;
    }
}
