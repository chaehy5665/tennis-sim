using TennisSim.Core;
using TennisSim.Core.Bounce;

namespace TennisSim.Calibration;

public sealed class TargetSpec
{
    public double NormalSpeedRmseMS { get; set; } = 0.5;
    public double TangentialSpeedRmseMS { get; set; } = 1.0;
    public double AngularSpeedRmseRpm { get; set; } = 300;
    public double ExitAngleRmseDeg { get; set; } = 2.0;
    public double CprSpreadPoints { get; set; } = 2.0;
}

public sealed class FitConfig
{
    public string Model { get; set; } = "M2";
    public string TrainSplit { get; set; } = "train";
    public string ValidationSplit { get; set; } = "validation";
    public string TestSplit { get; set; } = "test";
    public double VrefMS { get; set; } = BounceModel.NominalVrefMS;
    public double HuberDelta { get; set; } = 1.5;
    public double PriorLambda { get; set; } = 0.0;
    public double MuMaxCap { get; set; } = 2.0;
    public double ParameterBoundAbs { get; set; } = 8.0;
    public double NormalApproachFloorMS { get; set; } = 0.5;
    public double AngleFloorTangentialMS { get; set; } = 1.0;
    public int MaxEvaluations { get; set; } = 30000;
    public double InitialStep { get; set; } = 0.2;
    public double Shrink { get; set; } = 0.5;
    public double StopStep { get; set; } = 1e-7;
    public int BootstrapResamples { get; set; } = 40;
    public uint BootstrapSeed { get; set; } = 20260915;
    public double BootstrapStopStep { get; set; } = 1e-4;
    public double ValidationRmseGainForComplexity { get; set; } = 0.03;
    public TargetSpec Targets { get; set; } = new();
    public List<double[]> ExtraStarts { get; set; } = new();
}

public sealed class ParameterFit
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public double? StandardError { get; set; }
    public double? BootstrapLow { get; set; }
    public double? BootstrapHigh { get; set; }
    public bool AtBoundary { get; set; }
    public string Status { get; set; } = "NOT_IDENTIFIED";
}

public sealed class Estimation
{
    public string Model { get; set; } = "";
    public List<string> ParameterNames { get; set; } = new();
    public double[] Parameters { get; set; } = Array.Empty<double>();
    public List<ParameterFit> Fits { get; set; } = new();
    public double TrainLoss { get; set; }
    public double ValidationLoss { get; set; }
    public double TrainNormalisedRmse { get; set; }
    public double ValidationNormalisedRmse { get; set; }
    // The joint metric covers every residual the fit itself uses (normal, tangential, angular). Model
    // selection and the complexity gate follow this number, not the normal component alone.
    public double TrainJointNormalisedRmse { get; set; }
    public double ValidationJointNormalisedRmse { get; set; }
    public int NormalRecords { get; set; }
    public int TangentialRecords { get; set; }
    public int AngularRecords { get; set; }
    public Dictionary<string, int> LimitModes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int Evaluations { get; set; }
    public InteractionProfile Profile { get; set; } = new();
    // Surface of the majority of the records the estimation actually used. Court or material labels
    // are never mapped onto a profile by name.
    public string SurfaceId { get; set; } = "";
}

public static class Fit
{
    public static BallSpec BallFor(BounceRecord record)
    {
        var ball = BounceProfiles.NominalType2();
        if (!string.IsNullOrWhiteSpace(record.BallSpecId) && record.BallSpecId != ball.Id)
            throw new ArgumentException("Record " + record.RecordId + " uses ball spec " + record.BallSpecId + "; only the nominal " + ball.Id + " spec is available, supply its own BallSpec first");
        return ball;
    }

    public static SurfaceSample Sample(BounceRecord record, BallSpec ball)
    {
        var normal = record.NormalVector;
        // Flat-contact records without a measured position derive the contact point from the normal;
        // the collision equations only use the normal and the surface clearance.
        return new SurfaceSample
        {
            ContactPositionM = record.Position - normal * ball.RadiusM,
            Normal = normal,
            MaterialId = record.SurfaceId,
            LocationId = record.Observed.PositionMeasured ? record.LocationId : "derived-flat-contact",
            ConditionMetadata = record.SurfaceConditionId
        };
    }

