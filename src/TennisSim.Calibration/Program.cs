using System.Text.Json;
using TennisSim.Core.Bounce;

namespace TennisSim.Calibration;

public sealed class CandidateSummary
{
    public string Model { get; set; } = "";
    public int ParameterCount { get; set; }
    public double TrainLoss { get; set; }
    public double ValidationLoss { get; set; }
    public double TrainNormalisedRmse { get; set; }
    public double ValidationNormalisedRmse { get; set; }
    public int Evaluations { get; set; }
    public bool Selected { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class FitRun
{
    public string SchemaVersion { get; set; } = "1.0";
    public string RunId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Model { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string DatasetHash { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string ManifestHash { get; set; } = "";
    public string TrainSplitHash { get; set; } = "";
    public string ValidationSplitHash { get; set; } = "";
    public string TestSplitHash { get; set; } = "";
    public Dictionary<string, int> SplitCounts { get; set; } = new();
    public Dictionary<string, int> EvidenceCounts { get; set; } = new();
    public List<CandidateSummary> Candidates { get; set; } = new();
    public FitConfig Config { get; set; } = new();
    public Estimation Estimation { get; set; } = new();
    public ProfileDomain? Domain { get; set; }
    public EvaluationReport? ValidationReport { get; set; }
    public EvaluationReport? TestReport { get; set; }
    public string ProfileHash { get; set; } = "";
    public string ProfileRefusal { get; set; } = "";
    public List<string> Notes { get; set; } = new();
}

public static class ProfileExport
{
    public static InteractionProfile Build(Estimation estimation, FitRun run, EvidenceType evidence, ValidationStatus validation, ProfileRelease release, ProfileDomain? domain, string runId, string surfaceId)
    {
        var profile = estimation.Profile.Copy();
        profile.Id = "fit-" + estimation.Model + "-" + runId;
        profile.SurfaceId = surfaceId;
        profile.Evidence = evidence;
        profile.CalibrationStatus = evidence switch
        {
            EvidenceType.SYNTHETIC => CalibrationStatus.FITTED_SYNTHETIC_ONLY,
            _ => validation == ValidationStatus.PASS_IN_DOMAIN ? CalibrationStatus.CALIBRATED_IN_DOMAIN : CalibrationStatus.MODEL_INADEQUATE
        };
        profile.ValidationStatus = validation;
        profile.Release = release;
        profile.SourceDatasetHash = run.DatasetHash;
        profile.CalibrationRunId = runId;
        profile.Domain = domain;
        profile.SupportedConditions.Add("fitted domain: sn<=" + (domain?.SnMax?.ToString("F3") ?? "n/a") + " m/s, st<=" + (domain?.StMax?.ToString("F3") ?? "n/a") + " m/s, su<=" + (domain?.SuMax?.ToString("F3") ?? "n/a") + " m/s");
        profile.SupportedConditions.Add(evidence == EvidenceType.SYNTHETIC ? "SYNTHETIC evidence only: valid for software verification, never for gameplay calibration claims" : "measured evidence in the named ball-surface-state combination only");
        profile.Parameters = estimation.Fits.Select(f => new ParameterSummary { Parameter = f.Name, Status = f.Status, Value = f.Value, StandardError = f.StandardError, BootstrapLow = f.BootstrapLow, BootstrapHigh = f.BootstrapHigh, AtBoundary = f.AtBoundary }).ToList();
        profile.Notes = "Fit run " + runId + " (" + estimation.Model + "), dataset hash " + run.DatasetHash + ". Parameter status: " + string.Join(", ", estimation.Fits.Select(f => f.Name + "=" + f.Status)) + ".";
        profile.Validate();
        return profile;
    }

    public static void Save(string directory, InteractionProfile profile, BallSpec ball, FitRun run)
    {
        Directory.CreateDirectory(directory);
        CalibrationJson.Save(Path.Combine(directory, "profile.json"), profile);
        CalibrationJson.Save(Path.Combine(directory, "ball.json"), ball);
        CalibrationJson.Save(Path.Combine(directory, "run.json"), run);
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (DataInsufficientException ex) { Console.Error.WriteLine("DATA_INSUFFICIENT: " + ex.Message); return 3; }
        catch (ArgumentException ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 2; }
        catch (IOException ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 2; }
        catch (JsonException ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 2; }
    }

    private sealed class Args
    {
        private readonly Dictionary<string, string> values = new();
        private readonly HashSet<string> flags = new();
        public Args(string[] raw)
        {
            for (int i = 0; i < raw.Length; i++)
            {
                if (!raw[i].StartsWith("--")) throw new ArgumentException("Unknown argument: " + raw[i]);
                string key = raw[i][2..];
                if (i + 1 < raw.Length && !raw[i + 1].StartsWith("--")) values[key] = raw[++i];
                else flags.Add(key);
            }
        }
        public string Get(string key, string fallback = "") => values.GetValueOrDefault(key, fallback);
        public bool Has(string key) => flags.Contains(key) || values.ContainsKey(key);
        public int Int(string key, int fallback) => values.TryGetValue(key, out var v) ? int.Parse(v) : fallback;
        public double Double(string key, double fallback) => values.TryGetValue(key, out var v) ? double.Parse(v) : fallback;
        public uint UInt(string key, uint fallback) => values.TryGetValue(key, out var v) ? uint.Parse(v) : fallback;
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help")
        {
            Console.WriteLine("tennis-calibrate valid-data|synthetic|fit|evaluate|virtual-itf|export-profile|compare-runtime");
            Console.WriteLine("validate-data --dataset data/bounce/manifest.json [--out report.json]");
            Console.WriteLine("synthetic --out data/bounce/synthetic [--count 240 --seed 20260915 --en 0.83 --mu 0.35 --beta 0]");
            Console.WriteLine("fit --dataset <manifest.json> --config calibration/fit-config.json --out artifacts/calibration/run-001 [--models M1,M2 --synthetic]");
            Console.WriteLine("evaluate --run <run directory> --split test");
            Console.WriteLine("virtual-itf --profile <profile.json> [--ball <ball.json> --temperature-c 23 --spin-rev-per-s 0]");
            Console.WriteLine("export-profile --run <run directory> --out <profile.json> [--table-nodes 5]");
            Console.WriteLine("compare-runtime --profile <profile.json> [--table-nodes 5 --probes 9 --fixtures <dir>]");
            Console.WriteLine("exit codes: 0 ok, 1 target or gate failure, 2 input error, 3 data insufficient / no releasable profile, 4 comparison tolerance exceeded");
            return 0;
        }
        var options = new Args(args.Skip(1).ToArray());
        var tolerances = new BounceTolerances();
        switch (args[0])
        {
            case "validate-data": return ValidateData(options);
            case "synthetic": return SyntheticCommand(options);
            case "fit": return FitCommand(options, tolerances);
            case "evaluate": return EvaluateCommand(options, tolerances);
            case "virtual-itf": return VirtualItfCommand(options, tolerances);
            case "export-profile": return ExportProfile(options, tolerances);
            case "compare-runtime": return CompareRuntime(options, tolerances);
            default: throw new ArgumentException("Unknown command " + args[0]);
        }
    }

    private static int ValidateData(Args options)
    {
        var dataset = BounceDataset.Load(options.Get("dataset", "data/bounce/manifest.json"));
        var report = new
        {
            dataset.ManifestPath,
            dataset.DatasetHash,
            dataset.ManifestHash,
            splitRule = dataset.Manifest.SplitRule,
            targetPopulation = dataset.Manifest.TargetPopulation,
            total = dataset.Records.Count,
            measured = dataset.MeasuredRecords,
            synthetic = dataset.SyntheticRecords,
            evidenceCounts = dataset.EvidenceCounts,
            recordsPerDataset = dataset.RecordsPerDataset,
            splits = new[] { "train", "validation", "test" }.ToDictionary(s => s, s => dataset.Split(s).Count()),
            groups = dataset.Splits.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { group = p.Key, split = p.Value }).ToList(),
            empiricalData = dataset.MeasuredRecords == 0 ? "MISSING" : "PRESENT",
            unconstrained = dataset.Records.Count(r => !r.UsableForTangential) + " records without measured incident spin (normal response only)"
        };
        if (options.Has("out")) CalibrationJson.Save(options.Get("out"), report);
        Console.WriteLine(CalibrationJson.Serialize(report));
        Console.WriteLine("EMPIRICAL_DATA: " + (dataset.MeasuredRecords == 0 ? "MISSING" : "SUFFICIENT"));
        return 0;
    }

    private static int SyntheticCommand(Args options)
    {
        string directory = options.Get("out", "data/bounce/synthetic");
        var spec = new SyntheticSpec
        {
            Count = options.Int("count", 240),
            Seed = options.UInt("seed", 20260915),
            Sessions = options.Int("sessions", 12),
            TruthEn = options.Double("en", 0.83),
            TruthMu = options.Double("mu", 0.35),
            TruthBeta = options.Double("beta", 0.0),
            VelocityNoiseMS = options.Double("velocity-noise", 0.25),
            AngularNoiseRadS = options.Double("angular-noise", 15),
            SpinMeasuredFraction = options.Double("spin-measured", 0.7),
            SpinAfterObservedFraction = options.Double("spin-after-observed", 0.6)
        };
        Directory.CreateDirectory(directory);
        var records = Synthetic.Generate(spec);
        string recordFile = "records.jsonl";
        File.WriteAllLines(Path.Combine(directory, recordFile), records.Select(CalibrationJson.SerializeCompact));
        var generation = new
        {
            generator = "TennisSim.Calibration Synthetic.Generate",
            evidenceType = "SYNTHETIC",
            recordFile,
            recordCount = records.Count,
            recordFileHash = CalibrationJson.HashFile(Path.Combine(directory, recordFile)),
            spec,
            note = "SYNTHETIC records produced from a published truth vector. They verify software behaviour and must never be reported as measurements."
        };
        CalibrationJson.Save(Path.Combine(directory, "generation.json"), generation);
        Console.WriteLine("SYNTHETIC_DATASET dir=" + Path.GetFullPath(directory) + " records=" + records.Count + " truthEn=" + spec.TruthEn + " truthMu=" + spec.TruthMu + " truthBeta=" + spec.TruthBeta);
        return 0;
    }

    private static FitConfig LoadConfig(string path)
    {
        if (File.Exists(path)) return CalibrationJson.Load<FitConfig>(path);
        return new FitConfig();
    }

    private static int FitCommand(Args options, BounceTolerances tolerances)
    {
        var dataset = BounceDataset.Load(options.Get("dataset", "data/bounce/manifest.json"));
        var config = LoadConfig(options.Get("config", "calibration/fit-config.json"));
        string runId = options.Get("run-id", Path.GetFileName(options.Get("out", "run-001")));
        string output = options.Get("out", "artifacts/calibration/" + runId);
        string models = options.Get("models", config.Model);
        bool syntheticRequested = options.Has("synthetic");
        bool measured = dataset.MeasuredRecords > 0;
        var run = new FitRun
        {
            RunId = runId,
            Model = models,
            DatasetHash = dataset.DatasetHash,
            ManifestPath = dataset.ManifestPath,
            ManifestHash = dataset.ManifestHash,
            EvidenceCounts = dataset.EvidenceCounts,
            Config = config
        };
        foreach (string split in new[] { "train", "validation", "test" })
            run.SplitCounts[split] = dataset.Split(split).Count();
        run.TrainSplitHash = CalibrationJson.Hash(string.Join(",", dataset.Split("train").Select(r => r.RecordId)));
        run.ValidationSplitHash = CalibrationJson.Hash(string.Join(",", dataset.Split("validation").Select(r => r.RecordId)));
        run.TestSplitHash = CalibrationJson.Hash(string.Join(",", dataset.Split("test").Select(r => r.RecordId)));
        EvidenceType evidence = dataset.SyntheticRecords > 0 && !measured ? EvidenceType.SYNTHETIC
            : dataset.Records.Where(r => r.EvidenceType is EvidenceType.MEASURED_RAW or EvidenceType.PUBLISHED_MEASUREMENT or EvidenceType.DIGITIZED_MEASUREMENT)
                .GroupBy(r => r.EvidenceType).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(EvidenceType.SYNTHETIC);
        run.Evidence = evidence.ToString();
        if (!measured && !syntheticRequested)
        {
            run.Status = "REFUSED_DATA_INSUFFICIENT";
            run.ProfileRefusal = "EMPIRICAL_DATA:MISSING. No record with measured evidence exists in " + dataset.ManifestPath + "; a calibration profile is not exported and no coefficient was invented.";
            run.Notes.Add("Run --synthetic to exercise the pipeline on the labelled synthetic dataset instead.");
            if (options.Has("out")) CalibrationJson.Save(Path.Combine(output, "run.json"), run);
            Console.Error.WriteLine("REFUSED: " + run.ProfileRefusal);
            return 3;
        }
        bool estimateBeta = dataset.Split(config.TrainSplit).Any(r => r.UsableForAngular);
        var candidates = new List<CandidateSummary>();
        Estimation? selected = null;
        CandidateSummary? selectedSummary = null;
        foreach (string model in models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var estimation = Fit.Run(model, dataset, config, tolerances, estimateBeta);
            var summary = new CandidateSummary
            {
                Model = model,
                ParameterCount = estimation.Parameters.Length,
                TrainLoss = estimation.TrainLoss,
                ValidationLoss = estimation.ValidationLoss,
                TrainNormalisedRmse = estimation.TrainNormalisedRmse,
                ValidationNormalisedRmse = estimation.ValidationNormalisedRmse,
                Evaluations = estimation.Evaluations,
                Warnings = estimation.Warnings.Distinct().ToList()
            };
            // Complexity gate: a richer model must improve the validation residual by more than the
            // pre-registered relative gain, not merely the training residual.
            bool better = selected == null || summary.ValidationNormalisedRmse < selectedSummary!.ValidationNormalisedRmse;
            bool allowed = selected == null || summary.ParameterCount <= selectedSummary!.ParameterCount
                || summary.ValidationNormalisedRmse < selectedSummary.ValidationNormalisedRmse * (1 - config.ValidationRmseGainForComplexity);
            if (better && allowed) { selected = estimation; selectedSummary = summary; }
            candidates.Add(summary);
        }
        if (selected == null) throw new DataInsufficientException("No model could be fitted");
        selectedSummary!.Selected = true;
        run.Candidates = candidates;
        run.Estimation = selected;
        run.Model = selected.Model;
        run.Domain = Domain(selected, dataset, config);
        var validationReport = Metrics.Evaluate(selected.Profile, dataset.Split(config.ValidationSplit), config.ValidationSplit, config, tolerances, gateTargets: measured);
        var testReport = Metrics.Evaluate(selected.Profile, dataset.Split(config.TestSplit), config.TestSplit, config, tolerances, gateTargets: measured);
        run.ValidationReport = validationReport;
        run.TestReport = testReport;
        bool validationPassed = validationReport.TargetViolations.Count == 0 && selected.Fits.All(f => f.Status is "IDENTIFIED" or "FIXED" || f.Status == "LOWER_BOUND_ONLY");
        // Synthetic holdout performance is reported the same way but never becomes a calibration claim.
        var validationStatus = validationPassed ? ValidationStatus.PASS_IN_DOMAIN : ValidationStatus.FAIL;
        var release = evidence == EvidenceType.SYNTHETIC ? ProfileRelease.DEV_ONLY : options.Has("approve") && validationPassed ? ProfileRelease.APPROVED : ProfileRelease.BLOCKED;
        string surfaceId = dataset.Manifest.Datasets.FirstOrDefault()?.SurfaceId ?? "unspecified";
        var profile = ProfileExport.Build(selected, run, evidence, validationStatus, release, run.Domain, runId, surfaceId);
        run.ProfileHash = ProfileHash.Compute(profile);
        run.Status = evidence == EvidenceType.SYNTHETIC ? "COMPLETE_SYNTHETIC_ONLY" : validationPassed ? "COMPLETE_MEASURED" : "COMPLETE_VALIDATION_FAILED";
        if (evidence != EvidenceType.SYNTHETIC && release == ProfileRelease.BLOCKED)
            run.ProfileRefusal = "Validation did not pass the pre-registered targets or --approve was not supplied; release stays BLOCKED.";
        ProfileExport.Save(output, profile, BounceProfiles.NominalType2(), run);
        Console.WriteLine("FIT model=" + profile.ModelId + "/" + selected.Model + " status=" + run.Status + " evidence=" + evidence + " release=" + release);
        Console.WriteLine("  parameters: " + string.Join(", ", selected.Fits.Select(f => f.Name + "=" + f.Value.ToString("R") + " [" + f.Status + "]")));
        Console.WriteLine("  train normalised RMSE=" + selected.TrainNormalisedRmse.ToString("F5") + " validation normalised RMSE=" + selected.ValidationNormalisedRmse.ToString("F5"));
        Console.WriteLine("  validation: normal=" + Fmt(validationReport.NormalSpeedRmseMS) + " m/s tangential=" + Fmt(validationReport.TangentialSpeedRmseMS) + " m/s angular=" + Fmt(validationReport.AngularSpeedRmseRpm) + " rpm angle=" + Fmt(validationReport.ExitAngleRmseDeg) + " deg");
        Console.WriteLine("  test:       normal=" + Fmt(testReport.NormalSpeedRmseMS) + " m/s tangential=" + Fmt(testReport.TangentialSpeedRmseMS) + " m/s angular=" + Fmt(testReport.AngularSpeedRmseRpm) + " rpm angle=" + Fmt(testReport.ExitAngleRmseDeg) + " deg");
        Console.WriteLine("  limit modes: " + string.Join(", ", selected.LimitModes.Select(p => p.Key + "=" + p.Value)));
        foreach (string warning in selected.Warnings.Distinct()) Console.WriteLine("  WARNING: " + warning);
        Console.WriteLine("  profile=" + Path.GetFullPath(Path.Combine(output, "profile.json")) + " hash=" + run.ProfileHash);
        return options.Has("strict") && validationReport.TargetViolations.Count > 0 ? 1 : 0;
    }

    private static string Fmt(double? value) => value.HasValue ? value.Value.ToString("F5") : "n/a";

    private static ProfileDomain Domain(Estimation estimation, BounceDataset dataset, FitConfig config)
    {
        var records = dataset.Split(config.TrainSplit).Where(r => r.UsableForTangential).ToList();
        double snMax = 0, stMax = 0, suMax = 0;
        var ball = BounceProfiles.NominalType2();
        foreach (var record in records)
        {
            var geometry = BounceModel.Geometry(Fit.PreState(record), record.NormalVector, ball.RadiusM);
            snMax = Math.Max(snMax, geometry.Sn); stMax = Math.Max(stMax, geometry.St); suMax = Math.Max(suMax, geometry.Su);
        }
        return new ProfileDomain { SnMin = 0, SnMax = Math.Max(snMax, 1e-6), StMin = 0, StMax = Math.Max(stMax, 1e-6), SuMin = 0, SuMax = Math.Max(suMax, 1e-6) };
    }

    private static FitRun LoadRun(string directory) => CalibrationJson.Load<FitRun>(Path.Combine(directory, "run.json"));

    private static int EvaluateCommand(Args options, BounceTolerances tolerances)
    {
        string directory = options.Get("run", "");
        if (directory.Length == 0) throw new ArgumentException("--run directory is required");
        var run = LoadRun(directory);
        var dataset = BounceDataset.Load(run.ManifestPath);
        if (dataset.DatasetHash != run.DatasetHash) throw new ArgumentException("Dataset hash changed since the fit run; refusing to evaluate against a different dataset");
        var profile = CalibrationJson.Load<InteractionProfile>(Path.Combine(directory, "profile.json"));
        string split = options.Get("split", "test");
        bool measured = string.Equals(run.Evidence, nameof(EvidenceType.SYNTHETIC), StringComparison.Ordinal) == false;
        var report = Metrics.Evaluate(profile, dataset.Split(split), split, run.Config, tolerances, gateTargets: measured);
        CalibrationJson.Save(Path.Combine(directory, "evaluation-" + split + ".json"), report);
        Console.WriteLine("EVALUATE model=" + run.Model + " split=" + split + " records=" + report.Records + " resolved=" + report.Resolved + " angularObserved=" + report.AngularObserved);
        Console.WriteLine("  normal RMSE=" + Fmt(report.NormalSpeedRmseMS) + " m/s bias=" + Fmt(report.NormalSpeedBiasMS) + " p95=" + Fmt(report.NormalSpeedP95MS));
        Console.WriteLine("  tangential RMSE=" + Fmt(report.TangentialSpeedRmseMS) + " m/s p95=" + Fmt(report.TangentialSpeedP95MS));
        Console.WriteLine("  angular RMSE=" + Fmt(report.AngularSpeedRmseRpm) + " rpm p95=" + Fmt(report.AngularSpeedP95Rpm));
        Console.WriteLine("  exit angle RMSE=" + Fmt(report.ExitAngleRmseDeg) + " deg p95=" + Fmt(report.ExitAngleP95Deg) + " excludedBelowFloor=" + report.AngleExcludedBelowFloor);
        foreach (var pair in report.BySurface) Console.WriteLine("  surface " + pair.Key + ": n=" + pair.Value.Count + " normal=" + Fmt(pair.Value.NormalSpeedRmseMS) + " tangential=" + Fmt(pair.Value.TangentialSpeedRmseMS) + " angle=" + Fmt(pair.Value.ExitAngleRmseDeg));
        foreach (string violation in report.TargetViolations) Console.WriteLine("  TARGET_VIOLATION: " + violation);
        foreach (string note in report.Notes) Console.WriteLine("  note: " + note);
        return report.TargetViolations.Count > 0 ? 1 : 0;
    }

    private static int VirtualItfCommand(Args options, BounceTolerances tolerances)
    {
        string path = options.Get("profile", "");
        if (path.Length == 0) throw new ArgumentException("--profile is required");
        var profile = CalibrationJson.Load<InteractionProfile>(path);
        var ball = options.Has("ball") ? CalibrationJson.Load<BallSpec>(options.Get("ball")) : BounceProfiles.NominalType2();
        var result = VirtualItf.Compute(profile, ball, tolerances, options.Double("temperature-c", 23), options.Double("spin-rev-per-s", 0));
        if (options.Has("out")) CalibrationJson.Save(options.Get("out"), result);
        Console.WriteLine(CalibrationJson.Serialize(result));
        Console.WriteLine("CPR_RAW=" + result.CprRaw.ToString("R") + " eTest=" + result.ETest.ToString("R") + " muTest=" + result.MuTest.ToString("R"));
        Console.WriteLine("NOT_AN_ITF_CERTIFICATION: the official CS 01/02 procedure is not reproduced.");
        return profile.Evidence == EvidenceType.SYNTHETIC ? 1 : 0;
    }

    private static int ExportProfile(Args options, BounceTolerances tolerances)
    {
        if (options.Has("built-in"))
        {
            var builtIn = BounceProfiles.DesignUncalibrated();
            string target = options.Get("out", "profiles/design-unc-v1.json");
            CalibrationJson.Save(target, builtIn);
            CalibrationJson.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".", "ball-type2-nominal.json"), BounceProfiles.NominalType2());
            Console.WriteLine("EXPORT_BUILT_IN profile=" + Path.GetFullPath(target) + " hash=" + ProfileHash.Compute(builtIn) + " evidence=" + builtIn.Evidence + " calibrationStatus=" + builtIn.CalibrationStatus + " validationStatus=" + builtIn.ValidationStatus + " release=" + builtIn.Release);
            Console.WriteLine("RELEASE_NOT_APPROVED: the built-in profile is an uncalibrated design assumption.");
            return 0;
        }
        string directory = options.Get("run", "");
        if (directory.Length == 0) throw new ArgumentException("--run directory is required");
        var run = LoadRun(directory);
        if (run.ProfileRefusal.Length > 0 && run.Status == "REFUSED_DATA_INSUFFICIENT")
            throw new DataInsufficientException(run.ProfileRefusal);
        var profile = CalibrationJson.Load<InteractionProfile>(Path.Combine(directory, "profile.json"));
        int tableNodes = options.Int("table-nodes", 0);
        if (tableNodes >= 2)
        {
            var domain = profile.Domain ?? new ProfileDomain { SnMin = 0, SnMax = 15, StMin = 0, StMax = 35, SuMin = 0, SuMax = 35 };
            profile.Table = Tables.Build(profile, tableNodes, domain.SnMax ?? 15, domain.StMax ?? 35, domain.SuMax ?? 35);
        }
        profile.Validate();
        string output = options.Get("out", Path.Combine(directory, "profile-export.json"));
        CalibrationJson.Save(output, profile);
        Console.WriteLine("EXPORT profile=" + Path.GetFullPath(output) + " hash=" + ProfileHash.Compute(profile) + " evidence=" + profile.Evidence + " calibrationStatus=" + profile.CalibrationStatus + " validationStatus=" + profile.ValidationStatus + " release=" + profile.Release + (profile.Table == null ? " table=none" : " table=" + profile.Table.CellCount + " cells"));
        if (profile.Release != ProfileRelease.APPROVED) Console.WriteLine("RELEASE_NOT_APPROVED: this profile is not promoted for product use.");
        return 0;
    }

    private static int CompareRuntime(Args options, BounceTolerances tolerances)
    {
        string path = options.Get("profile", "");
        if (path.Length == 0) throw new ArgumentException("--profile is required");
        var analytic = CalibrationJson.Load<InteractionProfile>(path);
        var ball = options.Has("ball") ? CalibrationJson.Load<BallSpec>(options.Get("ball")) : BounceProfiles.NominalType2();
        if (analytic.BallSpecId.Length > 0 && analytic.BallSpecId != ball.Id) throw new ArgumentException("Profile is bound to ball spec " + analytic.BallSpecId + "; pass --ball");
        int nodes = options.Int("table-nodes", 5);
        int probes = options.Int("probes", 9);
        var domain = analytic.Domain ?? new ProfileDomain { SnMin = 0, SnMax = 20, StMin = 0, StMax = 40, SuMin = 0, SuMax = 40 };
        double snMax = domain.SnMax ?? 20, stMax = domain.StMax ?? 40, suMax = domain.SuMax ?? 40;
        var table = Tables.Build(analytic, nodes, snMax, stMax, suMax);
        var comparison = Tables.Compare(analytic, table, probes, snMax, stMax, suMax, tolerances, ball, options.Double("en-tolerance", 0.02), options.Double("mu-relative-tolerance", 0.05), options.Double("beta-tolerance", 0.02));
        if (options.Has("out")) CalibrationJson.Save(options.Get("out"), comparison);
        Console.WriteLine(CalibrationJson.Serialize(comparison));
        Console.WriteLine("COMPARE_RUNTIME probes=" + comparison.Probes + " maxEnDev=" + comparison.MaxEnDeviation.ToString("R") + " maxMuRelDev=" + comparison.MaxMuRelativeDeviation.ToString("R") + " maxBetaDev=" + comparison.MaxBetaDeviation.ToString("R") + " maxVelocityDev=" + comparison.MaxVelocityDeviationMS.ToString("R"));
        Console.WriteLine("Table evaluation is a lossy runtime representation; matching the analytic model is NOT bitwise reproducibility.");
        return comparison.Violations.Count > 0 ? 4 : 0;
    }
}
