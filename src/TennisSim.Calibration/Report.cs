using TennisSim.Core;
using TennisSim.Core.Bounce;

namespace TennisSim.Calibration;

public sealed class GroupMetrics
{
    public int Count { get; set; }
    public double? NormalSpeedRmseMS { get; set; }
    public double? TangentialSpeedRmseMS { get; set; }
    public double? ExitAngleRmseDeg { get; set; }
}

public sealed class EvaluationReport
{
    public string Split { get; set; } = "";
    public string Model { get; set; } = "";
    public int Records { get; set; }
    public int Resolved { get; set; }
    public int Unresolved { get; set; }
    public int AngularObserved { get; set; }
    public int AngleExcludedBelowFloor { get; set; }
    public double? NormalSpeedRmseMS { get; set; }
    public double? NormalSpeedBiasMS { get; set; }
    public double? NormalSpeedP95MS { get; set; }
    public double? TangentialSpeedRmseMS { get; set; }
    public double? TangentialSpeedP95MS { get; set; }
    public double? AngularSpeedRmseRadS { get; set; }
    public double? AngularSpeedRmseRpm { get; set; }
    public double? AngularSpeedP95Rpm { get; set; }
    public double? ExitAngleRmseDeg { get; set; }
    public double? ExitAngleP95Deg { get; set; }
    // Structural diagnostic of the instruction document section 12.4, computed from the observations
    // alone: R_L = I (omega_after - omega_before) - r x (m (v_after - v_before)). V1 requires R_L to sit
    // inside the measurement uncertainty; a systematic residual means the impulse model is missing
    // physics (contact torque, moving reaction point, deformation, geometry or timing error).
    public int AngularImpulseResidualCount { get; set; }
    public double? AngularImpulseResidualMedianNs { get; set; }
    public double? AngularImpulseResidualMaxNs { get; set; }
    public double? AngularImpulseResidualMedianOverUncertainty { get; set; }
    public List<object> AngularImpulseResiduals { get; set; } = new();
    public Dictionary<string, GroupMetrics> BySurface { get; set; } = new();
    public Dictionary<string, GroupMetrics> BySession { get; set; } = new();
    public List<string> TargetViolations { get; set; } = new();
    public bool TargetsGated { get; set; }
    public List<string> Notes { get; set; } = new();
}

