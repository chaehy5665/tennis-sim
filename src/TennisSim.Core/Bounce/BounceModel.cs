using System;
using System.Collections.Generic;

namespace TennisSim.Core.Bounce
{
    // Geometry of one contact. Engine coordinates: X width, Y up, Z court length.
    // The instruction document is written with z up; the transforms below are the same
    // equations expressed in the preserved engine coordinate system.
    public struct ImpactGeometry
    {
        public double Sn;
        public double St;
        public double Su;
        public Vec3 Normal;
        public Vec3 Offset;
        public Vec3 Slip;
    }

    // V1: stationary local plane effective-impulse model. Pure function; no time, I/O, global
    // state or RNG use. Finite contact patch, deformation history and surface motion are not modelled.
    public static class BounceModel
    {
        public const string ModelIdV1 = "V1-plane-impulse";
        public const double NominalVrefMS = 10.0;

        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        // Component of v in the plane perpendicular to the unit normal n.
        public static Vec3 Perp(Vec3 v, Vec3 n) => v - n * Dot(v, n);

        public static double Sigmoid(double a) => 1.0 / (1.0 + Math.Exp(-a));

        public static ImpactGeometry Geometry(ImpactState pre, Vec3 normal, double radiusM)
        {
            Vec3 offset = normal * (-radiusM);
            Vec3 vt = Perp(pre.VelocityMS, normal);
            Vec3 slip = Perp(pre.VelocityMS + Cross(pre.AngularVelocityRadS, offset), normal);
            return new ImpactGeometry { Normal = normal, Offset = offset, Slip = slip, Sn = -Dot(pre.VelocityMS, normal), St = vt.Length, Su = slip.Length };
        }

        public static double KineticEnergy(ImpactState state, BallSpec ball) =>
            0.5 * ball.MassKg * Dot(state.VelocityMS, state.VelocityMS) + 0.5 * ball.InertiaKgM2 * Dot(state.AngularVelocityRadS, state.AngularVelocityRadS);

        // Design normalisation features. These are implementation proposals, not measured natural laws.
        public static void FeatureValues(double sn, double st, double su, double vref, out double f1, out double f2, out double f3)
        {
            f1 = Math.Log(1 + sn / vref);
            f2 = st * st / (sn * sn + st * st + vref * vref);
            f3 = Math.Log(1 + su / vref);
        }

        // Effective coefficient evaluation. Inputs are never modified; only the evaluation point is
        // bounded by the pre-declared domain, and any excursion is reported to the caller.
        public static void EvaluateResponses(InteractionProfile profile, double sn, double st, double su, out double en, out double mu, out double beta, out bool outOfDomain)
        {
            outOfDomain = false;
            if (profile.Table != null)
            {
                profile.Table.Evaluate(sn, st, su, out en, out mu, out beta, out bool clamped);
                outOfDomain = clamped;
                return;
            }
            double esn = Clamp(profile.Domain?.SnMin, profile.Domain?.SnMax, sn, ref outOfDomain);
            double est = Clamp(profile.Domain?.StMin, profile.Domain?.StMax, st, ref outOfDomain);
            double esu = Clamp(profile.Domain?.SuMin, profile.Domain?.SuMax, su, ref outOfDomain);
            FeatureValues(esn, est, esu, profile.VrefMS, out double f1, out double f2, out double f3);
            en = EvaluateNormalResponse(profile.NormalResponse, f1, f2);
            mu = EvaluateFrictionResponse(profile.FrictionResponse, f3);
            beta = EvaluateTangentialResponse(profile.TangentialResponse);
        }

        private static double Clamp(double? min, double? max, double value, ref bool clipped)
        {
            if (min.HasValue && value < min.Value) { clipped = true; return min.Value; }
            if (max.HasValue && value > max.Value) { clipped = true; return max.Value; }
            return value;
        }

