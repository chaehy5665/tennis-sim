using System;
using System.Collections.Generic;
using TennisSim.Core;

namespace TennisSim.Coach
{
    // A projected point: screen pixels (origin top-left, y down) and the distance along the camera's view axis.
    public readonly struct ScreenPoint
    {
        public readonly double X, Y, Depth;
        public ScreenPoint(double x, double y, double depth) { X = x; Y = y; Depth = depth; }
        public bool InFront => Depth > 0;
    }

    // Where the renderer draws the ball: its centre at a fixed screen size, the shadow straight below it on the
    // ground, and whether the dashed height guide between them is shown.
    public readonly struct BallMarks
    {
        public readonly ScreenPoint Ball, Shadow;
        public readonly double Diameter;
        public readonly bool HeightGuide;
        public BallMarks(ScreenPoint ball, ScreenPoint shadow, double diameter, bool heightGuide) { Ball = ball; Shadow = shadow; Diameter = diameter; HeightGuide = heightGuide; }
    }

    // A camera-facing player capsule, its ground shadow and the screen-fixed badge above it, all in pixels.
    public readonly struct PlayerMarks
    {
        public readonly ScreenPoint Foot, Head;
        public readonly double HalfWidth, ShadowRadiusX, ShadowRadiusY, BadgeCenterX, BadgeCenterY;
        public PlayerMarks(ScreenPoint foot, ScreenPoint head, double halfWidth, double shadowRadiusX, double shadowRadiusY, double badgeCenterX, double badgeCenterY)
        { Foot = foot; Head = head; HalfWidth = halfWidth; ShadowRadiusX = shadowRadiusX; ShadowRadiusY = shadowRadiusY; BadgeCenterX = badgeCenterX; BadgeCenterY = badgeCenterY; }
        public ScreenRect Badge => new ScreenRect(BadgeCenterX - BroadcastSpec.BadgeSize / 2, BadgeCenterY - BroadcastSpec.BadgeSize / 2, BroadcastSpec.BadgeSize, BroadcastSpec.BadgeSize);
    }

    // Pinhole projection for the fixed broadcast camera. Pure maths on recorded positions: it never moves, snaps or
    // extrapolates anything, so the drawn scene can only differ from the record in size, never in place.
    public sealed class BroadcastCamera
    {
        readonly Vec3 position, right, up, forward;
        readonly double focal, centerX, centerY;

        public BroadcastCamera(Vec3 position, Vec3 target, double verticalFovDegrees, ScreenRect viewport)
        {
            this.position = position;
            forward = Normalize(target - position);
            right = Normalize(Cross(new Vec3(0, 1, 0), forward));
            up = Cross(forward, right);
            focal = viewport.Height / 2 / Math.Tan(verticalFovDegrees * Math.PI / 360);
            centerX = viewport.X + viewport.Width / 2;
            centerY = viewport.Y + viewport.Height / 2;
        }

        public static BroadcastCamera Default() =>
            new BroadcastCamera(BroadcastSpec.CameraPosition, BroadcastSpec.CameraTarget, BroadcastSpec.VerticalFovDegrees, BroadcastSpec.Viewport);

        public ScreenPoint Project(Vec3 world)
        {
            var d = world - position;
            double depth = Dot(d, forward);
            if (depth <= 0) return new ScreenPoint(double.NaN, double.NaN, depth);
            return new ScreenPoint(centerX + focal * Dot(d, right) / depth, centerY - focal * Dot(d, up) / depth, depth);
        }

        // Screen pixels covered by one metre across the view at this depth.
        public double PixelsPerMetre(double depth) => focal / depth;

        public BallMarks Ball(Vec3 ball) =>
            new BallMarks(Project(ball), Project(new Vec3(ball.X, 0, ball.Z)), BroadcastSpec.BallDiameter, ball.Y > BroadcastSpec.BallHeightGuideAbove);

        public PlayerMarks Player(Vec3 foot)
        {
            var f = Project(new Vec3(foot.X, 0, foot.Z));
            var h = Project(new Vec3(foot.X, BroadcastSpec.PlayerHeight, foot.Z));
            double half = PixelsPerMetre(f.Depth) * BroadcastSpec.PlayerWidth / 2;
            double shadowX = half * BroadcastSpec.PlayerShadowWidthScale;
            return new PlayerMarks(f, h, half, shadowX, shadowX * BroadcastSpec.PlayerShadowDepthScale,
                h.X, h.Y - BroadcastSpec.BadgeGap - BroadcastSpec.BadgeSize / 2);
        }

        static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        static Vec3 Normalize(Vec3 a) => a * (1 / a.Length);
    }

    // The painted court in world coordinates, built from the Core constants so the drawing follows any rule change.
    public static class BroadcastCourt
    {
        public readonly struct Segment
        {
            public readonly Vec3 A, B;
            public Segment(Vec3 a, Vec3 b) { A = a; B = b; }
        }

        public static Vec3[] SinglesCorners => new[]
        {
            new Vec3(-Court.HalfWidth, 0, -Court.HalfLength), new Vec3(Court.HalfWidth, 0, -Court.HalfLength),
            new Vec3(Court.HalfWidth, 0, Court.HalfLength), new Vec3(-Court.HalfWidth, 0, Court.HalfLength),
        };

        public static Vec3[] RunOffCorners => new[]
        {
            new Vec3(-BroadcastSpec.RunOffHalfWidth, 0, -BroadcastSpec.RunOffHalfLength), new Vec3(BroadcastSpec.RunOffHalfWidth, 0, -BroadcastSpec.RunOffHalfLength),
            new Vec3(BroadcastSpec.RunOffHalfWidth, 0, BroadcastSpec.RunOffHalfLength), new Vec3(-BroadcastSpec.RunOffHalfWidth, 0, BroadcastSpec.RunOffHalfLength),
        };

        // Baselines, sidelines, service lines, the centre service line and the two centre marks (10 cm).
        public static List<Segment> Lines()
        {
            double w = Court.HalfWidth, l = Court.HalfLength, s = Court.ServiceLine;
            return new List<Segment>
            {
                new Segment(new Vec3(-w, 0, -l), new Vec3(w, 0, -l)), new Segment(new Vec3(-w, 0, l), new Vec3(w, 0, l)),
                new Segment(new Vec3(-w, 0, -l), new Vec3(-w, 0, l)), new Segment(new Vec3(w, 0, -l), new Vec3(w, 0, l)),
                new Segment(new Vec3(-w, 0, -s), new Vec3(w, 0, -s)), new Segment(new Vec3(-w, 0, s), new Vec3(w, 0, s)),
                new Segment(new Vec3(0, 0, -s), new Vec3(0, 0, s)),
                new Segment(new Vec3(0, 0, -l), new Vec3(0, 0, -l + .1)), new Segment(new Vec3(0, 0, l), new Vec3(0, 0, l - .1)),
            };
        }

        // The net tape as a polyline over the Core height profile, post to post.
        public static Vec3[] NetTape(int strips = 20)
        {
            var points = new Vec3[strips + 1];
            for (int i = 0; i <= strips; i++)
            {
                double x = -BroadcastSpec.NetPostX + 2 * BroadcastSpec.NetPostX * i / strips;
                points[i] = new Vec3(x, Court.NetHeight(x), 0);
            }
            return points;
        }
    }
}