public static class Metrics
{
    private static double? Rmse(List<double> values) => values.Count == 0 ? null : Math.Sqrt(values.Sum(v => v * v) / values.Count);
    private static double? P95(List<double> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.Select(Math.Abs).OrderBy(v => v).ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(0.95 * sorted.Length) - 1)];
    }

    public static EvaluationReport Evaluate(InteractionProfile profile, IEnumerable<BounceRecord> records, string split, FitConfig config, BounceTolerances tolerances, bool gateTargets)
    {
        var report = new EvaluationReport { Split = split, Model = profile.ModelId };
        var normal = new List<double>(); var normalSigned = new List<double>(); var tangential = new List<double>(); var angular = new List<double>(); var angle = new List<double>();
        var bySurface = new Dictionary<string, List<BounceRecord>>(); var bySession = new Dictionary<string, List<BounceRecord>>();
        foreach (var record in records)
        {
            report.Records++;
            var quality = Fit.Evaluate(profile, record, tolerances);
            if (quality == null) { report.Unresolved++; continue; }
            report.Resolved++;
            normal.Add(quality.NormalErrorMS); normalSigned.Add(quality.NormalErrorMS); tangential.Add(quality.TangentialErrorMS);
            if (record.UsableForAngular && record.Observed.AngularVelocityAfter.Any(o => o)) { angular.Add(quality.AngularErrorRadS); report.AngularObserved++; }
            if (quality.AngleErrorDeg.HasValue) angle.Add(quality.AngleErrorDeg.Value); else report.AngleExcludedBelowFloor++;
            if (!bySurface.ContainsKey(record.SurfaceId)) bySurface[record.SurfaceId] = new List<BounceRecord>();
            bySurface[record.SurfaceId].Add(record);
            if (!bySession.ContainsKey(record.SessionId)) bySession[record.SessionId] = new List<BounceRecord>();
            bySession[record.SessionId].Add(record);
        }
        report.NormalSpeedRmseMS = Rmse(normal);
        report.NormalSpeedBiasMS = normalSigned.Count == 0 ? null : normalSigned.Average();
        report.NormalSpeedP95MS = P95(normal);
        report.TangentialSpeedRmseMS = Rmse(tangential);
        report.TangentialSpeedP95MS = P95(tangential);
        report.AngularSpeedRmseRadS = Rmse(angular);
        report.AngularSpeedRmseRpm = report.AngularSpeedRmseRadS * 60.0 / (2 * Math.PI);
        report.AngularSpeedP95Rpm = P95(angular) * 60.0 / (2 * Math.PI);
        report.ExitAngleRmseDeg = Rmse(angle);
        report.ExitAngleP95Deg = P95(angle);
        // Angular impulse residual, independent of the fitted profile.
        var residualMagnitudes = new List<double>();
        var residualRatios = new List<double>();
        foreach (var record in records)
        {
            if (!record.UsableForAngular) continue;
            var ball = Fit.BallFor(record);
            var recordNormal = record.NormalVector;
            Vec3 offset = recordNormal * (-ball.RadiusM);
            Vec3 deltaVelocity = record.VelocityAfter - record.VelocityBefore;
            Vec3 deltaSpin = record.SpinAfter - record.SpinBefore;
            Vec3 residual = deltaSpin * ball.InertiaKgM2 - BounceModel.Cross(offset, deltaVelocity * ball.MassKg);
            double sigmaSpin = Fit.ObservedSigma(record.AngularVelocityAfterStdDevRadS, record.Observed.AngularVelocityAfter);
            double sigmaVelocity = Fit.ObservedSigma(record.VelocityAfterStdDevMS, record.Observed.VelocityAfter);
            double sigma = Math.Sqrt(Math.Pow(ball.InertiaKgM2 * sigmaSpin, 2) + Math.Pow(ball.RadiusM * ball.MassKg * sigmaVelocity, 2));
            residualMagnitudes.Add(residual.Length);
            residualRatios.Add(sigma <= 0 ? 0 : residual.Length / sigma);
            report.AngularImpulseResiduals.Add(new { record.RecordId, surface = record.SurfaceId, residualNs = residual.Length, uncertaintyNs = sigma, ratio = sigma <= 0 ? 0 : residual.Length / sigma });
            report.AngularImpulseResidualCount++;
        }
        if (residualMagnitudes.Count > 0)
        {
            var ordered = residualMagnitudes.OrderBy(v => v).ToArray();
            var orderedRatios = residualRatios.OrderBy(v => v).ToArray();
            report.AngularImpulseResidualMedianNs = ordered[ordered.Length / 2];
            report.AngularImpulseResidualMaxNs = ordered[ordered.Length - 1];
            report.AngularImpulseResidualMedianOverUncertainty = orderedRatios[orderedRatios.Length / 2];
        }
        foreach (var pair in bySurface) report.BySurface[pair.Key] = Summarise(profile, pair.Value, tolerances);
        foreach (var pair in bySession) report.BySession[pair.Key] = Summarise(profile, pair.Value, tolerances);
        if (gateTargets)
        {
            if (report.NormalSpeedRmseMS > config.Targets.NormalSpeedRmseMS) report.TargetViolations.Add("normalSpeedRmse " + report.NormalSpeedRmseMS?.ToString("F4") + " > " + config.Targets.NormalSpeedRmseMS);
            if (report.TangentialSpeedRmseMS > config.Targets.TangentialSpeedRmseMS) report.TargetViolations.Add("tangentialSpeedRmse " + report.TangentialSpeedRmseMS?.ToString("F4") + " > " + config.Targets.TangentialSpeedRmseMS);
            if (report.AngularSpeedRmseRpm > config.Targets.AngularSpeedRmseRpm) report.TargetViolations.Add("angularSpeedRmse " + report.AngularSpeedRmseRpm?.ToString("F2") + " rpm > " + config.Targets.AngularSpeedRmseRpm);
            if (report.ExitAngleRmseDeg > config.Targets.ExitAngleRmseDeg) report.TargetViolations.Add("exitAngleRmse " + report.ExitAngleRmseDeg?.ToString("F4") + " deg > " + config.Targets.ExitAngleRmseDeg);
            report.TargetsGated = true;
        }
        else report.Notes.Add("Targets not gated: the evaluated evidence is synthetic or the caller requested reporting only. Limits in this report are descriptive.");
        report.Notes.Add("Partial observations are reported with masks and coverage; this is not a complete 3D validation when angular coverage is below the record count.");
        return report;
    }

    private static GroupMetrics Summarise(InteractionProfile profile, List<BounceRecord> records, BounceTolerances tolerances)
    {
        var normal = new List<double>(); var tangential = new List<double>(); var angle = new List<double>();
        foreach (var record in records)
        {
            var quality = Fit.Evaluate(profile, record, tolerances);
            if (quality == null) continue;
            normal.Add(quality.NormalErrorMS); tangential.Add(quality.TangentialErrorMS);
            if (quality.AngleErrorDeg.HasValue) angle.Add(quality.AngleErrorDeg.Value);
        }
        return new GroupMetrics { Count = records.Count, NormalSpeedRmseMS = Rmse(normal), TangentialSpeedRmseMS = Rmse(tangential), ExitAngleRmseDeg = Rmse(angle) };
    }
}