    public static ImpactState PreState(BounceRecord record) => new()
    {
        PositionM = record.Position,
        VelocityMS = record.VelocityBefore,
        AngularVelocityRadS = record.SpinBefore,
        ImpactTimeS = record.ImpactTimeS
    };

    public sealed class Quality
    {
        public double NormalErrorMS;
        public double TangentialErrorMS;
        public double AngularErrorRadS;
        public double? AngleErrorDeg;
        public ActiveImpulseLimit Limit;
        public Vec3 PredictedVelocityMS;
        public Vec3 PredictedAngularVelocityRadS;
    }

    // Forward evaluation of one record against a profile. The sim reads the record only through
    // ResolveBounce, so fitting and runtime share exactly one model implementation. Observation masks
    // are axis aligned: an unobserved component contributes no residual and no sigma weight.
    public static Quality? Evaluate(InteractionProfile profile, BounceRecord record, BounceTolerances tolerances)
    {
        var ball = BallFor(record);
        var result = BounceModel.Resolve(PreState(record), ball, null, Sample(record, ball), profile, tolerances);
        if (result.Status != BounceStatus.RESOLVED) return null;
        var normal = record.NormalVector;
        bool[] mask = record.Observed.VelocityAfter;
        bool[] angularMask = record.Observed.AngularVelocityAfter;
        var error = Masked(result.PostState.VelocityMS - record.VelocityAfter, mask);
        bool normalObserved = mask[NormalAxis(normal)];
        double normalError = normalObserved ? BounceModel.Dot(error, normal) : 0.0;
        double tangentialError = BounceModel.Perp(error, normal).Length;
        var angularErrorVector = Masked(result.PostState.AngularVelocityRadS - record.SpinAfter, angularMask);
        double angularError = angularErrorVector.Length;
        double predictedTangential = BounceModel.Perp(result.PostState.VelocityMS, normal).Length;
        double observedTangential = BounceModel.Perp(record.VelocityAfter, normal).Length;
        double? angleError = null;
        if (predictedTangential >= 1.0 && observedTangential >= 1.0)
        {
            double predictedAngle = Math.Atan2(BounceModel.Dot(result.PostState.VelocityMS, normal), predictedTangential);
            double observedAngle = Math.Atan2(BounceModel.Dot(record.VelocityAfter, normal), observedTangential);
            angleError = Math.Abs(predictedAngle - observedAngle) * 180.0 / Math.PI;
        }
        return new Quality
        {
            NormalErrorMS = normalError,
            TangentialErrorMS = tangentialError,
            AngularErrorRadS = angularError,
            AngleErrorDeg = angleError,
            Limit = result.ActiveImpulseLimit,
            PredictedVelocityMS = result.PostState.VelocityMS,
            PredictedAngularVelocityRadS = result.PostState.AngularVelocityRadS
        };
    }

    private static Vec3 Masked(Vec3 value, bool[] mask) => new Vec3(mask[0] ? value.X : 0.0, mask[1] ? value.Y : 0.0, mask[2] ? value.Z : 0.0);

    private static int NormalAxis(Vec3 normal)
    {
        double x = Math.Abs(normal.X), y = Math.Abs(normal.Y), z = Math.Abs(normal.Z);
        return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
    }

    // Standard deviation of the observed components only; an unobserved component must not change
    // the weight of the residual vector.
    public static double ObservedSigma(double[] sigma, bool[] mask)
    {
        double sum = 0; int count = 0;
        for (int i = 0; i < 3; i++) if (mask[i]) { sum += sigma[i] * sigma[i]; count++; }
        return count == 0 ? sigma[Math.Max(0, Array.FindIndex(sigma, s => s > 0))] : Math.Sqrt(sum / count);
    }