        public static double EvaluateNormalResponse(NormalResponseParameters p, double f1, double f2)
        {
            double value = p.Kind == NormalResponseKind.Constant ? p.En0 : Sigmoid(p.A0 + p.A1 * f1 + p.A2 * f2);
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        public static double EvaluateFrictionResponse(FrictionResponseParameters p, double f3)
        {
            double value = p.Kind == FrictionResponseKind.Constant ? p.Mu0 : p.MuMax * Sigmoid(p.B0 + p.B1 * f3);
            return Math.Max(0.0, value);
        }

        public static double EvaluateTangentialResponse(TangentialResponseParameters p)
        {
            double value = p.Kind == TangentialResponseKind.Zero ? 0.0
                : p.Kind == TangentialResponseKind.ConstantBeta ? p.Beta : Sigmoid(p.C0);
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        // Tangential kinetic energy change of the impulse model. Non-positive for beta in [0,1]
        // and q = min((1+beta)*mt*|ut|, mu*Jn).
        public static double TangentialEnergyChange(Vec3 slipBefore, Vec3 tangentImpulse, double tangentialMass) =>
            Dot(tangentImpulse, slipBefore) + Dot(tangentImpulse, tangentImpulse) / (2 * tangentialMass);

        // Flat-surface sample placed at the current contact geometry. No spatial field, no per-bounce noise.
        public static SurfaceSample SampleAtContact(ImpactState pre, BallSpec ball, string materialId, string locationId)
        {
            Vec3 normal = new Vec3(0, 1, 0);
            return new SurfaceSample
            {
                ContactPositionM = pre.PositionM - normal * ball.RadiusM,
                Normal = normal,
                MaterialId = materialId,
                LocationId = locationId,
                ConditionMetadata = ""
            };
        }

        public static BounceResult Resolve(ImpactState pre, BallSpec ball, BallCondition? condition, SurfaceSample sample, InteractionProfile profile, BounceTolerances tolerances)
        {
            if (pre == null) throw new ArgumentNullException(nameof(pre));
            if (ball == null) throw new ArgumentNullException(nameof(ball));
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (tolerances == null) throw new ArgumentNullException(nameof(tolerances));
            ball.Validate(); sample.Validate(); profile.Validate(); tolerances.Validate();
            condition?.Validate();
            if (!pre.PositionM.IsFinite || !pre.VelocityMS.IsFinite || !pre.AngularVelocityRadS.IsFinite) throw new ArgumentException("Impact state must be finite");
            if (!Vec3.Finite(pre.ImpactTimeS)) throw new ArgumentException("Impact time must be finite");

            Vec3 n = sample.Normal;
            double mass = ball.MassKg, radius = ball.RadiusM, inertia = ball.InertiaKgM2;
            var geometry = Geometry(pre, n, radius);

            var result = new BounceResult
            {
                PostState = pre.Copy(),
                PreContactSlipMS = geometry.Slip,
                PostContactSlipMS = geometry.Slip,
                ProfileId = profile.Id,
                ProfileRevision = profile.Revision,
                ProfileHash = ProfileHash.Compute(profile),
                ModelId = profile.ModelId,
                EnergyBeforeJ = KineticEnergy(pre, ball),
                EnergyAfterJ = KineticEnergy(pre, ball)
            };

            double clearance = Dot(pre.PositionM - sample.ContactPositionM, n) - radius;
            if (clearance < -tolerances.DeepPenetrationEpsilonM)
            {
                // Deep initial penetration is not a new collision. No impulse, no position change.
                result.Status = BounceStatus.DEEP_INITIAL_PENETRATION;
                result.Warnings.Add("DEEP_INITIAL_PENETRATION=" + clearance.ToString("R"));
                result.ActiveImpulseLimit = ActiveImpulseLimit.NONE;
                return result;
            }
            if (geometry.Sn <= tolerances.NormalApproachEpsilonMS)
            {
                // Already separating, or a tangential contact inside the normal-approach tolerance.
                result.Status = geometry.Sn < -tolerances.NormalApproachEpsilonMS ? BounceStatus.SEPARATING_NO_IMPULSE : BounceStatus.TANGENTIAL_CONTACT_NO_IMPULSE;
                result.ActiveImpulseLimit = ActiveImpulseLimit.NONE;
                result.PostState = pre.Copy();
                return result;
            }

            EvaluateResponses(profile, geometry.Sn, geometry.St, geometry.Su, out double en, out double mu, out double beta, out bool outOfDomain);
            result.OutOfDomain = outOfDomain;
            if (outOfDomain) result.Warnings.Add("OUT_OF_DOMAIN");
            if (en < 0 || en > 1) result.Warnings.Add("EN_OUTSIDE_V1_RANGE");
            if (mu < 0) result.Warnings.Add("MU_NEGATIVE");
            if (beta < 0 || beta > 1) result.Warnings.Add("BETA_OUTSIDE_V1_RANGE");

            double vn = -geometry.Sn;
            double normalImpulse = -(1 + en) * mass * vn;
            double tangentialMass = 1.0 / (1.0 / mass + radius * radius / inertia);
            double slip = geometry.Su;
            double goal = (1 + beta) * tangentialMass * slip;
            double limitImpulse = mu * normalImpulse;

            double tangentialImpulseMagnitude;
            ActiveImpulseLimit activeLimit;
            if (slip <= tolerances.SlipEpsilonMS) { tangentialImpulseMagnitude = 0; activeLimit = ActiveImpulseLimit.NEAR_ZERO_SLIP; }
            else if (Math.Abs(goal - limitImpulse) <= tolerances.TieRelativeTolerance * Math.Max(Math.Abs(goal), Math.Abs(limitImpulse)))
            { tangentialImpulseMagnitude = Math.Min(goal, limitImpulse); activeLimit = ActiveImpulseLimit.TIE_WITHIN_TOLERANCE; }
            else if (limitImpulse < goal) { tangentialImpulseMagnitude = limitImpulse; activeLimit = ActiveImpulseLimit.COULOMB_LIMITED; }
            else { tangentialImpulseMagnitude = goal; activeLimit = ActiveImpulseLimit.TANGENTIAL_TARGET_LIMITED; }

            Vec3 tangentImpulse = slip <= tolerances.SlipEpsilonMS ? new Vec3() : geometry.Slip * (-tangentialImpulseMagnitude / slip);
            Vec3 afterVelocity = pre.VelocityMS + (n * normalImpulse + tangentImpulse) * (1.0 / mass);
            Vec3 afterAngular = pre.AngularVelocityRadS + Cross(geometry.Offset, tangentImpulse) * (1.0 / inertia);

            result.NormalImpulseNs = normalImpulse;
            result.TangentImpulseNs = tangentImpulse;
            result.NormalCorUsed = en;
            result.MuEffectiveUsed = mu;
            result.BetaGripUsed = beta;
            result.ActiveImpulseLimit = activeLimit;

            if (!afterVelocity.IsFinite || !afterAngular.IsFinite)
            {
                result.Status = BounceStatus.NUMERICAL_FAILURE;
                result.Warnings.Add("NON_FINITE_OUTPUT");
                return result;
            }

            result.PostState = new ImpactState
            {
                PositionM = sample.ContactPositionM + n * radius,
                VelocityMS = afterVelocity,
                AngularVelocityRadS = afterAngular,
                ImpactTimeS = pre.ImpactTimeS
            };
            result.PostContactSlipMS = Perp(afterVelocity + Cross(afterAngular, geometry.Offset), n);
            double slideSquared = Dot(geometry.Slip, geometry.Slip);
            result.BetaEffective = slideSquared > tolerances.SlipDenominatorEpsilonM2S2 ? -Dot(result.PostContactSlipMS, geometry.Slip) / slideSquared : (double?)null;
            result.EnergyAfterJ = KineticEnergy(result.PostState, ball);

            // Resting contact policy. A rigid point-mass bounce model otherwise produces an endless
            // series of ever smaller bounces (Zeno behaviour); the contact is instead held at rest once
            // the resolved normal speed cannot lift the centre above the modelled length scale.
            if (Dot(afterVelocity, n) < tolerances.SettleNormalSpeedMS)
            {
                result.Status = BounceStatus.SETTLED;
                result.Warnings.Add("SETTLED_CONTACT");
                result.NormalImpulseNs = 0;
                result.TangentImpulseNs = new Vec3();
                result.ActiveImpulseLimit = ActiveImpulseLimit.NONE;
                Vec3 restingVelocity = afterVelocity - n * Dot(afterVelocity, n);
                result.PostState = new ImpactState
                {
                    PositionM = sample.ContactPositionM + n * radius,
                    VelocityMS = restingVelocity,
                    AngularVelocityRadS = pre.AngularVelocityRadS,
                    ImpactTimeS = pre.ImpactTimeS
                };
                result.PostContactSlipMS = Perp(restingVelocity + Cross(pre.AngularVelocityRadS, geometry.Offset), n);
                result.BetaEffective = null;
                result.EnergyAfterJ = KineticEnergy(result.PostState, ball);
                foreach (string violation in CheckInvariants(pre, result, ball, sample, tolerances))
                {
                    result.Warnings.Add("INVARIANT_" + violation);
                    if (violation == "ENERGY_INCREASE") result.Status = BounceStatus.NUMERICAL_FAILURE;
                }
                return result;
            }
            result.Status = BounceStatus.RESOLVED;

            // Required invariants of V1. Violations are reported, not silently corrected.
            foreach (string violation in CheckInvariants(pre, result, ball, sample, tolerances))
            {
                result.Warnings.Add("INVARIANT_" + violation);
                if (violation == "ENERGY_INCREASE") result.Status = BounceStatus.NUMERICAL_FAILURE;
            }
            return result;
        }

        public static List<string> CheckInvariants(ImpactState pre, BounceResult result, BallSpec ball, SurfaceSample sample, BounceTolerances tolerances)
        {
            var violations = new List<string>();
            if (result.Status != BounceStatus.RESOLVED && result.Status != BounceStatus.SETTLED) return violations;
            var post = result.PostState;
            double mass = ball.MassKg, inertia = ball.InertiaKgM2;
            Vec3 offset = sample.Normal * (-ball.RadiusM);
            double velocityTolerance = tolerances.ImpulseToleranceNs / mass;

            double energyLimit = result.EnergyBeforeJ + tolerances.EnergyAtolJ + tolerances.EnergyRtol * Math.Max(Math.Abs(result.EnergyBeforeJ), Math.Abs(result.EnergyAfterJ));
            if (result.EnergyAfterJ > energyLimit) violations.Add("ENERGY_INCREASE");
            if (result.Status == BounceStatus.SETTLED)
            {
                // A resting contact applies no impulse: speed is removed, never added, and the
                // tangential velocity and spin carry over unchanged.
                if (Math.Abs(Dot(post.VelocityMS, sample.Normal)) > tolerances.SettleNormalSpeedMS) violations.Add("SETTLED_NORMAL_SPEED");
                if (Perp(post.VelocityMS, sample.Normal).Length > Perp(pre.VelocityMS, sample.Normal).Length + tolerances.ImpulseToleranceNs) violations.Add("SETTLED_TANGENTIAL_GAIN");
                if (post.AngularVelocityRadS.Length > pre.AngularVelocityRadS.Length + tolerances.AngularImpulseTolerance) violations.Add("SETTLED_SPIN_GAIN");
                double settledOffset = Dot(post.PositionM - sample.ContactPositionM, sample.Normal) - ball.RadiusM;
                if (Math.Abs(settledOffset) > tolerances.DeepPenetrationEpsilonM) violations.Add("POSITION_NOT_ON_SURFACE");
                return violations;
            }
            if (result.NormalImpulseNs < -tolerances.ImpulseToleranceNs) violations.Add("NEGATIVE_NORMAL_IMPULSE");
            if (result.TangentImpulseNs.Length > result.MuEffectiveUsed * result.NormalImpulseNs + tolerances.ImpulseToleranceNs) violations.Add("FRICTION_LIMIT_EXCEEDED");
            if (Math.Abs(Dot(result.TangentImpulseNs, sample.Normal)) > tolerances.TangentToleranceNs) violations.Add("TANGENT_IMPULSE_NOT_TANGENT");
            Vec3 expectedAngular = Cross(offset, result.TangentImpulseNs) * (1.0 / inertia);
            if ((post.AngularVelocityRadS - pre.AngularVelocityRadS - expectedAngular).Length > tolerances.AngularImpulseTolerance) violations.Add("ANGULAR_IMPULSE_MISMATCH");
            Vec3 expectedVelocity = (sample.Normal * result.NormalImpulseNs + result.TangentImpulseNs) * (1.0 / mass);
            if ((post.VelocityMS - pre.VelocityMS - expectedVelocity).Length > velocityTolerance + tolerances.NormalApproachEpsilonMS) violations.Add("VELOCITY_IMPULSE_MISMATCH");
            if (Math.Abs(Dot(post.AngularVelocityRadS - pre.AngularVelocityRadS, sample.Normal)) > tolerances.NormalRotationToleranceRadS) violations.Add("NORMAL_ROTATION_CHANGED");
            double surfaceOffset = Dot(post.PositionM - sample.ContactPositionM, sample.Normal) - ball.RadiusM;
            if (Math.Abs(surfaceOffset) > tolerances.DeepPenetrationEpsilonM) violations.Add("POSITION_NOT_ON_SURFACE");
            return violations;
        }
    }
}
