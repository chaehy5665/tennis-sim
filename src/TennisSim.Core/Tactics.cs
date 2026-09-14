using System;
using System.Collections.Generic;

namespace TennisSim.Core
{
    public enum TargetStyle { Balanced, TargetBackhand }
    public enum Aggression { Safe, Balanced, Aggressive }
    public enum ServeDirection { Mixed, Wide, Body, T }
    public sealed class Tactic
    {
        public TargetStyle Target { get; set; }
        public Aggression Aggression { get; set; } = Aggression.Balanced;
        public ServeDirection Serve { get; set; }
        public Tactic Copy() => (Tactic)MemberwiseClone();
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(TargetStyle), Target) || !Enum.IsDefined(typeof(Aggression), Aggression) || !Enum.IsDefined(typeof(ServeDirection), Serve)) throw new ArgumentException("Unknown tactic enum");
        }
    }
    public sealed class Candidate
    {
        public string Name { get; set; } = "";
        public Vec3 Target { get; set; }
        public Vec3 LaunchVelocity { get; set; }
        public double FlightSeconds { get; set; }
        public double Weight { get; set; }
        public bool Feasible { get; set; }
        public string Rejection { get; set; } = "";
    }
    public sealed class ShotChoice
    {
        public Candidate Selected { get; set; } = new Candidate();
        public List<Candidate> Candidates { get; set; } = new List<Candidate>();
        public bool Forehand { get; set; }
        public double Control { get; set; }
        public double PreparationQuality { get; set; }
    }
    public static class ShotPolicy
    {
        public static ShotChoice Choose(PlayerProfile p, PlayerState self, PlayerProfile opponent, PlayerState other,
            Vec3 contact, Tactic tactic, bool serve, int attempt, bool deuce, double preparation, SimConfig c, SeedRandom rng)
        {
            bool forehand = Court.IsForehand(self.Position, contact, self.End, p.LeftHanded);
            double power = serve ? p.ServePower : forehand ? p.ForehandPower : p.BackhandPower;
            var result = new ShotChoice { Forehand = forehand, Control = serve ? p.ServeControl : forehand ? p.ForehandControl : p.BackhandControl, PreparationQuality = preparation };
            if (serve)
            {
                double sign = deuce ? self.End : -self.End;
                string[] names = { "Wide", "Body", "T" };
                double[] xs = { 3.35, 1.9, .48 };
                for (int i = 0; i < 3; i++)
                    result.Candidates.Add(new Candidate { Name = names[i], Target = new Vec3(sign * xs[i], Court.BallRadius, -self.End * (attempt == 1 ? 5.35 : 4.75)), Weight = tactic.Serve == ServeDirection.Mixed ? 1 : tactic.Serve.ToString() == names[i] ? 8 : .4 });
            }
            else
            {
                double backhand = Math.Max(-3.25, Math.Min(3.25, Court.BackhandX(other.Position, other.End, opponent.LeftHanded)));
                double open = other.Position.X >= 0 ? -3.25 : 3.25;
                result.Candidates.Add(new Candidate { Name = "SafeDeep", Target = new Vec3(contact.X * -.15, Court.BallRadius, -self.End * 8.6), Weight = tactic.Aggression == Aggression.Safe ? 6 : 2 });
                result.Candidates.Add(new Candidate { Name = "Backhand", Target = new Vec3(backhand, Court.BallRadius, -self.End * 9.1), Weight = tactic.Target == TargetStyle.TargetBackhand ? 10 : 2 });
                result.Candidates.Add(new Candidate { Name = "OpenCourt", Target = new Vec3(open, Court.BallRadius, -self.End * 8.7), Weight = 1.4 + Math.Min(3, Math.Abs(other.Position.X)) });
                result.Candidates.Add(new Candidate { Name = "Attack", Target = new Vec3(open * 1.13, Court.BallRadius, -self.End * 10.65), Weight = tactic.Aggression == Aggression.Aggressive ? 8 : tactic.Aggression == Aggression.Safe ? .2 : 2 });
            }
            double total = 0;
            foreach (var candidate in result.Candidates)
            {
                bool attack = candidate.Name == "Attack";
                if (preparation < .1) candidate.Rejection = "InsufficientPreparationTime";
                else if (!serve && Vec3.GroundDistance(self.Position, contact) > c.Reach + 1e-8) candidate.Rejection = "UnreachableContact";
                else if (attack && (preparation < .65 || contact.Y < .85 || self.Velocity.GroundLength > p.MaxSpeed * .85)) candidate.Rejection = "InsufficientPreparationTime";
                if (candidate.Rejection.Length == 0)
                {
                    double speed = serve ? 23 + 15 * power : (tactic.Aggression == Aggression.Aggressive ? 18 : tactic.Aggression == Aggression.Safe ? 13 : 15.5) + 9 * power;
                    double flight = serve ? .72 + .18 * (1 - power) + (attempt == 2 ? .15 : 0) : Math.Max(.8, Vec3.GroundDistance(contact, candidate.Target) / speed);
                    double margin = serve ? .04 : tactic.Aggression == Aggression.Safe ? .5 : .20;
                    for (; flight <= 2.4; flight += .045)
                    {
                        var velocity = BallPhysics.Launch(contact, candidate.Target, flight, c);
                        double f = -contact.Z / velocity.Z;
                        double crossTime = c.Drag < 1e-9 ? f : (f * c.Drag < 1 ? -Math.Log(1 - c.Drag * f) / c.Drag : double.NaN);
                        var crossing = BallPhysics.At(new BallState { Position = contact, Velocity = velocity }, crossTime, c);
                        if (crossTime > 0 && crossTime < flight && crossing.Position.Y > Court.NetHeight(crossing.Position.X) + Court.BallRadius + margin && velocity.Length <= (serve ? 24 + 18 * power : 19 + 14 * power))
                        { candidate.Feasible = true; candidate.FlightSeconds = flight; candidate.LaunchVelocity = velocity; break; }
                    }
                    if (!candidate.Feasible) candidate.Rejection = "UnsafeTrajectory";
                }
                // Control/preparation penalize risky options before the weighted draw.
                if (attack) candidate.Weight *= (.25 + .75 * result.Control) * preparation;
                if (candidate.Feasible) total += candidate.Weight;
            }
            if (total <= 0) throw new SimulationLimitExceeded("No feasible shot candidates at contact");
            double roll = rng.Next() * total;
            foreach (var candidate in result.Candidates)
                if (candidate.Feasible) { result.Selected = candidate; roll -= candidate.Weight; if (roll < 0) break; }
            return result;
        }
        public static Vec3 Execute(ShotChoice choice, PlayerState self, Tactic tactic, bool serve, int attempt, SeedRandom rng)
        {
            double error = .12 + (1 - choice.Control) * .95 + (1 - choice.PreparationQuality) * .4 + (1 - self.Energy) * .18;
            if (serve && attempt == 2) error *= .55;
            else if (tactic.Aggression == Aggression.Aggressive) error *= 1.25;
            else if (tactic.Aggression == Aggression.Safe) error *= .8;
            var v = choice.Selected.LaunchVelocity;
            return v + new Vec3(rng.Symmetric() * error, rng.Symmetric() * error * .8, rng.Symmetric() * error);
        }
    }
}