    public static InteractionProfile BuildProfile(string model, double[] p, double vrefMs, string ballSpecId, string surfaceId)
    {
        var profile = new InteractionProfile { Id = "fit-" + model, Revision = 1, ModelId = BounceModel.ModelIdV1, BallSpecId = ballSpecId, SurfaceId = surfaceId, VrefMS = vrefMs };
        switch (model)
        {
            case "M1":
                profile.NormalResponse.Kind = NormalResponseKind.Constant; profile.NormalResponse.En0 = p[0];
                profile.FrictionResponse.Kind = FrictionResponseKind.Constant; profile.FrictionResponse.Mu0 = p[1];
                profile.TangentialResponse.Kind = TangentialResponseKind.Zero;
                return profile;
            case "M1B":
                profile.NormalResponse.Kind = NormalResponseKind.Constant; profile.NormalResponse.En0 = p[0];
                profile.FrictionResponse.Kind = FrictionResponseKind.Constant; profile.FrictionResponse.Mu0 = p[1];
                profile.TangentialResponse.Kind = TangentialResponseKind.ConstantBeta; profile.TangentialResponse.Beta = p[2];
                return profile;
            case "M2":
                profile.NormalResponse.Kind = NormalResponseKind.StateDependentSigmoid;
                profile.NormalResponse.A0 = p[0]; profile.NormalResponse.A1 = p[1]; profile.NormalResponse.A2 = p[2];
                profile.FrictionResponse.Kind = FrictionResponseKind.Constant; profile.FrictionResponse.Mu0 = p[3];
                profile.TangentialResponse.Kind = TangentialResponseKind.Zero;
                return profile;
            case "M3":
                profile.NormalResponse.Kind = NormalResponseKind.StateDependentSigmoid;
                profile.NormalResponse.A0 = p[0]; profile.NormalResponse.A1 = p[1]; profile.NormalResponse.A2 = p[2];
                profile.FrictionResponse.Kind = FrictionResponseKind.StateDependentSigmoid;
                profile.FrictionResponse.MuMax = p[3]; profile.FrictionResponse.B0 = p[4]; profile.FrictionResponse.B1 = p[5];
                profile.TangentialResponse.Kind = TangentialResponseKind.ConstantBeta; profile.TangentialResponse.Beta = p[6];
                return profile;
            default: throw new ArgumentException("Unknown model " + model + "; expected M1, M2 or M3");
        }
    }

    public static string[] ParameterNames(string model) => model switch
    {
        "M1" => new[] { "en", "mu_eff" },
        "M1B" => new[] { "en", "mu_eff", "beta_grip" },
        "M2" => new[] { "en.a0", "en.a1", "en.a2", "mu_eff" },
        "M3" => new[] { "en.a0", "en.a1", "en.a2", "mu_max", "mu.b0", "mu.b1", "beta_grip" },
        _ => throw new ArgumentException("Unknown model " + model)
    };

    public static double[] LowerBounds(string model, FitConfig config) => model switch
    {
        "M1" => new[] { 0.02, 0.0 },
        "M1B" => new[] { 0.02, 0.0, 0.0 },
        "M2" => new[] { -config.ParameterBoundAbs, -config.ParameterBoundAbs, -config.ParameterBoundAbs, 0.0 },
        "M3" => new[] { -config.ParameterBoundAbs, -config.ParameterBoundAbs, -config.ParameterBoundAbs, 0.02, -config.ParameterBoundAbs, -config.ParameterBoundAbs, 0.0 },
        _ => throw new ArgumentException("Unknown model " + model)
    };

    public static double[] UpperBounds(string model, FitConfig config) => model switch
    {
        "M1" => new[] { 0.999, config.MuMaxCap },
        "M1B" => new[] { 0.999, config.MuMaxCap, 0.999 },
        "M2" => new[] { config.ParameterBoundAbs, config.ParameterBoundAbs, config.ParameterBoundAbs, config.MuMaxCap },
        "M3" => new[] { config.ParameterBoundAbs, config.ParameterBoundAbs, config.ParameterBoundAbs, config.MuMaxCap, config.ParameterBoundAbs, config.ParameterBoundAbs, 0.999 },
        _ => throw new ArgumentException("Unknown model " + model)
    };

