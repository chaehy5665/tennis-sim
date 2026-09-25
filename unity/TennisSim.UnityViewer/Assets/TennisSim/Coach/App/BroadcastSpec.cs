using TennisSim.Core;

namespace TennisSim.Coach
{
    // Sizes and timings of the 2.5D broadcast view (design system broadcast.md, docs/BROADCAST_ASSETS.md). Court
    // dimensions are not repeated here: they come from TennisSim.Core.Court. Everything below is display only and
    // never feeds back into the simulation.
    public static class BroadcastSpec
    {
        // Screen: the coach UI reference resolution, the header band above the 3D viewport, and the title-safe inset.
        public const double ScreenWidth = 1280, ScreenHeight = 800, HeaderHeight = 56, SafeInset = 32;

        // Broadcast camera, court centre at the origin (x across, y up, z along the court, metres). Framed so that a
        // player anywhere the engine has put one (|z| up to 18.9 m behind the net on engine v6, seed 42) stays in
        // view: the near foot at 19 m stays above the control bar and the far badge below the safe-area top.
        public static readonly Vec3 CameraPosition = new Vec3(0, 12, -42);
        public static readonly Vec3 CameraTarget = new Vec3(0, 0, -5.5);
        public const double VerticalFovDegrees = 26;
        public const double FramedDepth = 19;

        // The painted run-off: as wide as the viewer's (18 m) and long enough (40 m) that deep players still stand on
        // court colour. The net posts: NetHeight's profile reaches its full
        // height at |x| = 5.029 m, which is where the posts stand.
        public const double RunOffHalfWidth = 9, RunOffHalfLength = 20, NetPostX = 5.029;

        // Players: a camera-facing capsule on a ground shadow disc, and a screen-fixed A/B badge above the head.
        public const double PlayerHeight = 1.8, PlayerWidth = .6;
        public const double PlayerShadowWidthScale = 1.6, PlayerShadowDepthScale = .35;
        public const double BadgeSize = 22, BadgeGap = 8;

        // Ball: drawn at a fixed screen diameter (a real 6.7 cm ball is only a few pixels), always on its recorded
        // centre, with a ground shadow and a height guide once it is clearly off the ground.
        public const double BallDiameter = 11, BallOutline = 1.5;
        public const double BallShadowWidth = 12, BallShadowHeight = 5;
        public const double BallHeightGuideAbove = .3;

        // Court line width in pixels at a given camera depth: about 2 px at the near baseline, 1.2 px at the far one, never under 1 px.
        public const double LineWidthAt30m = 2.2, MinLineWidth = 1;
        public static double LineWidth(double depth) => System.Math.Max(MinLineWidth, LineWidthAt30m * 30 / depth);

        // Motion timing chosen by the renderer. The contact frame itself is always the BallHit time.
        public const double PrepareLead = .35, FollowThrough = .3, MissGrace = .1, LateSideSwitch = .15;

        // HUD plates over the scene (x, y, width, height in px). All sit inside the title-safe area.
        public static readonly ScreenRect Scoreboard = new ScreenRect(32, 88, 300, 84);
        public static readonly ScreenRect CurrentTactic = new ScreenRect(928, 88, 320, 60);
        public static readonly ScreenRect LastPoint = new ScreenRect(32, 652, 360, 44);
        public static readonly ScreenRect ControlBar = new ScreenRect(32, 712, 1216, 56);
        public static ScreenRect[] HudPlates => new[] { Scoreboard, CurrentTactic, LastPoint, ControlBar };

        public static ScreenRect Viewport => new ScreenRect(0, HeaderHeight, ScreenWidth, ScreenHeight - HeaderHeight);
        public static ScreenRect SafeArea => new ScreenRect(SafeInset, HeaderHeight + SafeInset, ScreenWidth - 2 * SafeInset, ScreenHeight - HeaderHeight - 2 * SafeInset);
    }

    public readonly struct ScreenRect
    {
        public readonly double X, Y, Width, Height;
        public ScreenRect(double x, double y, double width, double height) { X = x; Y = y; Width = width; Height = height; }
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
        public bool Overlaps(ScreenRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
    }
}
