using System;
using TennisSim.Core;
using TennisSim.Core.Bounce;

namespace TennisSim.Cli;

// Fixed inputs exercise the production Core bounce model. Expected values are analytic or an
// independent recomputation of the documented V1 equations; none of them is measured data.
public static class BounceScenarios
{
    // Engine coordinates: X width, Y up, Z court length. The instruction document is written with
    // z up; a true vector relabels components, a polar vector (spin) additionally flips sign.
    private static BallSpec Ball() => BounceProfiles.NominalType2();

    private static InteractionProfile Profile(double en, double mu, double beta)
    {
        var profile = new InteractionProfile { Id = "fixture-v1", Revision = 1, ModelId = BounceModel.ModelIdV1, BallSpecId = BounceProfiles.NominalBallId, SurfaceId = BounceProfiles.DesignSurfaceId, Evidence = EvidenceType.SYNTHETIC, CalibrationStatus = CalibrationStatus.UNCALIBRATED, Notes = "analytic fixture profile, not measured" };
        profile.NormalResponse.Kind = NormalResponseKind.Constant; profile.NormalResponse.En0 = en;
        profile.FrictionResponse.Kind = FrictionResponseKind.Constant; profile.FrictionResponse.Mu0 = mu;
        if (beta == 0) profile.TangentialResponse.Kind = TangentialResponseKind.Zero;
        else { profile.TangentialResponse.Kind = TangentialResponseKind.ConstantBeta; profile.TangentialResponse.Beta = beta; }
        profile.Validate();
        return profile;
    }

    private static BounceResult Resolve(Vec3 velocity, Vec3 spin, double en, double mu, double beta, BounceTolerances? tolerances = null)
    {
        var ball = Ball();
        var pre = new ImpactState { PositionM = new Vec3(0, ball.RadiusM, 0), VelocityMS = velocity, AngularVelocityRadS = spin, ImpactTimeS = 0 };
        var sample = BounceModel.SampleAtContact(pre, ball, "fixture", "fixture");
        return BounceModel.Resolve(pre, ball, null, sample, Profile(en, mu, beta), tolerances ?? new BounceTolerances());
    }

