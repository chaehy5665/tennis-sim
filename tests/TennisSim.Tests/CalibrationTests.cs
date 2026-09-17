using TennisSim.Core;
using TennisSim.Core.Bounce;
using TennisSim.Calibration;

namespace TennisSim.Tests;

// Calibration pipeline tests. All fitting here uses the labelled SYNTHETIC dataset or generated data.
public static class CalibrationTests
{
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Near(double a, double b, double eps, string message = "") => Check(Math.Abs(a - b) <= eps, message + " " + a + " != " + b);

    private static BounceDataset LoadRepositoryDataset()
    {
        string manifest = Path.Combine(Root(), "data", "bounce", "manifest.json");
        Check(File.Exists(manifest), "Missing dataset manifest at " + manifest);
        return BounceDataset.Load(manifest);
    }

    // The test runner may be started from the repository root or from the project directory.
    private static string Root()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "TennisSim.sln"))) directory = directory.Parent;
        return directory == null ? Directory.GetCurrentDirectory() : directory.FullName;
    }

    public static void Run(Action<string, Action> test)
    {
        var tolerances = new BounceTolerances();

        test("Calibration: synthetic dataset stays synthetic and measured records stay published", () =>
        {
            var dataset = LoadRepositoryDataset();
            Check(dataset.Records.Count > 0, "Expected the labelled synthetic dataset");
            var synthetic = BounceDataset.Load(Path.Combine(Root(), "data", "bounce", "manifest.json"), new[] { "synthetic-type2-flat" });
            Check(synthetic.SyntheticRecords == synthetic.Records.Count && synthetic.MeasuredRecords == 0, "The synthetic dataset must never contain measured evidence");
            Check(dataset.MeasuredRecords == 7, "Only the published Cross 2002 records count as measured");
            Check(dataset.Records.Where(r => r.EvidenceType != EvidenceType.SYNTHETIC).All(r => r.SourceId == "C2002"), "Measured records must come from the registered published source");
            Check(dataset.Splits.Values.Distinct().Count() >= 2, "Splits must be assigned per group");
            Check(dataset.Split("test").All(r => dataset.SplitOf(r) == "test"));
            Check(dataset.Split("train").Count() > dataset.Split("test").Count());
            foreach (var record in synthetic.Records) Check(record.EvidenceType == EvidenceType.SYNTHETIC, "Evidence relabelled");
            Check(synthetic.Records.Count(r => !r.Observed.AngularVelocityBeforeMeasured) > 0, "Expected synthetic records whose incident spin is unmeasured");
            Check(synthetic.Records.Any(r => !r.Observed.PositionMeasured) == false, "Synthetic records carry a measured contact position");
        });

        test("Calibration: shipped design profile matches the built-in one", () =>
        {
            string path = Path.Combine(Root(), "profiles", "design-unc-v1.json");
            Check(File.Exists(path), "Missing shipped profile at " + path);
            var shipped = CalibrationJson.Deserialize<InteractionProfile>(File.ReadAllText(path));
            var builtIn = BounceProfiles.DesignUncalibrated();
            Check(ProfileHash.Compute(shipped) == ProfileHash.Compute(builtIn), "Shipped design profile drifted from the built-in profile");
            Check(shipped.Release == ProfileRelease.DEV_ONLY && shipped.CalibrationStatus == CalibrationStatus.UNCALIBRATED && shipped.Evidence == EvidenceType.ASSUMED_PRIOR, "Shipped design profile must stay UNCALIBRATED and DEV_ONLY");
            string ballPath = Path.Combine(Root(), "profiles", "ball-type2-nominal.json");
            Check(File.Exists(ballPath), "Missing shipped ball spec at " + ballPath);
            var ball = CalibrationJson.Deserialize<BallSpec>(File.ReadAllText(ballPath));
            Check(ball.Id == builtIn.BallSpecId && ball.Provenance == EvidenceType.ASSUMED_PRIOR, "Shipped ball spec must match the profile binding and stay ASSUMED_PRIOR");
            // The shipped profile must load through the runtime CLI contract and resolve an impact.
            var environment = new SurfaceEnvironment { Model = BounceModelKind.ImpulseV1, Profile = shipped, Ball = ball };
            environment.Validate();
            var pre = new ImpactState { PositionM = new Vec3(0, ball.RadiusM, 0), VelocityMS = new Vec3(0, -8, 20), ImpactTimeS = 0 };
            var result = BounceModel.Resolve(pre, ball, null, BounceModel.SampleAtContact(pre, ball, shipped.SurfaceId, "test"), shipped, new BounceTolerances());
            Check(result.Status == BounceStatus.RESOLVED && result.ProfileHash == ProfileHash.Compute(shipped));
        });

        test("Calibration: dataset loader rejects malformed records", () =>
        {
            string directory = Path.Combine(Root(), "artifacts", "tests", "dataset-reject");
            Directory.CreateDirectory(directory);
            string manifestPath = Path.Combine(directory, "manifest.json");
            File.WriteAllText(manifestPath, Synthetic.ManifestJson(new SyntheticSpec { Count = 4, Sessions = 4, DatasetId = "reject" }, "records.jsonl", "unit test: rejection cases"));
            var records = Synthetic.Generate(new SyntheticSpec { Count = 4, Sessions = 4, DatasetId = "reject" });
            void Rejects(Action write, string what)
            {
                write();
                File.WriteAllLines(Path.Combine(directory, "records.jsonl"), records.Select(CalibrationJson.SerializeCompact));
                bool rejected = false;
                try { BounceDataset.Load(manifestPath); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Expected rejection: " + what);
            }
            Rejects(() => records[0].SessionId = "", "missing session id");
            records = Synthetic.Generate(new SyntheticSpec { Count = 4, Sessions = 4, DatasetId = "reject" });
            Rejects(() => records[1].Normal = new[] { 0.2, 0.9, 0.0 }, "unnormalized normal");
            records = Synthetic.Generate(new SyntheticSpec { Count = 4, Sessions = 4, DatasetId = "reject" });
            var unmeasured = records.First(r => !r.Observed.AngularVelocityBeforeMeasured);
            Rejects(() => unmeasured.AngularVelocityBeforeRadS = new[] { 100.0, 0.0, 0.0 }, "unmeasured spin reported as a value");
            records = Synthetic.Generate(new SyntheticSpec { Count = 4, Sessions = 4, DatasetId = "reject" });
            Rejects(() => records[3].EvidenceType = EvidenceType.PUBLISHED_MEASUREMENT, "evidence class relabelled");
            records = Synthetic.Generate(new SyntheticSpec { Count = 4, Sessions = 4, DatasetId = "reject" });
            Rejects(() => records[0].SourceId = "UNKNOWN-SOURCE", "unknown source id");
            // JSON cannot carry NaN or Infinity under the record contract, so that check lives in Validate().
            records = Synthetic.Generate(new SyntheticSpec { Count = 4, Sessions = 4, DatasetId = "reject" });
            records[0].VelocityAfterMS = new[] { 1.0, double.NaN, 3.0 };
            bool nonFiniteRejected = false;
            try { records[0].Validate(); } catch (ArgumentException) { nonFiniteRejected = true; }
            Check(nonFiniteRejected, "Non-finite observation must be rejected by the record contract");
            File.WriteAllText(Path.Combine(directory, "records.jsonl"), "{ \"recordId\": \"broken\"");
            bool malformedRejected = false;
            try { BounceDataset.Load(manifestPath); } catch (ArgumentException) { malformedRejected = true; }
            Check(malformedRejected, "Truncated JSON must be rejected by the loader");
        });

        test("Calibration: fit refuses an empty dataset instead of inventing coefficients", () =>
        {
            var dataset = LoadRepositoryDataset();
            var empty = Synthetic.Generate(new SyntheticSpec { Count = 0 });
            Check(empty.Count == 0);
            var config = new FitConfig { BootstrapResamples = 0, Model = "M1" };
            bool refused = false;
            try { Fit.Run("M1", DatasetWithoutRecords(dataset), config, tolerances, estimateBeta: false); }
            catch (DataInsufficientException) { refused = true; }
            Check(refused, "Fitting without records must raise DataInsufficientException");
        });

        test("Calibration: synthetic fit recovers the published truth", () =>
        {
            var spec = new SyntheticSpec { Count = 96, Sessions = 8, Seed = 4242, TruthEn = 0.86, TruthMu = 0.42, TruthBeta = 0 };
            var dataset = DatasetFromRecords(Synthetic.Generate(spec), spec);
            var config = new FitConfig { Model = "M1", BootstrapResamples = 0, InitialStep = 0.1, StopStep = 1e-6 };
            var estimation = Fit.Run("M1", dataset, config, tolerances, estimateBeta: false);
            Check(estimation.TrainNormalisedRmse > 0);
            Near(estimation.Parameters[0], spec.TruthEn, 0.02, "en recovery");
            Near(estimation.Parameters[1], spec.TruthMu, 0.03, "mu recovery");
            Check(estimation.Fits[0].Status == "IDENTIFIED" && estimation.Fits[1].Status == "IDENTIFIED", "Parameter identification must bracket both contact modes");
            Check(estimation.LimitModes.ContainsKey(nameof(ActiveImpulseLimit.COULOMB_LIMITED)) && estimation.LimitModes.ContainsKey(nameof(ActiveImpulseLimit.TANGENTIAL_TARGET_LIMITED)));
            var validation = Metrics.Evaluate(estimation.Profile, dataset.Split("validation"), "validation", config, tolerances, gateTargets: false);
            Check(validation.NormalSpeedRmseMS < 0.5 && validation.TangentialSpeedRmseMS < 1.0, "Synthetic holdout must meet the pre-registered targets");
            Check(validation.TargetsGated == false && validation.Notes.Any(n => n.Contains("synthetic")), "Synthetic targets must not be gated");
        });

        test("Calibration: synthetic profiles never become calibration claims", () =>
        {
            var spec = new SyntheticSpec { Count = 48, Sessions = 6, Seed = 77, TruthEn = 0.8, TruthMu = 0.3 };
            var dataset = DatasetFromRecords(Synthetic.Generate(spec), spec);
            var config = new FitConfig { Model = "M1", BootstrapResamples = 0 };
            var estimation = Fit.Run("M1", dataset, config, tolerances, estimateBeta: false);
            var run = new FitRun { RunId = "unit", DatasetHash = dataset.DatasetHash, Model = "M1" };
            var profile = ProfileExport.Build(estimation, run, EvidenceType.SYNTHETIC, ValidationStatus.PASS_IN_DOMAIN, ProfileRelease.DEV_ONLY, new ProfileDomain { SnMin = 0, SnMax = 20, StMin = 0, StMax = 40, SuMin = 0, SuMax = 40 }, "unit", spec.SurfaceId);
            Check(profile.CalibrationStatus == CalibrationStatus.FITTED_SYNTHETIC_ONLY, "Synthetic evidence must not become CALIBRATED_IN_DOMAIN");
            Check(profile.Release == ProfileRelease.DEV_ONLY && profile.Evidence == EvidenceType.SYNTHETIC);
            Check(profile.Domain != null && profile.Domain.SnMax > 0);
            Check(profile.Parameters.Count >= 2, "Exported profile must carry parameter summaries");
            Check(profile.SupportedConditions.Any(c => c.Contains("SYNTHETIC")));
            Check(ProfileHash.Compute(profile) == ProfileHash.Compute(CalibrationJson.Deserialize<InteractionProfile>(CalibrationJson.Serialize(profile))), "Exported profile must survive JSON round trip");
        });

        test("Calibration: CPR shaped virtual test is computed from the bounce result", () =>
        {
            var ball = BounceProfiles.NominalType2();
            var fast = Fit.BuildProfile("M1", new[] { 0.9, 0.1 }, 10, ball.Id, "s1");
            var slow = Fit.BuildProfile("M1", new[] { 0.6, 0.6 }, 10, ball.Id, "s2");
            var fastResult = VirtualItf.Compute(fast, ball, tolerances, 23, 3);
            var slowResult = VirtualItf.Compute(slow, ball, tolerances, 23, 3);
            Check(fastResult.CprRaw > slowResult.CprRaw, "A livelier surface must give a higher raw CPR");
            double expectedCpr = 100 * (1 - fastResult.MuTest) + 150 * (0.81 - fastResult.E23);
            Near(fastResult.CprRaw, expectedCpr, 1e-9, "CPR formula");
            Near(fastResult.ETest, fastResult.NormalExitSpeedMS / fastResult.NormalIncidenceSpeedMS, 1e-12, "e_test definition");
            Near(fastResult.IncidenceSpeedMS, 30.0, 0, "CS 01/02 incidence speed");
            Near(fastResult.IncidenceAngleDeg, 16.0, 0, "CS 01/02 incidence angle");
            var temperature = VirtualItf.Compute(fast, ball, tolerances, 30, 3);
            Check(temperature.E23 < fastResult.E23, "e_23 temperature correction direction");
        });

        test("Calibration: published bounce measurements load as measured evidence", () =>
        {
            var dataset = BounceDataset.Load(Path.Combine(Root(), "data", "bounce", "manifest.json"), new[] { "cross2002-tennis-ball-surfaces" });
            Check(dataset.Records.Count == 7, "expected 7 published records");
            Check(dataset.MeasuredRecords == 7 && dataset.SyntheticRecords == 0, "published records must stay measured evidence");
            Check(dataset.Records.All(r => r.EvidenceType == EvidenceType.PUBLISHED_MEASUREMENT));
            Check(dataset.Records.All(r => r.UsableForTangential && r.UsableForAngular), "zero incident spin is an experimental condition, so the tangential response is usable");
            Check(dataset.Records.All(r => !r.Observed.PositionMeasured) && dataset.Records.All(r => r.Observed.NormalMeasured), "published flat-contact records state the normal, not a court position");
            Check(dataset.Records.All(r => r.Observed.AngularVelocityBeforeMeasured), "incident spin is zero by construction and declared as such");
            Check(dataset.Split("train").Count() == 2 && dataset.Split("validation").Count() == 1 && dataset.Split("test").Count() == 4, "split assignment must stay fixed");
            Check(dataset.Split("test").All(r => r.SurfaceId != dataset.Split("train").First().SurfaceId), "held-out records are different surfaces, never merged into one profile");
            string notePath = Path.Combine(Root(), "data", "bounce", "published", "cross2002-extraction.json");
            Check(File.Exists(notePath), "missing extraction note");
            var note = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(notePath))!;
            Check(note["recordCount"]!.GetValue<int>() == 7, "extraction note record count");
            Check(note["checks"]!.AsArray().Count == 7, "every record must carry a derived-column check");
            Check(Published().Count == 7);
        });

        test("Calibration: published fit returns a measured profile with bounded friction", () =>
        {
            var dataset = BounceDataset.Load(Path.Combine(Root(), "data", "bounce", "manifest.json"), new[] { "cross2002-tennis-ball-surfaces" });
            var config = new FitConfig { Model = "M1B", BootstrapResamples = 0, InitialStep = 0.1, StopStep = 1e-7 };
            var estimation = Fit.Run("M1B", dataset, config, tolerances, estimateBeta: true);
            Near(estimation.Parameters[0], 0.806, 0.02, "en on emery");
            Check(estimation.Parameters[2] > 0, "a tangential restitution term must be able to move away from zero on this data");
            Check(estimation.Fits.Any(f => f.Name == "mu_eff" && f.Status == "UPPER_BOUND_ONLY"), "friction never saturates in these bounces, so mu is only upper bounded");
            Check(estimation.LimitModes.ContainsKey(nameof(ActiveImpulseLimit.TANGENTIAL_TARGET_LIMITED)));
            var run = new FitRun { RunId = "unit-published", DatasetHash = dataset.DatasetHash, Model = "M1B" };
            var profile = ProfileExport.Build(estimation, run, EvidenceType.PUBLISHED_MEASUREMENT, ValidationStatus.PASS_IN_DOMAIN, ProfileRelease.BLOCKED, estimation.Profile.Domain, "unit-published", estimation.SurfaceId);
            Check(profile.Evidence == EvidenceType.PUBLISHED_MEASUREMENT && profile.CalibrationStatus == CalibrationStatus.CALIBRATED_IN_DOMAIN, "measured evidence may reach CALIBRATED_IN_DOMAIN");
            Check(profile.Release == ProfileRelease.BLOCKED, "a profile must not be released without explicit approval");
            Check(profile.SurfaceId == estimation.SurfaceId && profile.SurfaceId.Length > 0, "the profile is bound to the surface the training records describe");
        });

        test("Calibration: angular impulse residual is computed from the observations", () =>
        {
            var dataset = BounceDataset.Load(Path.Combine(Root(), "data", "bounce", "manifest.json"), new[] { "cross2002-tennis-ball-surfaces" });
            var config = new FitConfig { Model = "M1", BootstrapResamples = 0 };
            var estimation = Fit.Run("M1", dataset, config, tolerances, estimateBeta: false);
            var report = Metrics.Evaluate(estimation.Profile, dataset.Split("validation"), "validation", config, tolerances, gateTargets: false);
            Check(report.AngularImpulseResidualCount == 1, "the validation record observes spin");
            Check(report.AngularImpulseResidualMedianOverUncertainty > 1.5, "the published impulse pair violates the rigid tangential coupling well beyond its uncertainty: " + report.AngularImpulseResidualMedianOverUncertainty);
            var test = Metrics.Evaluate(estimation.Profile, dataset.Split("test"), "test", config, tolerances, gateTargets: false);
            Check(test.AngularImpulseResidualCount == 4);
            Check(test.AngularImpulseResiduals.Count == 4 && test.AngularImpulseResidualMaxNs > test.AngularImpulseResidualMedianNs, "per-record residuals must be reported, not only an average");
        });

        test("Calibration: lookup table export stays within the comparison tolerance", () =>
        {
            var ball = BounceProfiles.NominalType2();
            var profile = Fit.BuildProfile("M2", new[] { 1.6, 0.5, 0.2, 0.5 }, 10, ball.Id, "s1");
            var table = Tables.Build(profile, 5, 20, 40, 40);
            var comparison = Tables.Compare(profile, table, 9, 20, 40, 40, tolerances, ball, 0.02, 0.05, 0.02);
            Check(comparison.Violations.Count == 0, string.Join("; ", comparison.Violations));
            Check(comparison.Probes == 9 && comparison.ClampedProbes == 0, "Probe grid must be nodes^3 with no clamping inside the domain");
            var rejected = Tables.Compare(profile, table, 9, 20, 40, 40, tolerances, ball, -1, -1, -1);
            Check(rejected.Violations.Count == 3, "Pre-registered tolerances must be able to fail");
        });
    }

    private static BounceDataset DatasetFromRecords(List<BounceRecord> records, SyntheticSpec spec)
    {
        string directory = Path.Combine(Root(), "artifacts", "tests", "synthetic-" + spec.Seed);
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "records.jsonl"), records.Select(CalibrationJson.SerializeCompact));
        string manifest = Synthetic.ManifestJson(spec, "records.jsonl", "unit test");
        File.WriteAllText(Path.Combine(directory, "manifest.json"), manifest);
        return BounceDataset.Load(Path.Combine(directory, "manifest.json"));
    }

    private static List<BounceRecord> Published()
    {
        var dataset = BounceDataset.Load(Path.Combine(Root(), "data", "bounce", "manifest.json"), new[] { "cross2002-tennis-ball-surfaces" });
        return dataset.Records;
    }

    private static BounceDataset DatasetWithoutRecords(BounceDataset source)
    {
        string directory = Path.Combine(Root(), "artifacts", "tests", "empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "records.jsonl"), "");
        var spec = new SyntheticSpec { DatasetId = source.Manifest.Datasets[0].DatasetId, SurfaceId = source.Manifest.Datasets[0].SurfaceId };
        File.WriteAllText(Path.Combine(directory, "manifest.json"), Synthetic.ManifestJson(spec, "records.jsonl", "empty dataset"));
        return BounceDataset.Load(Path.Combine(directory, "manifest.json"));
    }
}