    public static double[] Initial(string model) => model switch
    {
        "M1" => new[] { 0.80, 0.40 },
        "M1B" => new[] { 0.80, 0.40, 0.10 },
        "M2" => new[] { 1.60, 0.0, 0.0, 0.40 },
        "M3" => new[] { 1.60, 0.0, 0.0, 0.80, 0.0, 0.0, 0.0 },
        _ => throw new ArgumentException("Unknown model " + model)
    };

    private static int[] NormalIndices(string model) => model switch
    {
        "M1" => new[] { 0 },
        "M1B" => new[] { 0 },
        "M2" => new[] { 0, 1, 2 },
        "M3" => new[] { 0, 1, 2 },
        _ => throw new ArgumentException("Unknown model " + model)
    };

    private static int[] FrictionIndices(string model) => model switch
    {
        "M1" => new[] { 1 },
        "M1B" => new[] { 1 },
        "M2" => new[] { 3 },
        "M3" => new[] { 3, 4, 5 },
        _ => throw new ArgumentException("Unknown model " + model)
    };

    private static int BetaIndex(string model) => model switch { "M1B" => 2, "M3" => 6, _ => -1 };

    private enum Stage { Normal, Tangential, Joint }

    private sealed class LossShape
    {
        public double Total;
        public double SumSquares;
        public int Count;
    }

    private static LossShape LossOf(Stage stage, string model, double[] parameters, IReadOnlyList<BounceRecord> records, FitConfig config, BounceTolerances tolerances)
    {
        var profile = BuildProfile(model, parameters, config.VrefMS, BounceProfiles.NominalBallId, "fit-domain");
        var shape = new LossShape();
        foreach (var record in records)
        {
            var quality = Evaluate(profile, record, tolerances);
            if (quality == null) continue;
            var residuals = new List<double>();
            double sigmaV = ObservedSigma(record.VelocityAfterStdDevMS, record.Observed.VelocityAfter);
            double sigmaW = ObservedSigma(record.AngularVelocityAfterStdDevRadS, record.Observed.AngularVelocityAfter);
            if (stage == Stage.Normal) residuals.Add(quality.NormalErrorMS / sigmaV);
            else
            {
                if (record.TangentialVelocityObserved) residuals.Add(0.7071067811865476 * quality.TangentialErrorMS / sigmaV);
                if (record.UsableForAngular) residuals.Add(0.5773502691896257 * quality.AngularErrorRadS / sigmaW);
            }
            foreach (double r in residuals)
            {
                shape.SumSquares += r * r;
                shape.Count++;
                double a = Math.Abs(r);
                shape.Total += a <= config.HuberDelta ? 0.5 * a * a : config.HuberDelta * (a - 0.5 * config.HuberDelta);
            }
        }
        if (config.PriorLambda > 0)
        {
            var prior = Initial(model);
            for (int i = 0; i < parameters.Length; i++) shape.Total += config.PriorLambda * Math.Pow(parameters[i] - prior[i], 2);
        }
        return shape;
    }

    private static double[] Minimise(Stage stage, string model, double[] start, int[] free, IReadOnlyList<BounceRecord> records, FitConfig config, BounceTolerances tolerances, double stopStep, out int evaluations, out double loss)
    {
        var lower = LowerBounds(model, config);
        var upper = UpperBounds(model, config);
        var x = (double[])start.Clone();
        for (int i = 0; i < x.Length; i++) x[i] = Math.Clamp(x[i], lower[i], upper[i]);
        double best = LossOf(stage, model, x, records, config, tolerances).Total;
        double step = config.InitialStep;
        evaluations = 1;
        while (step > stopStep && evaluations < config.MaxEvaluations)
        {
            bool improved = false;
            foreach (int index in free)
            {
                foreach (double direction in new[] { 1.0, -1.0 })
                {
                    if (evaluations >= config.MaxEvaluations) break;
                    var trial = (double[])x.Clone();
                    trial[index] = Math.Clamp(x[index] + direction * step, lower[index], upper[index]);
                    if (trial[index] == x[index]) continue;
                    double value = LossOf(stage, model, trial, records, config, tolerances).Total;
                    evaluations++;
                    if (value < best - 1e-15) { best = value; x = trial; improved = true; }
                }
            }
            if (!improved) step *= config.Shrink;
        }
        loss = best;
        return x;
    }