public sealed class VirtualItfResult
{
    public double IncidenceSpeedMS { get; set; } = 30.0;
    public double IncidenceAngleDeg { get; set; } = 16.0;
    public double IncidenceSpinRevPerS { get; set; }
    public double TemperatureC { get; set; } = 23.0;
    public double NormalIncidenceSpeedMS { get; set; }
    public double HorizontalIncidenceSpeedMS { get; set; }
    public double NormalExitSpeedMS { get; set; }
    public double HorizontalExitSpeedMS { get; set; }
    public double ETest { get; set; }
    public double MuTest { get; set; }
    public double E23 { get; set; }
    public double CprRaw { get; set; }
    public string ProfileId { get; set; } = "";
    public string ProfileHash { get; set; } = "";
    public string Evidence { get; set; } = "";
    public List<string> Notes { get; set; } = new();
}

// CPR-shaped virtual test (instruction document section 10). This is NOT the official ITF procedure.
public static class VirtualItf
{
    public static VirtualItfResult Compute(InteractionProfile profile, BallSpec ball, BounceTolerances tolerances, double temperatureC, double spinRevPerSecond)
    {
        var result = new VirtualItfResult { TemperatureC = temperatureC, IncidenceSpinRevPerS = spinRevPerSecond, ProfileId = profile.Id, ProfileHash = ProfileHash.Compute(profile), Evidence = profile.Evidence.ToString() };
        double angle = result.IncidenceAngleDeg * Math.PI / 180.0;
        double w = result.IncidenceSpeedMS * Math.Sin(angle);
        double u = result.IncidenceSpeedMS * Math.Cos(angle);
        result.NormalIncidenceSpeedMS = w;
        result.HorizontalIncidenceSpeedMS = u;
        var pre = new ImpactState { PositionM = new Vec3(0, ball.RadiusM, 0), VelocityMS = new Vec3(0, -w, u), AngularVelocityRadS = new Vec3(spinRevPerSecond * 2 * Math.PI, 0, 0), ImpactTimeS = 0 };
        var sample = BounceModel.SampleAtContact(pre, ball, profile.SurfaceId, "cs-01-02-condition");
        var bounce = BounceModel.Resolve(pre, ball, null, sample, profile, tolerances);
        if (bounce.Status != BounceStatus.RESOLVED) throw new DataInsufficientException("CS 01/02 shaped condition did not resolve: " + bounce.Status);
        result.NormalExitSpeedMS = BounceModel.Dot(bounce.PostState.VelocityMS, sample.Normal);
        result.HorizontalExitSpeedMS = BounceModel.Perp(bounce.PostState.VelocityMS, sample.Normal).Length;
        result.ETest = result.NormalExitSpeedMS / w;
        result.MuTest = (u - result.HorizontalExitSpeedMS) / (w * (1 + result.ETest));
        result.E23 = result.ETest + 0.003 * (23 - temperatureC);
        result.CprRaw = 100 * (1 - result.MuTest) + 150 * (0.81 - result.E23);
        result.Notes.Add("Raw CPR kept unrounded; no ITF classification band is asserted here.");
        result.Notes.Add("Incidence 30 m/s at 16 degrees with spin " + spinRevPerSecond + " rev/s; the CS 01/02 procedure and its surface conditioning are not reproduced.");
        result.Notes.Add("mu_test is computed from the virtual before/after speeds, not copied from the internal mu_eff parameter.");
        return result;
    }
}

public sealed class TableComparison
{
    public int Probes { get; set; }
    public double MaxEnDeviation { get; set; }
    public double MaxMuRelativeDeviation { get; set; }
    public double MaxBetaDeviation { get; set; }
    public double MaxVelocityDeviationMS { get; set; }
    public double MaxAngularDeviationRadS { get; set; }
    public int ClampedProbes { get; set; }
    public List<string> Violations { get; set; } = new();
}

