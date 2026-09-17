using TennisSim.Core;
using TennisSim.Core.Bounce;

namespace TennisSim.Tests;

// Bounce model tests. Expected values are analytic or independently recomputed; none is measured data.
public static class BounceTests
{
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Near(double a, double b, double eps = 1e-9, string message = "") => Check(Math.Abs(a - b) <= eps, message + " " + a + " != " + b);

    private static BallSpec Ball() => BounceProfiles.NominalType2();

    private static InteractionProfile Profile(double en, double mu, double beta, int revision = 1)
    {
        var profile = new InteractionProfile { Id = "fixture-v1", Revision = revision, ModelId = BounceModel.ModelIdV1, BallSpecId = BounceProfiles.NominalBallId, SurfaceId = BounceProfiles.DesignSurfaceId, Evidence = EvidenceType.SYNTHETIC };
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
        return BounceModel.Resolve(pre, ball, null, BounceModel.SampleAtContact(pre, ball, "fixture", "fixture"), Profile(en, mu, beta), tolerances ?? new BounceTolerances());
    }

    public static void Run(Action<string, Action> test)
    {
        var tolerances = new BounceTolerances();

        // Document fixtures ported to the engine frame (X width, Y up, Z length). A true vector
        // relabels components; a polar vector additionally flips sign for this det=-1 map.
        test("Bounce: documented impact fixtures match the V1 equations", () =>
        {
            var cases = new (string Name, Vec3 V, Vec3 W, double Mu, double Beta, Vec3 VOut, Vec3 WOut)[]
            {
                ("sliding", new Vec3(0, -8, 20), new Vec3(), 0.3, 0, new Vec3(0, 6.4, 15.68), new Vec3(234.464043419267, 0, 0)),
                ("zero_end_slip", new Vec3(0, -8, 20), new Vec3(), 0.6, 0, new Vec3(0, 6.4, 12.903225806452), new Vec3(385.170919595570, 0, 0)),
                ("tangent_reversal", new Vec3(0, -8, 20), new Vec3(), 0.6, 0.2, new Vec3(0, 6.4, 11.483870967742), new Vec3(462.205103514685, 0, 0)),
                ("overspin", new Vec3(0, -5, 10), new Vec3(500, 0, 0), 0.6, 0, new Vec3(0, 4.0, 12.395161290323), new Vec3(370.004814636495, 0, 0))
            };
            foreach (var item in cases)
            {
                var result = Resolve(item.V, item.W, 0.8, item.Mu, item.Beta);
                Check(result.Status == BounceStatus.RESOLVED, item.Name + " status " + result.Status);
                Near(result.PostState.VelocityMS.Z, item.VOut.Z, 1e-9, item.Name + " vz");
                Near(result.PostState.VelocityMS.Y, item.VOut.Y, 1e-9, item.Name + " vy");
                Near(result.PostState.AngularVelocityRadS.X, item.WOut.X, 1e-9, item.Name + " wx");
            }
            // Over-spin: the centre forward speed increases because contact slip reversed.
            var overspin = Resolve(new Vec3(0, -5, 10), new Vec3(500, 0, 0), 0.8, 0.6, 0);
            Check(overspin.PostState.VelocityMS.Z > 10, "Reversed contact slip must be able to speed the centre up, not be rejected");
        });

        test("Bounce: energy, friction and impulse invariants hold over a sweep", () =>
        {
            int cases = 0;
            foreach (double vt in new[] { 2.0, 5, 10, 20, 40 })
                foreach (double vn in new[] { 0.5, 1, 4, 8, 20 })
                    foreach (double spin in new[] { -800.0, 0, 300, 800 })
                        foreach (double mu in new[] { 0.05, 0.2, 0.6, 1.5 })
                            foreach (double beta in new[] { 0.0, 0.2, 0.8 })
                            {
                                var ball = Ball();
                                var pre = new ImpactState { PositionM = new Vec3(0, ball.RadiusM, 0), VelocityMS = new Vec3(0, -vn, vt), AngularVelocityRadS = new Vec3(spin, 0, 0), ImpactTimeS = 0 };
                                var sample = BounceModel.SampleAtContact(pre, ball, "fixture", "fixture");
                                var result = BounceModel.Resolve(pre, ball, null, sample, Profile(0.8, mu, beta), tolerances);
                                Check(result.Status == BounceStatus.RESOLVED, "status " + result.Status);
                                Check(result.Warnings.Count == 0, "warnings " + string.Join(",", result.Warnings));
                                Check(result.EnergyAfterJ <= result.EnergyBeforeJ + tolerances.EnergyAtolJ, "energy increased");
                                Check(result.TangentImpulseNs.Length <= result.MuEffectiveUsed * result.NormalImpulseNs + tolerances.ImpulseToleranceNs, "friction limit");
                                Check(BounceModel.CheckInvariants(pre, result, ball, sample, tolerances).Count == 0, "invariant violation");
                                Check(Math.Abs(BounceModel.Dot(result.PostState.AngularVelocityRadS - pre.AngularVelocityRadS, sample.Normal)) <= tolerances.NormalRotationToleranceRadS, "normal spin component changed");
                                cases++;
                            }
            Check(cases == 1200, "sweep size " + cases);
        });

        test("Bounce: tangential energy identity is non-positive", () =>
        {
            var ball = Ball();
            double mt = 1.0 / (1.0 / ball.MassKg + ball.RadiusM * ball.RadiusM / ball.InertiaKgM2);
            foreach (double mu in new[] { 0.05, 0.3, 1.0 })
                foreach (double beta in new[] { 0.0, 0.5, 1.0 })
                {
                    var slip = new Vec3(0, 0, 20);
                    var result = Resolve(new Vec3(0, -8, 20), new Vec3(), 0.8, mu, beta);
                    double change = BounceModel.TangentialEnergyChange(slip, result.TangentImpulseNs, mt);
                    Check(change <= 1e-12, "tangential energy change " + change + " at mu=" + mu + " beta=" + beta);
                }
        });

        test("Bounce: contact classification covers separation, penetration, slip and limits", () =>
        {
            var separating = Resolve(new Vec3(0, 5, 3), new Vec3(), 0.8, 0.5, 0);
            Check(separating.Status == BounceStatus.SEPARATING_NO_IMPULSE && separating.NormalImpulseNs == 0 && separating.PostState.VelocityMS.Y == 5);
            var grazing = Resolve(new Vec3(0, 0, 5), new Vec3(), 0.8, 0.5, 0);
            Check(grazing.Status == BounceStatus.TANGENTIAL_CONTACT_NO_IMPULSE && grazing.TangentImpulseNs.Length == 0);
            var ball = Ball();
            var deep = new ImpactState { PositionM = new Vec3(0, ball.RadiusM - 1e-3, 0), VelocityMS = new Vec3(0, -5, 3), AngularVelocityRadS = new Vec3(), ImpactTimeS = 0 };
            var deepSample = new SurfaceSample { ContactPositionM = new Vec3(0, 0, 0), Normal = new Vec3(0, 1, 0), MaterialId = "fixture", LocationId = "fixture" };
            var penetration = BounceModel.Resolve(deep, ball, null, deepSample, Profile(0.8, 0.5, 0), tolerances);
            Check(penetration.Status == BounceStatus.DEEP_INITIAL_PENETRATION && penetration.PostState.VelocityMS.Y == -5 && penetration.PostState.PositionM.Y == deep.PositionM.Y, "Deep penetration must not move or impulse the ball");
            double mt = 1.0 / (1.0 / ball.MassKg + ball.RadiusM * ball.RadiusM / ball.InertiaKgM2);
            double tieMu = mt * 20.0 / (1.8 * ball.MassKg * 8);
            Check(Resolve(new Vec3(0, -8, 20), new Vec3(), 0.8, tieMu, 0).ActiveImpulseLimit == ActiveImpulseLimit.TIE_WITHIN_TOLERANCE);
            Check(Resolve(new Vec3(0, -8, 20), new Vec3(), 0.8, 0.05, 0).ActiveImpulseLimit == ActiveImpulseLimit.COULOMB_LIMITED);
            Check(Resolve(new Vec3(0, -8, 20), new Vec3(), 0.8, 1.5, 0.8).ActiveImpulseLimit == ActiveImpulseLimit.TANGENTIAL_TARGET_LIMITED);
            double rollingSpin = 20.0 / ball.RadiusM;
            var rolling = Resolve(new Vec3(0, -8, 20), new Vec3(rollingSpin, 0, 0), 0.8, 0.6, 0);
            Check(rolling.ActiveImpulseLimit == ActiveImpulseLimit.NEAR_ZERO_SLIP && rolling.TangentImpulseNs.Length == 0 && rolling.BetaEffective == null);
        });

        test("Bounce: structural input is rejected explicitly", () =>
        {
            var ball = Ball();
            var pre = new ImpactState { PositionM = new Vec3(0, ball.RadiusM, 0), VelocityMS = new Vec3(0, -8, 20), ImpactTimeS = 0 };
            var sample = BounceModel.SampleAtContact(pre, ball, "fixture", "fixture");
            void Rejects(Action action, string what)
            {
                bool rejected = false;
                try { action(); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Expected explicit rejection: " + what);
            }
            var badMass = ball.Copy(); badMass.MassKg = double.NaN;
            Rejects(() => BounceModel.Resolve(pre, badMass, null, sample, Profile(0.8, 0.3, 0), tolerances), "NaN mass");
            var badRadius = ball.Copy(); badRadius.RadiusM = 0;
            Rejects(() => BounceModel.Resolve(pre, badRadius, null, sample, Profile(0.8, 0.3, 0), tolerances), "zero radius");
            var badNormal = sample.Copy(); badNormal.Normal = new Vec3(0.2, 0.9, 0);
            Rejects(() => BounceModel.Resolve(pre, ball, null, badNormal, Profile(0.8, 0.3, 0), tolerances), "unnormalized normal");
            var badVelocity = pre.Copy(); badVelocity.VelocityMS = new Vec3(0, double.PositiveInfinity, 0);
            Rejects(() => BounceModel.Resolve(badVelocity, ball, null, sample, Profile(0.8, 0.3, 0), tolerances), "non-finite velocity");
            var badBeta = Profile(0.8, 0.3, 0.2).Copy(); badBeta.TangentialResponse.Beta = 1.4;
            Rejects(() => BounceModel.Resolve(pre, ball, null, sample, badBeta, tolerances), "beta outside [0,1]");
            var badEn = Profile(0.8, 0.3, 0).Copy(); badEn.NormalResponse.En0 = 1.5;
            Rejects(() => BounceModel.Resolve(pre, ball, null, sample, badEn, tolerances), "en outside [0,1]");
            var badTolerances = new BounceTolerances { EnergyAtolJ = -1 };
            Rejects(() => BounceModel.Resolve(pre, ball, null, sample, Profile(0.8, 0.3, 0), badTolerances), "negative tolerance");
        });

        test("Bounce: out-of-domain evaluation is flagged without changing the input velocity", () =>
        {
            var ball = Ball();
            var profile = Profile(0.8, 0.3, 0);
            profile.Domain = new ProfileDomain { SnMin = 0, SnMax = 8, StMin = 0, StMax = 20, SuMin = 0, SuMax = 20 };
            var pre = new ImpactState { PositionM = new Vec3(0, ball.RadiusM, 0), VelocityMS = new Vec3(0, -25, 45), AngularVelocityRadS = new Vec3(), ImpactTimeS = 0 };
            var sample = BounceModel.SampleAtContact(pre, ball, "fixture", "fixture");
            var result = BounceModel.Resolve(pre, ball, null, sample, profile, tolerances);
            Check(result.Status == BounceStatus.RESOLVED && result.OutOfDomain, "Expected RESOLVED with out_of_domain");
            Check(result.Warnings.Contains("OUT_OF_DOMAIN"));
            // Only the parameter evaluation point is bounded: the physical impulse uses the real input.
            double expectedNormal = -(1 + 0.8) * ball.MassKg * (-25);
            Near(result.NormalImpulseNs, expectedNormal, 1e-12, "Clip must not alter the physical impulse");
            var inside = BounceModel.Resolve(new ImpactState { PositionM = pre.PositionM, VelocityMS = new Vec3(0, -5, 10), ImpactTimeS = 0 }, ball, null, sample, profile, tolerances);
            Check(!inside.OutOfDomain);
        });

        test("Bounce: rotation covariance and reflection handedness", () =>
        {
            var baseline = Resolve(new Vec3(0, -8, 20), new Vec3(120, 0, 0), 0.8, 0.3, 0.1);
            var rotation = Mat3.Rotation(new Vec3(0, 1, 0), 0.7);
            Check(Math.Abs(rotation.Determinant - 1) < 1e-12);
            var rotated = Transform(rotation, new Vec3(0, -8, 20), new Vec3(120, 0, 0));
            Check((rotated.PostState.VelocityMS - rotation.Apply(baseline.PostState.VelocityMS)).Length < 1e-9, "velocity must rotate");
            Check((rotated.PostState.AngularVelocityRadS - rotation.ApplyAngular(baseline.PostState.AngularVelocityRadS)).Length < 1e-9, "spin must rotate as a polar vector");
            var reflect = Mat3.Reflection(0);
            Check(Math.Abs(reflect.Determinant + 1) < 1e-12);
            var mirrored = Transform(reflect, new Vec3(0, -8, 20), new Vec3(120, 0, 0));
            Check((mirrored.PostState.VelocityMS - reflect.Apply(baseline.PostState.VelocityMS)).Length < 1e-8, "mirrored velocity mismatch");
            Check((mirrored.PostState.AngularVelocityRadS - reflect.ApplyAngular(baseline.PostState.AngularVelocityRadS)).Length < 1e-7, "mirrored spin must flip for det=-1");
            var roundTrip = reflect.Transpose().ApplyAngular(reflect.ApplyAngular(new Vec3(1, 2, 3)));
            Check((roundTrip - new Vec3(1, 2, 3)).Length < 1e-12, "transform round trip");
        });

        test("Bounce: spin is carried through flight and only the impulse model consumes it", () =>
        {
            var ball = Ball();
            var spinning = new BallState { Position = new Vec3(0, 1, 0), Velocity = new Vec3(0, 2, 10), AngularVelocity = new Vec3(300, 0, 0) };
            var flown = BallPhysics.At(spinning, 0.25, new SimConfig());
            Near(flown.AngularVelocity.X, 300, 0, "Spin must persist through flight in V1");
            var config = new SimConfig();
            var environment = new SurfaceEnvironment { Model = BounceModelKind.ImpulseV1, Ball = ball };
            Collision? impulse = null;
            var falling = new BallState { Position = new Vec3(0, 1, 5), Velocity = new Vec3(0, -8, 20) };
            for (int i = 0; i < 40 && impulse == null; i++)
                falling = BallPhysics.Advance(falling, config.TickSeconds, config, e => { impulse = e; return false; }, environment);
            Check(impulse != null && impulse.Bounce != null && Math.Abs(impulse.After.AngularVelocity.X) > 100, "Impulse model must transfer friction into spin");
            Collision? legacy = null;
            var fallingLegacy = new BallState { Position = new Vec3(0, 1, 5), Velocity = new Vec3(0, -8, 20) };
            for (int i = 0; i < 40 && legacy == null; i++)
                fallingLegacy = BallPhysics.Advance(fallingLegacy, config.TickSeconds, config, e => { legacy = e; return false; });
            Check(legacy != null && legacy.Bounce == null && legacy.After.AngularVelocity.Length == 0, "Legacy bounce must not create spin or diagnostics");
        });

        test("Bounce: event integration records exactly one resolved contact per bounce", () =>
        {
            var input = new MatchInput { Seed = 42 };
            input.Surface = new SurfaceEnvironment { Model = BounceModelKind.ImpulseV1 };
            var engine = new MatchEngine(input);
            var record = engine.Run();
            Check(record.Status == "Completed", record.Diagnostic);
            var bounces = record.Events.Where(e => e.Kind == "BallBounced").ToList();
            Check(bounces.Count > 0);
            Check(bounces.All(e => e.Bounce != null && e.Bounce.ProfileHash == input.Surface.ProfileContentHash), "Every bounce must carry the resolved profile identity");
            Check(bounces.All(e => e.Bounce!.Status == BounceStatus.RESOLVED || e.Bounce.Status == BounceStatus.SETTLED));
            Check(bounces.All(e => e.Bounce!.PostState.PositionM.Y >= Court.BallRadius - 1e-9), "Contact must leave the ball on the surface");
            Check(record.Input.Surface != null && record.Input.Surface.Profile.Id == input.Surface.Profile.Id, "The replay input must carry the surface profile");
            // Chunking must not change the impulse-model record.
            string json = TennisSim.Cli.ReplayJson.Serialize(record);
            var chunked = new MatchEngine(TennisSim.Cli.ReplayJson.Deserialize<MatchInput>(TennisSim.Cli.ReplayJson.Serialize(input)));
            while (!chunked.Finished) chunked.AdvanceTicks(137);
            Check(json == TennisSim.Cli.ReplayJson.Serialize(chunked.Record), "Impulse-model record must not depend on tick chunking");
        });

        test("Bounce: predictor and actual flight agree with the impulse model enabled", () =>
        {
            var ball = Ball();
            var config = new SimConfig();
            var environment = new SurfaceEnvironment { Model = BounceModelKind.ImpulseV1, Ball = ball };
            // Bounces inside the court, travelling with the player's end and away from the net plane.
            var observed = new BallState { Position = new Vec3(1, 2.0, 7), Velocity = new Vec3(0, -2, 8) };
            var player = PlayerProfile.Preset("baseline", "A");
            const int end = 1;
            // Independent scan with the same model, using the same step the predictor uses.
            var advance = observed.Copy(); double elapsed = 0; Vec3? candidate = null;
            for (int i = 0; i < 240 && candidate == null; i++)
            {
                advance = BallPhysics.Advance(advance, 1.0 / 60, config, null, environment); elapsed += 1.0 / 60;
                if (advance.Bounces == 1 && advance.Position.Z * end > 0 && advance.Position.Y >= config.MinContactHeight && advance.Position.Y <= config.MaxContactHeight && advance.Velocity.Z * end > 0 && advance.Velocity.Z > 0)
                    candidate = new Vec3(advance.Position.X, 0, advance.Position.Z);
            }
            Check(candidate.HasValue, "Expected an eligible post-bounce contact window");
            var state = new PlayerState { End = end, Position = candidate!.Value };
            var predicted = Movement.PredictContact(player, state, observed, config, 0.3, out double arrival, out bool reachable, environment);
            Check(reachable, "A player standing at the bounce position must reach the contact");
            Near(predicted.X, candidate.Value.X, 1e-9, "Prediction must use the same bounce model as the engine");
            Near(predicted.Z, candidate.Value.Z, 1e-9, "Prediction must use the same bounce model as the engine");
            Near(arrival, elapsed, 1e-12, "Predicted contact time must match the simulated flight");
            // The predictor must also use the configured model, not a hard coded legacy path.
            var legacyPrediction = Movement.PredictContact(player, state, observed, config, 0.3, out _, out _, null);
            Check((predicted - legacyPrediction).Length > 1e-6, "Predictor must follow the configured bounce model");
        });

        test("Bounce: low-energy contact settles at rest without an unbounded loop", () =>
        {
            var config = new SimConfig();
            var environment = new SurfaceEnvironment { Model = BounceModelKind.ImpulseV1, Ball = Ball() };
            var resting = new BallState { Position = new Vec3(0, Court.BallRadius + 1e-7, 3), Velocity = new Vec3(0, -0.05, 0.05) };
            var previous = resting.Copy();
            for (int i = 0; i < 400; i++)
            {
                resting = BallPhysics.Advance(resting, config.TickSeconds, config, null, environment);
                Check(resting.Position.IsFinite && resting.Velocity.IsFinite, "Settling state must stay finite");
                Check(resting.Velocity.Length <= previous.Velocity.Length + 1e-9, "Settling must not gain energy");
                previous = resting.Copy();
            }
            Check(resting.Velocity.Length < 0.06, "A low energy contact must lose speed, not accelerate");
            // The contact holds the ball on the surface instead of bouncing it to zero height.
            Check(resting.Bounces >= 1 && Math.Abs(resting.Position.Y - Court.BallRadius) < 1e-12, "Settled ball must sit on the surface");
            Check(Math.Abs(resting.Velocity.Y) < 1e-12, "Settled ball must have no unresolved normal approach");
            // The settling decision is published, not silent: the last contact reports SETTLED with no impulse.
            Collision? settledContact = null;
            var single = new BallState { Position = new Vec3(0, Court.BallRadius + 1e-7, 3), Velocity = new Vec3(0, -0.05, 0.05) };
            var contacts = new List<Collision>();
            for (int i = 0; i < 20 && settledContact == null; i++)
            {
                contacts.Clear();
                single = BallPhysics.Advance(single, config.TickSeconds, config, e => { contacts.Add(e); return true; }, environment);
                settledContact = contacts.LastOrDefault(e => e.Bounce != null && e.Bounce.Status == BounceStatus.SETTLED);
            }
            Check(settledContact != null && settledContact.Bounce != null, "Expected a final contact to settle");
            Check(settledContact!.Bounce!.Status == BounceStatus.SETTLED, "Final contact status must be SETTLED, got " + settledContact.Bounce.Status);
            Check(settledContact.Bounce.Warnings.Contains("SETTLED_CONTACT") && settledContact.Bounce.NormalImpulseNs == 0, "Settled contact must publish its policy and apply no impulse");
            Check(Math.Abs(settledContact.After.Velocity.Y) < 1e-12 && Math.Abs(settledContact.After.Position.Y - Court.BallRadius) < 1e-12, "Settled contact must leave the ball resting on the surface");
            // A single very long step must also terminate: the settling policy closes the contact.
            var burst = new BallState { Position = new Vec3(0, Court.BallRadius + 1e-7, 3), Velocity = new Vec3(0, -0.05, 0.05) };
            var settled = BallPhysics.Advance(burst, 5.0, config, null, environment);
            Check(settled.Position.IsFinite && Math.Abs(settled.Position.Y - Court.BallRadius) < 1e-12, "Long step must settle on the surface");
        });

        test("Bounce: profile hash is stable, content sensitive and replay safe", () =>
        {
            var profile = Profile(0.8, 0.3, 0, revision: 3);
            string hash = ProfileHash.Compute(profile);
            Check(hash.Length == 64 && hash == ProfileHash.Compute(profile.Copy()), "Hash must be stable across copies");
            var changed = profile.Copy(); changed.FrictionResponse.Mu0 = 0.31;
            Check(hash != ProfileHash.Compute(changed), "Hash must change with content");
            var revisioned = profile.Copy(); revisioned.Revision = 4;
            Check(hash != ProfileHash.Compute(revisioned), "Hash must change with revision");
            string json = TennisSim.Cli.ReplayJson.Serialize(profile);
            Check(ProfileHash.Compute(TennisSim.Cli.ReplayJson.Deserialize<InteractionProfile>(json)) == hash, "Profile must survive JSON round trip with the same hash");
        });

        test("Bounce: lookup table tracks the analytic state-dependent model", () =>
        {
            var profile = Profile(0.8, 0.3, 0, revision: 2);
            profile.NormalResponse.Kind = NormalResponseKind.StateDependentSigmoid;
            profile.NormalResponse.A0 = 1.6; profile.NormalResponse.A1 = 0.5; profile.NormalResponse.A2 = 0.3;
            profile.FrictionResponse.Kind = FrictionResponseKind.StateDependentSigmoid;
            profile.FrictionResponse.MuMax = 0.9; profile.FrictionResponse.B0 = -0.4; profile.FrictionResponse.B1 = 0.6;
            profile.TangentialResponse.Kind = TangentialResponseKind.ConstantBeta; profile.TangentialResponse.Beta = 0.2;
            profile.Domain = new ProfileDomain { SnMin = 0, SnMax = 20, StMin = 0, StMax = 40, SuMin = 0, SuMax = 40 };
            profile.Validate();
            var table = TennisSim.Calibration.Tables.Build(profile, 5, 20, 40, 40);
            var comparison = TennisSim.Calibration.Tables.Compare(profile, table, 9, 20, 40, 40, tolerances, Ball(), 0.02, 0.05, 0.02);
            Check(comparison.Violations.Count == 0, string.Join("; ", comparison.Violations));
            Check(comparison.MaxEnDeviation > 0, "A state-dependent table must show a non-zero interpolation difference");
            Check(comparison.MaxVelocityDeviationMS < 0.05, "Table representation must stay close to the analytic model: " + comparison.MaxVelocityDeviationMS);
            var tabled = profile.Copy(); tabled.Table = table; tabled.Validate();
            var result = BounceModel.Resolve(new ImpactState { PositionM = new Vec3(0, Ball().RadiusM, 0), VelocityMS = new Vec3(0, -8, 20), ImpactTimeS = 0 }, Ball(), null, BounceModel.SampleAtContact(new ImpactState { PositionM = new Vec3(0, Ball().RadiusM, 0) }, Ball(), "x", "y"), tabled, tolerances);
            Check(result.Status == BounceStatus.RESOLVED && result.ProfileHash == ProfileHash.Compute(tabled));
        });

        test("Bounce: legacy runtime path is unchanged by the new surface argument", () =>
        {
            var config = new SimConfig();
            var drop = new BallState { Position = new Vec3(0, 2.54 + Court.BallRadius, 5) };
            Collision? plain = null; Collision? withSurface = null;
            var plainBall = drop.Copy(); var surfaceBall = drop.Copy();
            for (int i = 0; i < 200 && (plain == null || withSurface == null); i++)
            {
                if (plain == null) plainBall = BallPhysics.Advance(plainBall, 1.0 / 120, config, e => { plain = e; return false; });
                if (withSurface == null) surfaceBall = BallPhysics.Advance(surfaceBall, 1.0 / 120, config, e => { withSurface = e; return false; }, new SurfaceEnvironment { Model = BounceModelKind.Legacy });
            }
            Check(plain != null && withSurface != null);
            Check(plain!.After.Velocity.Y == withSurface!.After.Velocity.Y && plain.After.Velocity.Z == withSurface.After.Velocity.Z, "Legacy numbers must not move");
        });
    }

    private static BounceResult Transform(Mat3 transform, Vec3 velocity, Vec3 spin)
    {
        var ball = Ball();
        var pre = new ImpactState { PositionM = new Vec3(0, ball.RadiusM, 0), VelocityMS = velocity, AngularVelocityRadS = spin, ImpactTimeS = 0 };
        var sample = BounceModel.SampleAtContact(pre, ball, "fixture", "fixture");
        var transformedSample = sample.Copy();
        transformedSample.Normal = transform.Apply(sample.Normal);
        transformedSample.ContactPositionM = transform.Apply(sample.ContactPositionM);
        var transformedPre = transform.ApplyState(pre);
        return BounceModel.Resolve(transformedPre, ball, null, transformedSample, Profile(0.8, 0.3, 0.1), new BounceTolerances());
    }
}