    public static System.Collections.Generic.List<AuditScenarios.Result> Run()
    {
        var results = new System.Collections.Generic.List<AuditScenarios.Result>();
        void Add(string name, bool pass, double observed, string expected, string unit, string basis, string category = "CORRECTNESS_BUG") => results.Add(new(name, pass ? "PASS" : "FAIL", category, observed, expected, unit, basis));

        // Document fixtures recomputed independently in engine coordinates (see docs/BOUNCE_MODEL.md).
        double worst = 0;
        var fixture = new (string Name, Vec3 V, Vec3 W, double Mu, double Beta, Vec3 VOut, Vec3 WOut)[]
        {
            ("sliding", new Vec3(0, -8, 20), new Vec3(), 0.3, 0, new Vec3(0, 6.4, 15.68), new Vec3(234.464043419267, 0, 0)),
            ("zero_end_slip", new Vec3(0, -8, 20), new Vec3(), 0.6, 0, new Vec3(0, 6.4, 12.903225806452), new Vec3(385.170919595570, 0, 0)),
            ("tangent_reversal", new Vec3(0, -8, 20), new Vec3(), 0.6, 0.2, new Vec3(0, 6.4, 11.483870967742), new Vec3(462.205103514685, 0, 0)),
            ("overspin", new Vec3(0, -5, 10), new Vec3(500, 0, 0), 0.6, 0, new Vec3(0, 4.0, 12.395161290323), new Vec3(370.004814636495, 0, 0))
        };
        foreach (var item in fixture)
        {
            var r = Resolve(item.V, item.W, 0.8, item.Mu, item.Beta);
            double error = Math.Max((r.PostState.VelocityMS - item.VOut).Length, (r.PostState.AngularVelocityRadS - item.WOut).Length / 1000);
            worst = Math.Max(worst, error);
            bool identical = r.Status == BounceStatus.RESOLVED && error < 1e-9;
            Add("bounce.fixture." + item.Name, identical, error, "<1e-9", "mixed", "V1 equations recomputed independently in engine coordinates; synthetic fixture, not a court property");
        }
        Add("bounce.fixture.worstError", worst < 1e-9, worst, "<1e-9", "mixed", "Worst of the four documented fixtures");

        // Energy non-increase, friction bound and invariants over a deterministic sweep.
        int cases = 0, violations = 0, energyGain = 0, frictionViolation = 0; double maxRatio = 0;
        foreach (double vt in new[] { 2.0, 5, 10, 20, 40 })
            foreach (double vn in new[] { 0.5, 1, 4, 8, 20 })
                foreach (double spin in new[] { -800.0, 0, 300, 800 })
                    foreach (double mu in new[] { 0.05, 0.2, 0.6, 1.5 })
                        foreach (double beta in new[] { 0.0, 0.2, 0.8 })
                        {
                            var ball = Ball();
                            var pre = new ImpactState { PositionM = new Vec3(0, ball.RadiusM, 0), VelocityMS = new Vec3(0, -vn, vt), AngularVelocityRadS = new Vec3(spin, 0, 0), ImpactTimeS = 0 };
                            var sample = BounceModel.SampleAtContact(pre, ball, "fixture", "fixture");
                            var profile = Profile(0.8, mu, beta);
                            var r = BounceModel.Resolve(pre, ball, null, sample, profile, new BounceTolerances());
                            cases++;
                            int found = BounceModel.CheckInvariants(pre, r, ball, sample, new BounceTolerances()).Count;
                            violations += found;
                            double ratio = r.EnergyBeforeJ > 0 ? r.EnergyAfterJ / r.EnergyBeforeJ : 1;
                            maxRatio = Math.Max(maxRatio, ratio);
                            if (ratio > 1 + 1e-12) energyGain++;
                            if (r.TangentImpulseNs.Length > mu * r.NormalImpulseNs + 1e-12) frictionViolation++;
                            if (r.Status != BounceStatus.RESOLVED) violations++;
                        }
        Add("bounce.property.energyNonIncrease", energyGain == 0 && maxRatio <= 1 + 1e-12, maxRatio, "<=1+1e-12", "ratio", "Sweep of " + cases + " impacts: vt/vn/spin/mu/beta; tangential energy identity plus normal COR bound");
        Add("bounce.property.frictionBound", frictionViolation == 0, frictionViolation, "0", "cases", "|Jt| <= mu*Jn for every swept impact");
        Add("bounce.property.invariants", violations == 0, violations, "0", "violations", "Energy, impulse, friction, angular impulse, tangent and surface-placement invariants");

        var tieBall = Ball();
        double tieMass = 1.0 / (1.0 / tieBall.MassKg + tieBall.RadiusM * tieBall.RadiusM / tieBall.InertiaKgM2);
        double tieNormalImpulse = 1.8 * tieBall.MassKg * 8;
        double tieMu = tieMass * 20.0 / tieNormalImpulse;
        var tie = Resolve(new Vec3(0, -8, 20), new Vec3(), 0.8, tieMu, 0.0);
        Add("bounce.classification.slipEqualsGoal", tie.ActiveImpulseLimit == ActiveImpulseLimit.TIE_WITHIN_TOLERANCE, tie.ActiveImpulseLimit == ActiveImpulseLimit.TIE_WITHIN_TOLERANCE ? 1 : 0, "1 = TIE_WITHIN_TOLERANCE", "boolean", "mu solved so q_limit equals q_goal exactly (mu=" + tieMu.ToString("R") + ")");

        // Rolling contact: v + omega x r = 0 gives no tangential impulse.
        double rollSpin = 20.0 / Court.BallRadius;
        var rolling = Resolve(new Vec3(0, -8, 20), new Vec3(rollSpin, 0, 0), 0.8, 0.6, 0);
        Add("bounce.classification.nearZeroSlip", rolling.ActiveImpulseLimit == ActiveImpulseLimit.NEAR_ZERO_SLIP && rolling.TangentImpulseNs.Length == 0, rolling.TangentImpulseNs.Length, "0 impulse, NEAR_ZERO_SLIP", "N*s", "omega = v/R about the width axis removes contact-point slip");

        int limitErrors = 0;
        if (Resolve(new Vec3(0, -8, 20), new Vec3(), 0.8, 0.05, 0).ActiveImpulseLimit != ActiveImpulseLimit.COULOMB_LIMITED) limitErrors++;
        if (Resolve(new Vec3(0, -8, 20), new Vec3(), 0.8, 1.5, 0.8).ActiveImpulseLimit != ActiveImpulseLimit.TANGENTIAL_TARGET_LIMITED) limitErrors++;
        Add("bounce.classification.limitModes", limitErrors == 0, limitErrors, "2/2 expected modes", "cases", "Low mu saturates on Coulomb; high mu with beta>0 saturates on the tangential target");

        // Separation and penetration are classified, not treated as new collisions.
        var separating = Resolve(new Vec3(0, 5, 3), new Vec3(), 0.8, 0.5, 0);
        Add("bounce.reject.separating", separating.Status == BounceStatus.SEPARATING_NO_IMPULSE && separating.PostState.VelocityMS.Y == 5 && separating.NormalImpulseNs == 0, separating.NormalImpulseNs, "0 impulse, SEPARATING_NO_IMPULSE", "N*s", "Outgoing normal velocity: no impulse may be applied");
        var deep = new ImpactState { PositionM = new Vec3(0, Court.BallRadius - 1e-3, 0), VelocityMS = new Vec3(0, -5, 3), AngularVelocityRadS = new Vec3(), ImpactTimeS = 0 };
        var ballForDeep = Ball();
        var deepSample = new SurfaceSample { ContactPositionM = new Vec3(0, 0, 0), Normal = new Vec3(0, 1, 0), MaterialId = "fixture", LocationId = "fixture" };
        var deepResult = BounceModel.Resolve(deep, ballForDeep, null, deepSample, Profile(0.8, 0.5, 0), new BounceTolerances());
        Add("bounce.reject.deepPenetration", deepResult.Status == BounceStatus.DEEP_INITIAL_PENETRATION && deepResult.PostState.VelocityMS.Y == -5, deepResult.Status == BounceStatus.DEEP_INITIAL_PENETRATION ? 1 : 0, "1", "boolean", "Initial penetration must not generate an impulse or a new collision event");

        // Rotation covariance and reflection handedness.
        var baseline = Resolve(new Vec3(0, -8, 20), new Vec3(), 0.8, 0.3, 0);
        var rotate = Mat3.Rotation(new Vec3(0, 1, 0), 0.7);
        var rotatedPre = rotate.ApplyState(new ImpactState { PositionM = new Vec3(0, Court.BallRadius, 0), VelocityMS = new Vec3(0, -8, 20), AngularVelocityRadS = new Vec3(), ImpactTimeS = 0 });
        var rotatedSample = BounceModel.SampleAtContact(rotatedPre, Ball(), "fixture", "fixture");
        var rotated = BounceModel.Resolve(rotatedPre, Ball(), null, rotatedSample, Profile(0.8, 0.3, 0), new BounceTolerances());
        double covarianceError = Math.Max((rotated.PostState.VelocityMS - rotate.Apply(baseline.PostState.VelocityMS)).Length, (rotated.PostState.AngularVelocityRadS - rotate.ApplyAngular(baseline.PostState.AngularVelocityRadS)).Length / 1000);
        Add("bounce.transform.rotationCovariance", covarianceError < 1e-9, covarianceError, "<1e-9", "mixed", "Same impact rotated about the up axis; velocity covariant, spin covariant as a polar vector");
        var reflect = Mat3.Reflection(0);
        var mirroredPre = reflect.ApplyState(new ImpactState { PositionM = new Vec3(0, Court.BallRadius, 0), VelocityMS = new Vec3(0, -8, 20), AngularVelocityRadS = new Vec3(), ImpactTimeS = 0 });
        var mirrored = BounceModel.Resolve(mirroredPre, Ball(), null, BounceModel.SampleAtContact(mirroredPre, Ball(), "fixture", "fixture"), Profile(0.8, 0.3, 0), new BounceTolerances());
        double mirrorError = Math.Max((mirrored.PostState.VelocityMS - reflect.Apply(baseline.PostState.VelocityMS)).Length, (mirrored.PostState.AngularVelocityRadS - reflect.ApplyAngular(baseline.PostState.AngularVelocityRadS)).Length / 1000);
        Add("bounce.transform.reflectionHandedness", mirrorError < 1e-9, mirrorError, "<1e-9", "mixed", "Mirror transform: spin is a polar vector, so det(A)=-1 flips it while velocity does not");

        // Runtime path: the legacy model must be untouched by introducing the surface argument.
        var legacyConfig = new SimConfig();
        var drop = new BallState { Position = new Vec3(0, 2.54 + Court.BallRadius, 5) };
        var legacyOnly = BallPhysics.Advance(drop, 1.0 / 120, legacyConfig, e => e.After.Bounces < 2);
        var legacyWithSurface = BallPhysics.Advance(drop, 1.0 / 120, legacyConfig, e => e.After.Bounces < 2, new SurfaceEnvironment { Model = BounceModelKind.Legacy });
        double legacyDelta = (legacyOnly.Position - legacyWithSurface.Position).Length + Math.Abs(legacyOnly.Velocity.Y - legacyWithSurface.Velocity.Y);
        Add("bounce.runtime.legacyUnchanged", legacyDelta == 0, legacyDelta, "0", "mixed", "Legacy model selection must not alter the previous bounce code path");

        // Desktop reference: design en on a rigid, drag free drop reproduces the Type 2 rebound range.
        var impulseEnvironment = new SurfaceEnvironment { Model = BounceModelKind.ImpulseV1, Ball = Ball() };
        var noDrag = new SimConfig { Drag = 0 };
        var impulseDrop = BallStateForDrop(); Collision? impulseHit = null; double impulseTime = 0;
        while (impulseHit == null && impulseTime < 2)
        { impulseDrop = BallPhysics.Advance(impulseDrop, 1.0 / 240, noDrag, e => { impulseHit = e; return false; }, impulseEnvironment); impulseTime += 1.0 / 240; }
        double impulseApex = impulseHit == null ? 0 : impulseHit.After.Velocity.Y * impulseHit.After.Velocity.Y / (2 * 9.81);
        Add("bounce.designDrop.type2Reference", impulseApex >= 1.35 && impulseApex <= 1.47, impulseApex, "1.35..1.47 sampled lower bound", "m bottom", "UNCALIBRATED design en=0.74 on a rigid no-drag drop; design assumption, not a court measurement", "PARAMETER_MISMATCH");
        return results;
    }

    private static BallState BallStateForDrop() => new BallState { Position = new Vec3(0, 2.54 + Court.BallRadius, 5) };
}