public static class Tables
{
    public static ProfileTable Build(InteractionProfile profile, int nodes, double snMax, double stMax, double suMax)
    {
        if (nodes < 2) throw new ArgumentException("A lookup table needs at least two nodes per axis");
        var table = new ProfileTable
        {
            SnNodes = Axis(snMax, nodes),
            StNodes = Axis(stMax, nodes),
            SuNodes = Axis(suMax, nodes),
            EnValues = new double[nodes * nodes * nodes],
            MuValues = new double[nodes * nodes * nodes],
            BetaValues = new double[nodes * nodes * nodes]
        };
        var analytic = profile.Copy();
        analytic.Table = null;
        for (int i = 0; i < nodes; i++)
            for (int j = 0; j < nodes; j++)
                for (int k = 0; k < nodes; k++)
                {
                    BounceModel.EvaluateResponses(analytic, table.SnNodes[i], table.StNodes[j], table.SuNodes[k], out double en, out double mu, out double beta, out _);
                    int index = (i * nodes + j) * nodes + k;
                    table.EnValues[index] = en; table.MuValues[index] = mu; table.BetaValues[index] = beta;
                }
        table.Validate();
        return table;
    }

    private static double[] Axis(double max, int nodes)
    {
        var values = new double[nodes];
        for (int i = 0; i < nodes; i++) values[i] = max * i / (nodes - 1.0);
        return values;
    }

    public static TableComparison Compare(InteractionProfile analyticProfile, ProfileTable table, int probes, double snMax, double stMax, double suMax, BounceTolerances tolerances, BallSpec ball, double enTolerance, double muRelativeTolerance, double betaTolerance)
    {
        var comparison = new TableComparison { Probes = probes };
        var analytic = analyticProfile.Copy();
        analytic.Table = null;
        var tabled = analyticProfile.Copy();
        tabled.Table = table;
        for (int i = 0; i < probes; i++)
            for (int j = 0; j < probes; j++)
                for (int k = 0; k < probes; k++)
                {
                    double sn = snMax * i / (probes - 1.0), st = stMax * j / (probes - 1.0), su = suMax * k / (probes - 1.0);
                    BounceModel.EvaluateResponses(analytic, sn, st, su, out double enA, out double muA, out double betaA, out _);
                    table.Evaluate(sn, st, su, out double enT, out double muT, out double betaT, out bool clamped);
                    if (clamped) comparison.ClampedProbes++;
                    comparison.MaxEnDeviation = Math.Max(comparison.MaxEnDeviation, Math.Abs(enA - enT));
                    comparison.MaxMuRelativeDeviation = Math.Max(comparison.MaxMuRelativeDeviation, muA <= 1e-12 ? Math.Abs(muA - muT) : Math.Abs(muA - muT) / muA);
                    comparison.MaxBetaDeviation = Math.Max(comparison.MaxBetaDeviation, Math.Abs(betaA - betaT));
                    var pre = new ImpactState { PositionM = new Vec3(0, ball.RadiusM, 0), VelocityMS = new Vec3(0, -Math.Max(0.2, sn), Math.Max(0.2, st)), AngularVelocityRadS = new Vec3(Math.Max(0, su) / ball.RadiusM * 0.25, 0, 0), ImpactTimeS = 0 };
                    var sample = BounceModel.SampleAtContact(pre, ball, analyticProfile.SurfaceId, "table-probe");
                    var resolvedAnalytic = BounceModel.Resolve(pre, ball, null, sample, analytic, tolerances);
                    var resolvedTable = BounceModel.Resolve(pre, ball, null, sample, tabled, tolerances);
                    comparison.MaxVelocityDeviationMS = Math.Max(comparison.MaxVelocityDeviationMS, (resolvedAnalytic.PostState.VelocityMS - resolvedTable.PostState.VelocityMS).Length);
                    comparison.MaxAngularDeviationRadS = Math.Max(comparison.MaxAngularDeviationRadS, (resolvedAnalytic.PostState.AngularVelocityRadS - resolvedTable.PostState.AngularVelocityRadS).Length);
                }
        if (comparison.MaxEnDeviation > enTolerance) comparison.Violations.Add("en deviation " + comparison.MaxEnDeviation.ToString("R") + " > " + enTolerance);
        if (comparison.MaxMuRelativeDeviation > muRelativeTolerance) comparison.Violations.Add("mu relative deviation " + comparison.MaxMuRelativeDeviation.ToString("R") + " > " + muRelativeTolerance);
        if (comparison.MaxBetaDeviation > betaTolerance) comparison.Violations.Add("beta deviation " + comparison.MaxBetaDeviation.ToString("R") + " > " + betaTolerance);
        return comparison;
    }
}
