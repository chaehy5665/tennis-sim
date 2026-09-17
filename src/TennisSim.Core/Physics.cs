using System;
using System.Collections.Generic;
using TennisSim.Core.Bounce;

namespace TennisSim.Core
{
    // Physical epsilons kept separate per unit. One dimensionless epsilon is never reused for
    // lengths, speeds, times and contact classification.
    public static class PhysicsEpsilons
    {
        public const double RemainingStepS = 1e-12;
        public const double NetPlaneM = 1e-10;
    }

    public sealed class SimConfig
    {
        public double TickSeconds { get; set; } = 1.0 / 120;
        public double Gravity { get; set; } = 9.81;
        public double Drag { get; set; } = 0.08;
        public double Restitution { get; set; } = 0.74;
        public double GroundFriction { get; set; } = 0.88;
        public double Reach { get; set; } = 0.95;
        public double MinContactHeight { get; set; } = 0.45;
        public double MaxContactHeight { get; set; } = 1.9;
        public int SnapshotEveryTicks { get; set; } = 12;
        public int MaxPointTicks { get; set; } = 24000;
        public int MaxPoints { get; set; } = 1000;
        public int MaxServeAttempts { get; set; } = 100;
        public int FirstServer { get; set; }
        public int InitialEndA { get; set; } = -1;
        public SimConfig Copy() => (SimConfig)MemberwiseClone();
        public void Validate()
        {
            double[] values = { TickSeconds, Gravity, Drag, Restitution, GroundFriction, Reach, MinContactHeight, MaxContactHeight };
            foreach (double v in values) if (!Vec3.Finite(v)) throw new ArgumentException("Non-finite configuration");
            if (TickSeconds <= 0 || TickSeconds > 1.0 / 60 || Gravity <= 0 || Drag < 0 || Drag > 2 || Restitution <= 0 || Restitution > 1 || GroundFriction <= 0 || GroundFriction > 1 || Reach <= 0 || MinContactHeight <= Court.BallRadius || MaxContactHeight <= MinContactHeight || SnapshotEveryTicks < 1 || MaxPointTicks < 1 || MaxPoints < 1 || MaxServeAttempts < 1 || FirstServer < 0 || FirstServer > 1 || Math.Abs(InitialEndA) != 1) throw new ArgumentException("Invalid simulation configuration");
        }
    }
    public sealed class BallState
    {
        public Vec3 Position { get; set; }
        public Vec3 Velocity { get; set; }
        // Spin is carried through flight in both models. Only the impulse model consumes it;
        // the legacy multiplicative bounce ignores it and never generates it.
        public Vec3 AngularVelocity { get; set; }
        public int Bounces { get; set; }
        public bool NetTouched { get; set; }
        public bool CrossedNet { get; set; }
        public BallState Copy() => (BallState)MemberwiseClone();
    }
    public sealed class Collision
    {
        public string Kind { get; set; } = "";
        public double Offset { get; set; }
        public BallState Before { get; set; } = new BallState();
        public BallState After { get; set; } = new BallState();
        // Present only for impulse-model ground contacts; null under the legacy model.
        public BounceResult? Bounce { get; set; }
    }
    public static class BallPhysics
    {
        public static BallState At(BallState b, double t, SimConfig c)
        {
            var n = b.Copy();
            double e = Math.Exp(-c.Drag * t), f = c.Drag < 1e-9 ? t : (1 - e) / c.Drag;
            double drop = c.Drag < 1e-9 ? c.Gravity * t * t / 2 : c.Gravity * (t - f) / c.Drag;
            n.Position = b.Position + b.Velocity * f + new Vec3(0, -drop, 0);
            n.Velocity = b.Velocity * e + new Vec3(0, -c.Gravity * f, 0);
            return n;
        }
        public static Vec3 Launch(Vec3 origin, Vec3 target, double flight, SimConfig c)
        {
            double f = c.Drag < 1e-9 ? flight : (1 - Math.Exp(-c.Drag * flight)) / c.Drag;
            double drop = c.Drag < 1e-9 ? c.Gravity * flight * flight / 2 : c.Gravity * (flight - f) / c.Drag;
            return (target - origin + new Vec3(0, drop, 0)) * (1 / f);
        }
        private static double Root(BallState b, double dt, SimConfig c, bool ground)
        {
            double lo = 0, hi = dt;
            for (int i = 0; i < 40; i++)
            {
                double mid = (lo + hi) / 2; var p = At(b, mid, c).Position;
                bool before = ground ? p.Y > Court.BallRadius : p.Z * b.Position.Z > 0;
                if (before) lo = mid; else hi = mid;
            }
            return hi;
        }
        // Callback can stop at the exact event, before any remaining part of the tick is advanced.
        // surface == null keeps the legacy multiplicative bounce. With a surface environment the
        // ground contact resolves through the explicit impulse model exactly once per contact.
        public static BallState Advance(BallState start, double dt, SimConfig c, Func<Collision, bool>? onCollision = null, SurfaceEnvironment? surface = null)
        {
            var b = start.Copy(); double elapsed = 0;
            for (int count = 0; elapsed < dt - PhysicsEpsilons.RemainingStepS; count++)
            {
                if (count > 12) throw new SimulationLimitExceeded("Too many collisions in a physics step");
                double remaining = dt - elapsed;
                var end = At(b, remaining, c);
                bool impulseModel = surface != null && surface.Model == BounceModelKind.ImpulseV1;
                // The impulse path also detects a surface return that starts exactly on the surface and
                // ends below it inside one step; the legacy path keeps its previous detection rule.
                double ground = end.Position.Y <= Court.BallRadius && (b.Position.Y > Court.BallRadius || b.Velocity.Y < 0 || (impulseModel && end.Position.Y < Court.BallRadius)) ? Root(b, remaining, c, true) : double.PositiveInfinity;
                double net = !b.CrossedNet && b.Position.Z * end.Position.Z <= 0 && Math.Abs(b.Position.Z) > PhysicsEpsilons.NetPlaneM ? Root(b, remaining, c, false) : double.PositiveInfinity;
                double t = Math.Min(ground, net);
                if (double.IsInfinity(t)) return end;
                b = At(b, t, c); elapsed += t;
                var before = b.Copy(); string kind;
                BounceResult? bounce = null;
                bool settled = false;
                if (net < ground)
                {
                    b.CrossedNet = true;
                    if (Math.Abs(b.Position.X) > 5.029 + Court.BallRadius || b.Position.Y > Court.NetHeight(b.Position.X) + Court.BallRadius) continue;
                    kind = "NetTouched"; b.NetTouched = true;
                    bool tape = b.Position.Y >= Court.NetHeight(b.Position.X) - 0.10;
                    b.Velocity = tape ? new Vec3(b.Velocity.X * .7, Math.Max(.3, b.Velocity.Y * .4), b.Velocity.Z * .7) : new Vec3(b.Velocity.X * .2, b.Velocity.Y * .2, -b.Velocity.Z * .12);
                }
                else
                {
                    kind = "BallBounced";
                    if (impulseModel)
                    {
                        // t* found by bisection, pre-state evaluated at t*, one surface sample,
                        // exactly one ResolveBounce call, then the remaining flight integrates.
                        var pre = new ImpactState
                        {
                            PositionM = b.Position,
                            VelocityMS = b.Velocity,
                            AngularVelocityRadS = b.AngularVelocity,
                            ImpactTimeS = elapsed
                        };
                        // Contact point on the surface plane, not the ball centre height.
                        var sample = surface!.Sample(new Vec3(b.Position.X, 0, b.Position.Z));
                        bounce = BounceModel.Resolve(pre, surface.Ball, surface.Condition, sample, surface.Profile, surface.Tolerances);
                        if (bounce.Status == BounceStatus.RESOLVED || bounce.Status == BounceStatus.SETTLED)
                        {
                            b.Position = bounce.PostState.PositionM;
                            b.Velocity = bounce.PostState.VelocityMS;
                            b.AngularVelocity = bounce.PostState.AngularVelocityRadS;
                            b.Bounces++;
                            settled = bounce.Status == BounceStatus.SETTLED;
                        }
                        else
                        {
                            // Settling and contact policy. A contact that resolves to no impulse is not a
                            // bounce event: hold the ball on the surface, remove the unresolved normal
                            // approach, and end the step so the remainder cannot re-enter forever.
                            b.Position = sample.ContactPositionM + sample.Normal * surface.Ball.RadiusM;
                            double normalSpeed = BounceModel.Dot(b.Velocity, sample.Normal);
                            if (normalSpeed < 0) b.Velocity = b.Velocity - sample.Normal * normalSpeed;
                            return b;
                        }
                    }
                    else
                    {
                        b.Position = new Vec3(b.Position.X, Court.BallRadius, b.Position.Z);
                        b.Velocity = new Vec3(b.Velocity.X * c.GroundFriction, Math.Abs(b.Velocity.Y) * c.Restitution, b.Velocity.Z * c.GroundFriction);
                        b.Bounces++;
                    }
                }
                var collision = new Collision { Kind = kind, Offset = elapsed, Before = before, After = b.Copy(), Bounce = bounce };
                if (onCollision != null && !onCollision(collision)) return b;
                // A settled contact ends the step: the ball rests on the surface and no further
                // contact inside this step is physically meaningful.
                if (settled) return b;
            }
            return b;
        }
    }
    public sealed class SimulationLimitExceeded : Exception
    { public SimulationLimitExceeded(string message) : base(message) { } }
}