    public static Estimation Run(string model, BounceDataset dataset, FitConfig config, BounceTolerances tolerances, bool estimateBeta)
    {
        var train = dataset.Split(config.TrainSplit).Where(r => r.UsableForTangential && r.TangentialVelocityObserved).ToList();
        var trainNormal = dataset.Split(config.TrainSplit).Where(r => r.HasVelocityAfterObservation && Math.Abs(BounceModel.Dot(r.VelocityBefore, r.NormalVector)) >= config.NormalApproachFloorMS).ToList();
        var validation = dataset.Split(config.ValidationSplit).Where(r => r.UsableForTangential).ToList();
        var validationNormal = dataset.Split(config.ValidationSplit).Where(r => r.HasVelocityAfterObservation).ToList();
        if (trainNormal.Count == 0) throw new DataInsufficientException("No train records constrain the normal response");
        if (train.Count == 0) throw new DataInsufficientException("No train records constrain the tangential response (measured incident spin required)");

        var estimation = new Estimation { Model = model, ParameterNames = ParameterNames(model).ToList(), NormalRecords = trainNormal.Count, TangentialRecords = train.Count, AngularRecords = train.Count(r => r.UsableForAngular) };
        int evaluations = 0;
        var starts = new List<double[]> { Initial(model) };
        foreach (var start in config.ExtraStarts)
        {
            if (start.Length != Initial(model).Length) throw new ArgumentException("Extra start length does not match model " + model);
            starts.Add(start);
        }
        double[] bestParameters = Initial(model); double bestValidation = double.PositiveInfinity; double bestTrain = double.PositiveInfinity;
        int bestEvaluations = 0;
        foreach (var start in starts)
        {
            var candidate = (double[])start.Clone();
            // Stage B: normal response only, all spin-dependent information excluded.
            var normalFree = NormalIndices(model);
            candidate = Minimise(Stage.Normal, model, candidate, normalFree, trainNormal, config, tolerances, config.StopStep, out int e1, out _);
            evaluations += e1;
            // Stage C: tangential friction and response with the normal response held fixed.
            var frictionFree = FrictionIndices(model);
            if (estimateBeta && BetaIndex(model) >= 0)
            {
                var withBeta = frictionFree.Concat(new[] { BetaIndex(model) }).ToArray();
                candidate = Minimise(Stage.Tangential, model, candidate, withBeta, train, config, tolerances, config.StopStep, out int e2, out _);
                evaluations += e2;
            }
            else
            {
                candidate = Minimise(Stage.Tangential, model, candidate, frictionFree, train, config, tolerances, config.StopStep, out int e2, out _);
                evaluations += e2;
            }
            // Stage D: joint refinement.
            var allFree = Enumerable.Range(0, candidate.Length).Where(i => !(i == BetaIndex(model) && !estimateBeta)).ToArray();
            candidate = Minimise(Stage.Joint, model, candidate, allFree, train.Count >= trainNormal.Count ? train : trainNormal, config, tolerances, config.StopStep, out int e3, out _);
            evaluations += e3;
            double validationLoss = LossOf(Stage.Joint, model, candidate, validation, config, tolerances).Total + LossOf(Stage.Normal, model, candidate, validationNormal, config, tolerances).Total;
            if (validationLoss < bestValidation) { bestValidation = validationLoss; bestParameters = candidate; bestTrain = LossOf(Stage.Joint, model, candidate, train, config, tolerances).Total; bestEvaluations = evaluations; }
        }
        estimation.Parameters = bestParameters;
        estimation.TrainLoss = bestTrain;
        estimation.ValidationLoss = bestValidation;
        estimation.Evaluations = bestEvaluations;
        estimation.SurfaceId = train.GroupBy(r => r.SurfaceId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
        estimation.Profile = BuildProfile(model, bestParameters, config.VrefMS, BounceProfiles.NominalBallId, estimation.SurfaceId);
        estimation.TrainNormalisedRmse = Normalised(estimation.Profile, train, tolerances, joint: false);
        estimation.ValidationNormalisedRmse = Normalised(estimation.Profile, validation, tolerances, joint: false);
        estimation.TrainJointNormalisedRmse = Normalised(estimation.Profile, train, tolerances, joint: true);
        estimation.ValidationJointNormalisedRmse = Normalised(estimation.Profile, validation, tolerances, joint: true);
        foreach (var record in train) { var quality = Evaluate(estimation.Profile, record, tolerances); if (quality != null) estimation.LimitModes[quality.Limit.ToString()] = estimation.LimitModes.GetValueOrDefault(quality.Limit.ToString()) + 1; }
        Summarise(estimation, dataset, config, tolerances, model, estimateBeta);
        return estimation;
    }

    private static double Normalised(InteractionProfile profile, IReadOnlyList<BounceRecord> records, BounceTolerances tolerances, bool joint)
    {
        double sum = 0; int count = 0;
        foreach (var record in records)
        {
            var quality = Evaluate(profile, record, tolerances);
            if (quality == null) continue;
            double sigmaV = ObservedSigma(record.VelocityAfterStdDevMS, record.Observed.VelocityAfter);
            double sigmaW = ObservedSigma(record.AngularVelocityAfterStdDevRadS, record.Observed.AngularVelocityAfter);
            sum += quality.NormalErrorMS * quality.NormalErrorMS / (sigmaV * sigmaV);
            count++;
            if (!joint) continue;
            if (record.TangentialVelocityObserved) { sum += 0.5 * quality.TangentialErrorMS * quality.TangentialErrorMS / (sigmaV * sigmaV); count++; }
            if (record.UsableForAngular) { sum += (1.0 / 3.0) * quality.AngularErrorRadS * quality.AngularErrorRadS / (sigmaW * sigmaW); count++; }
        }
        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }

    private static void Summarise(Estimation estimation, BounceDataset dataset, FitConfig config, BounceTolerances tolerances, string model, bool estimateBeta)
    {
        var lower = LowerBounds(model, config);
        var upper = UpperBounds(model, config);
        var names = ParameterNames(model);
        var fits = new List<ParameterFit>();
        for (int i = 0; i < estimation.Parameters.Length; i++)
        {
            double value = estimation.Parameters[i];
            double tolerance = 1e-6 * Math.Max(1.0, Math.Abs(upper[i]));
            fits.Add(new ParameterFit { Name = names[i], Value = value, AtBoundary = Math.Abs(value - lower[i]) <= tolerance || Math.Abs(value - upper[i]) <= tolerance, Status = "FIXED" });
        }
        var normalFree = NormalIndices(model);
        var frictionFree = FrictionIndices(model);
        foreach (int i in normalFree) fits[i].Status = fits[i].AtBoundary ? "AT_BOUNDARY" : "IDENTIFIED";
        bool muBinds = estimation.LimitModes.ContainsKey(nameof(ActiveImpulseLimit.COULOMB_LIMITED));
        bool targetBinds = estimation.LimitModes.ContainsKey(nameof(ActiveImpulseLimit.TANGENTIAL_TARGET_LIMITED));
        string muStatus = muBinds && targetBinds ? "IDENTIFIED" : muBinds ? "LOWER_BOUND_ONLY" : targetBinds ? "UPPER_BOUND_ONLY" : "NOT_IDENTIFIED";
        foreach (int i in frictionFree)
        {
            fits[i].Status = fits[i].AtBoundary ? "AT_BOUNDARY" : muStatus;
            if (muStatus != "IDENTIFIED") estimation.Warnings.Add("mu is not bracketed by both contact modes in the fitting data; status " + muStatus);
        }
        int betaIndex = BetaIndex(model);
        if (betaIndex >= 0)
        {
            bool angular = estimation.AngularRecords > 0;
            fits[betaIndex].Status = !angular ? "NOT_IDENTIFIED" : fits[betaIndex].AtBoundary ? "AT_BOUNDARY" : "IDENTIFIED";
            if (!angular) { estimation.Warnings.Add("No record observes outgoing spin; beta cannot be identified and must be fixed at its prior"); estimateBeta = false; }
        }
        if (!muBinds && !targetBinds) estimation.Warnings.Add("No record reaches either impulse limit; the friction parameters are unconstrained");
        estimation.Fits = fits;

        // Cluster bootstrap over sessions. Groups are never split across resamples.
        var groups = dataset.Split(config.TrainSplit).Where(r => r.UsableForTangential).GroupBy(r => dataset.GroupOf(r)).ToList();
        if (config.BootstrapResamples > 0 && groups.Count > 1)
        {
            var rng = new SeedRandom(config.BootstrapSeed);
            var samples = new List<double[]>();
            for (int b = 0; b < config.BootstrapResamples; b++)
            {
                var resampled = new List<BounceRecord>();
                for (int g = 0; g < groups.Count; g++) resampled.AddRange(groups[(int)(rng.Next() * groups.Count)].ToList());
                var resampledNormal = resampled.Where(r => Math.Abs(BounceModel.Dot(r.VelocityBefore, r.NormalVector)) >= config.NormalApproachFloorMS).ToList();
                if (resampled.Count == 0 || resampledNormal.Count == 0) continue;
                var candidate = (double[])estimation.Parameters.Clone();
                candidate = Minimise(Stage.Normal, model, candidate, normalFree, resampledNormal, config, tolerances, config.BootstrapStopStep, out _, out _);
                var free = Enumerable.Range(0, candidate.Length).Where(i => !(i == betaIndex && !estimateBeta)).ToArray();
                candidate = Minimise(Stage.Joint, model, candidate, free, resampled, config, tolerances, config.BootstrapStopStep, out _, out _);
                samples.Add(candidate);
            }
            if (samples.Count > 4)
            {
                for (int i = 0; i < estimation.Parameters.Length; i++)
                {
                    var values = samples.Select(s => s[i]).OrderBy(v => v).ToArray();
                    double mean = values.Average();
                    double sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1));
                    // A degenerate spread means the resampling design does not identify this parameter's
                    // uncertainty; publish NOT_AVAILABLE rather than a fake zero standard error.
                    if (sd <= 1e-12)
                    {
                        fits[i].StandardError = null;
                        estimation.Warnings.Add("Cluster bootstrap did not move " + fits[i].Name + " across resamples; its uncertainty is NOT_AVAILABLE for this dataset design");
                    }
                    else fits[i].StandardError = sd;
                    fits[i].BootstrapLow = values[(int)Math.Floor(0.05 * (values.Length - 1))];
                    fits[i].BootstrapHigh = values[(int)Math.Ceiling(0.95 * (values.Length - 1))];
                }
                estimation.Warnings.Add("Uncertainty is a session-cluster bootstrap with " + samples.Count + " resamples (seed " + config.BootstrapSeed + "); it is not a population confidence interval");
            }
            else estimation.Warnings.Add("Bootstrap produced too few usable resamples; parameter uncertainty reported as NOT_AVAILABLE");
        }
        else estimation.Warnings.Add("Bootstrap not run: fewer than two independent session groups in the training split");
    }
}

public sealed class DataInsufficientException : Exception
{
    public DataInsufficientException(string message) : base(message) { }
}
