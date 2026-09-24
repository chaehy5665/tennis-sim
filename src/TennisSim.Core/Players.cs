using System;
using TennisSim.Core.Bounce;

namespace TennisSim.Core
{
    public sealed class PlayerProfile
    {
        public string Id { get; set; } = "A";
        public string Name { get; set; } = "Baseline";
        public double ServePower { get; set; } = .75;
        public double ServeControl { get; set; } = .78;
        public double ForehandPower { get; set; } = .8;
        public double ForehandControl { get; set; } = .8;
        public double BackhandPower { get; set; } = .65;
        public double BackhandControl { get; set; } = .72;
        public double MaxSpeed { get; set; } = 6.2;
        public double Acceleration { get; set; } = 10;
        public double ReactionSeconds { get; set; } = .20;
        public double PreparationSeconds { get; set; } = .18;
        public double Stamina { get; set; } = .8;
        public bool LeftHanded { get; set; }
        public PlayerProfile Copy() => (PlayerProfile)MemberwiseClone();
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Player ID is required");
            foreach (double v in new[] { ServePower, ServeControl, ForehandPower, ForehandControl, BackhandPower, BackhandControl, Stamina })
                if (!Vec3.Finite(v) || v < 0 || v > 1) throw new ArgumentException("Skills must be in [0,1]");
            if (!Vec3.Finite(MaxSpeed) || MaxSpeed <= 0 || MaxSpeed > 15 || !Vec3.Finite(Acceleration) || Acceleration <= 0 || Acceleration > 30 || !Vec3.Finite(ReactionSeconds) || ReactionSeconds < 0 || ReactionSeconds > 2 || !Vec3.Finite(PreparationSeconds) || PreparationSeconds < 0 || PreparationSeconds > 2) throw new ArgumentException("Invalid movement attributes");
        }
        public static PlayerProfile Preset(string name, string id)
        {
            switch (name.ToLowerInvariant())
            {
                case "server": return new PlayerProfile { Id = id, Name = "Granite", ServePower = .96, ServeControl = .77, BackhandPower = .48, BackhandControl = .60, MaxSpeed = 5.3, Acceleration = 8, Stamina = .65 };
                case "baseline": return new PlayerProfile { Id = id, Name = "Ember" };
                case "defender": return new PlayerProfile { Id = id, Name = "Willow", ServePower = .60, ServeControl = .87, ForehandPower = .64, ForehandControl = .9, BackhandPower = .63, BackhandControl = .88, MaxSpeed = 7.1, Acceleration = 12, ReactionSeconds = .16, Stamina = .96 };
                default: throw new ArgumentException("Unknown player preset: " + name);
            }
        }
    }
    public sealed class PlayerState
    {
        public string Id { get; set; } = "";
        public int End { get; set; }
        public Vec3 Position { get; set; }
        public Vec3 Velocity { get; set; }
        public Vec3 Facing { get; set; }
        public double Energy { get; set; } = 1;
        public PlayerState Copy() => (PlayerState)MemberwiseClone();
    }
    public static class Movement
    {
        public static double SpeedLimit(PlayerProfile p, PlayerState s) => p.MaxSpeed * (.8 + .2 * s.Energy);
        public static void Step(PlayerProfile p, PlayerState s, Vec3 target, double dt)
        {
            Vec3 delta = target - s.Position; delta.Y = 0;
            double distance = delta.GroundLength;
            double speed = Math.Min(SpeedLimit(p, s), Math.Sqrt(2 * p.Acceleration * distance));
            Vec3 desired = distance < 1e-8 ? new Vec3() : delta * (speed / distance);
            Vec3 change = desired - s.Velocity;
            if (change.Length > p.Acceleration * dt) change = change * (p.Acceleration * dt / change.Length);
            Vec3 old = s.Velocity;
            s.Velocity += change;
            // Energy drains slowly; clamp the tiny limit reduction with the same acceleration budget next tick.
            Vec3 displacement = (old + s.Velocity) * (.5 * dt);
            s.Position += displacement;
            s.Energy = Math.Max(.15, s.Energy - displacement.GroundLength * .0008 / (.4 + p.Stamina));
            s.Facing = new Vec3(0, 0, -s.End);
        }
        // A player with time lets the ball rise to a comfortable height, or takes a low bounce at its apex. A rushed
        // player takes the first legal height instead, which rules out an attacking shot (see ShotPolicy).
        public const double ComfortableContactHeight = .9;
        public static bool Comfortable(BallState ball) => ball.Position.Y >= ComfortableContactHeight || ball.Velocity.Y <= 0;
        // reactionSeconds overrides the profile value when the receiver has read the opponent's pattern.
        public static bool CanContact(PlayerProfile p, PlayerState s, BallState ball, double sinceOpponentHit, SimConfig c, double? reactionSeconds = null, bool comfortableOnly = false) =>
            (!comfortableOnly || Comfortable(ball)) &&
            sinceOpponentHit + 1e-9 >= (reactionSeconds ?? p.ReactionSeconds) + p.PreparationSeconds && ball.Bounces == 1 &&
            ball.Position.Y >= c.MinContactHeight && ball.Position.Y <= c.MaxContactHeight &&
            ball.Position.Z * s.End > 0 && ball.Velocity.Z * s.End > 0 && Vec3.GroundDistance(s.Position, ball.Position) <= c.Reach;
        public static Vec3 PredictContact(PlayerProfile p, PlayerState s, BallState observed, SimConfig c, double timeSinceHit, out double arrival, out bool reachable, SurfaceEnvironment? surface = null, double? reactionSeconds = null, bool comfortableOnly = false)
        {
            var b = observed.Copy(); Vec3 fallback = s.Position; arrival = 0; reachable = false;
            double reaction = reactionSeconds ?? p.ReactionSeconds;
            const double step = 1.0 / 60;
            for (double t = step; t < 4; t += step)
            {
                b = BallPhysics.Advance(b, step, c, null, surface);
                if (b.Bounces >= 2) break;
                if (b.Bounces != 1 || b.Position.Z * s.End <= 0 || b.Position.Y < c.MinContactHeight || b.Position.Y > c.MaxContactHeight) continue;
                if (comfortableOnly && !Comfortable(b)) continue;
                fallback = new Vec3(b.Position.X, 0, b.Position.Z); arrival = t;
                double travelTime = Math.Max(0, t - Math.Max(0, reaction - timeSinceHit));
                double speed = SpeedLimit(p, s), ramp = Math.Max(0, speed - s.Velocity.GroundLength) / p.Acceleration;
                double distance = travelTime < ramp ? s.Velocity.GroundLength * travelTime + .5 * p.Acceleration * travelTime * travelTime : s.Velocity.GroundLength * ramp + .5 * p.Acceleration * ramp * ramp + speed * (travelTime - ramp);
                if (Vec3.GroundDistance(s.Position, fallback) <= distance + c.Reach * .65 && timeSinceHit + t >= reaction + p.PreparationSeconds) { reachable = true; return fallback; }
            }
            return fallback;
        }
    }
}
