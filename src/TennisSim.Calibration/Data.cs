using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TennisSim.Core;
using TennisSim.Core.Bounce;

namespace TennisSim.Calibration;

public static class CalibrationJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    // JSONL records must be one line per record, so the compact options are kept separately.
    public static readonly JsonSerializerOptions CompactOptions = new(Options) { WriteIndented = false };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static string SerializeCompact<T>(T value) => JsonSerializer.Serialize(value, CompactOptions);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new ArgumentException("Empty JSON");
    public static void Save<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory != null) Directory.CreateDirectory(directory);
        File.WriteAllText(path, Serialize(value));
    }
    public static T Load<T>(string path) => Deserialize<T>(File.ReadAllText(path));
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

// Which measured components exist. An unmeasured spin is not a zero spin.
public sealed class ObservationMask
{
    public bool[] VelocityAfter { get; set; } = new[] { true, true, true };
    public bool[] AngularVelocityAfter { get; set; } = new[] { false, false, false };
    public bool AngularVelocityBeforeMeasured { get; set; }
    public bool PositionMeasured { get; set; } = true;
    public bool NormalMeasured { get; set; } = true;
}

// Collision record contract (instruction document, section 8.2). Missing values stay null and are
// declared through the mask; they are never replaced by zero.
public sealed class BounceRecord
{
    public string RecordId { get; set; } = "";
    public string DatasetId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public EvidenceType EvidenceType { get; set; } = EvidenceType.MEASURED_RAW;
    public string ExperimentId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SampleId { get; set; } = "";
    public string BallId { get; set; } = "";
    public string BallBatchId { get; set; } = "";
    public string BallSpecId { get; set; } = BounceProfiles.NominalBallId;
    public string SurfaceId { get; set; } = "";
    public string SurfaceConditionId { get; set; } = "";
    public string LocationId { get; set; } = "";
    public string SourceCoordinateSystem { get; set; } = "X width, Y up, Z court length";
    public int TransformRevision { get; set; } = 1;
    public double ImpactTimeS { get; set; }
    public double[] PositionM { get; set; } = new double[3];
    public double[] Normal { get; set; } = new double[] { 0, 1, 0 };
    public double[] VelocityBeforeMS { get; set; } = new double[3];
    public double[] VelocityAfterMS { get; set; } = new double[3];
    public double[] AngularVelocityBeforeRadS { get; set; } = new double[3];
    public double[] AngularVelocityAfterRadS { get; set; } = new double[3];
    public ObservationMask Observed { get; set; } = new ObservationMask();
    public double[] VelocityAfterStdDevMS { get; set; } = new double[] { 0.5, 0.5, 0.5 };
    public double[] AngularVelocityAfterStdDevRadS { get; set; } = new double[] { 20, 20, 20 };
    public double? BallTemperatureC { get; set; }
    public double? AirTemperatureC { get; set; }
    public double? SurfaceTemperatureC { get; set; }
    public double? RelativeHumidity { get; set; }
    public double? AtmosphericPressurePa { get; set; }
    public string BallUsageMetadata { get; set; } = "";
    public string AcquisitionMethod { get; set; } = "";
    public string SourcePage { get; set; } = "";
    public string SourceTableOrFigure { get; set; } = "";
    public string SourceRow { get; set; } = "";
    public string ExtractionMethod { get; set; } = "";
    public double? ExtractionUncertainty { get; set; }
    public List<string> QualityFlags { get; set; } = new();

    public Vec3 Position => new(PositionM[0], PositionM[1], PositionM[2]);
    public Vec3 NormalVector => new(Normal[0], Normal[1], Normal[2]);
    public Vec3 VelocityBefore => new(VelocityBeforeMS[0], VelocityBeforeMS[1], VelocityBeforeMS[2]);
    public Vec3 VelocityAfter => new(VelocityAfterMS[0], VelocityAfterMS[1], VelocityAfterMS[2]);
    public Vec3 SpinBefore => new(AngularVelocityBeforeRadS[0], AngularVelocityBeforeRadS[1], AngularVelocityBeforeRadS[2]);
    public Vec3 SpinAfter => new(AngularVelocityAfterRadS[0], AngularVelocityAfterRadS[1], AngularVelocityAfterRadS[2]);

    public bool HasVelocityAfterObservation => Observed.VelocityAfter.Length == 3 && Observed.VelocityAfter[0] && Observed.VelocityAfter[1] && Observed.VelocityAfter[2] && Observed.PositionMeasured && Observed.NormalMeasured;
    // Tangential and spin response require a measured incident spin: without it the contact slip is unknown.
    public bool UsableForTangential => HasVelocityAfterObservation && Observed.AngularVelocityBeforeMeasured;
    public bool UsableForAngular => UsableForTangential && Observed.AngularVelocityAfter.Length == 3 && (Observed.AngularVelocityAfter[0] || Observed.AngularVelocityAfter[1] || Observed.AngularVelocityAfter[2]);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RecordId)) throw new ArgumentException("Record id is required");
        if (string.IsNullOrWhiteSpace(DatasetId)) throw new ArgumentException("Dataset id is required on " + RecordId);
        if (string.IsNullOrWhiteSpace(SourceId)) throw new ArgumentException("Source id is required on " + RecordId);
        if (string.IsNullOrWhiteSpace(SessionId)) throw new ArgumentException("Session id is required on " + RecordId + " (splits and cluster bootstrap need it)");
        if (!double.IsFinite(ImpactTimeS)) throw new ArgumentException("Impact time must be finite on " + RecordId);
        if (TransformRevision < 1) throw new ArgumentException("Transform revision must be positive on " + RecordId);
        Require(RecordId, "positionM", PositionM, 3); Require(RecordId, "normal", Normal, 3);
        Require(RecordId, "velocityBeforeMS", VelocityBeforeMS, 3); Require(RecordId, "velocityAfterMS", VelocityAfterMS, 3);
        Require(RecordId, "angularVelocityBeforeRadS", AngularVelocityBeforeRadS, 3); Require(RecordId, "angularVelocityAfterRadS", AngularVelocityAfterRadS, 3);
        Require(RecordId, "velocityAfterStdDevMS", VelocityAfterStdDevMS, 3); Require(RecordId, "angularVelocityAfterStdDevRadS", AngularVelocityAfterStdDevRadS, 3);
        foreach (double sigma in VelocityAfterStdDevMS.Concat(AngularVelocityAfterStdDevRadS)) if (sigma <= 0) throw new ArgumentException("Observation standard deviations must be positive on " + RecordId);
        if (Math.Abs(NormalVector.Length - 1) > 1e-9) throw new ArgumentException("Normal must be normalized on " + RecordId);
        if (!Observed.AngularVelocityBeforeMeasured && SpinBefore.Length != 0) throw new ArgumentException("Unmeasured incident spin must be reported as zeros plus mask=false on " + RecordId);
        if (!Observed.VelocityAfter.Length.Equals(3) || !Observed.AngularVelocityAfter.Length.Equals(3)) throw new ArgumentException("Observation mask must have three components per vector on " + RecordId);
        if (ExtractionUncertainty.HasValue && ExtractionUncertainty.Value < 0) throw new ArgumentException("Negative extraction uncertainty on " + RecordId);
    }

    private static void Require(string id, string name, double[] values, int length)
    {
        if (values == null || values.Length != length) throw new ArgumentException(name + " must have exactly " + length + " components on " + id);
        foreach (double value in values) if (!double.IsFinite(value)) throw new ArgumentException(name + " must be finite on " + id);
    }
}

public sealed class BounceSource
{
    public string SourceId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Doi { get; set; } = "";
    public string AccessDate { get; set; } = "";
    public string Role { get; set; } = "";
    public string License { get; set; } = "";
    public bool RawRedistributionAllowed { get; set; }
    public string FileSha256 { get; set; } = "";
    public string OriginalUnits { get; set; } = "";
    public string ExtractionNotes { get; set; } = "";
    public string Reviewer { get; set; } = "";
}

public sealed class BounceDatasetEntry
{
    public string DatasetId { get; set; } = "";
    public EvidenceType EvidenceType { get; set; } = EvidenceType.MEASURED_RAW;
    public string RecordFile { get; set; } = "";
    public string Format { get; set; } = "jsonl";
    public string Description { get; set; } = "";
    public string Generator { get; set; } = "";
    public string GeneratorSeed { get; set; } = "";
    public string BallSpecId { get; set; } = BounceProfiles.NominalBallId;
    public string SurfaceId { get; set; } = "";
    public List<string> ReferenceConditions { get; set; } = new();
    public string GroupKey { get; set; } = "sessionId";
}

public sealed class BounceManifest
{
    public string SchemaVersion { get; set; } = "1.0";
    public string TargetPopulation { get; set; } = "UNSPECIFIED";
    public List<BounceSource> Sources { get; set; } = new();
    public List<BounceDatasetEntry> Datasets { get; set; } = new();
    // Group (session or experiment) to split. Fixed before fitting and covered by the dataset hash.
    public Dictionary<string, string> SplitAssignment { get; set; } = new();
    public string SplitRule { get; set; } = "explicit splitAssignment; a group absent from it falls back to SHA256(group) mod 5: 0 test, 1 validation, else train";
    public List<string> Notes { get; set; } = new();
}

public sealed class BounceDataset
{
    public BounceManifest Manifest { get; }
    public List<BounceRecord> Records { get; }
    public Dictionary<string, string> Splits { get; }
    public string DatasetHash { get; }
    public string ManifestHash { get; }
    public string ManifestPath { get; }
    public Dictionary<string, int> EvidenceCounts { get; } = new();
    public Dictionary<string, int> RecordsPerDataset { get; } = new();

    private BounceDataset(BounceManifest manifest, List<BounceRecord> records, Dictionary<string, string> splits, string hash, string manifestHash, string manifestPath)
    { Manifest = manifest; Records = records; Splits = splits; DatasetHash = hash; ManifestHash = manifestHash; ManifestPath = manifestPath; }

    public IEnumerable<BounceRecord> Split(string name) => Records.Where(r => SplitOf(r) == name);
    public string SplitOf(BounceRecord record) => Splits[GroupOf(record)];

    public string GroupOf(BounceRecord record)
    {
        var entry = Manifest.Datasets.First(d => d.DatasetId == record.DatasetId);
        return entry.GroupKey switch
        {
            "sessionId" => record.SessionId,
            "experimentId" => record.ExperimentId,
            "datasetId" => record.DatasetId,
            _ => record.SessionId
        };
    }

    public int MeasuredRecords => Records.Count(r => r.EvidenceType is EvidenceType.MEASURED_RAW or EvidenceType.PUBLISHED_MEASUREMENT or EvidenceType.DIGITIZED_MEASUREMENT);
    public int SyntheticRecords => Records.Count(r => r.EvidenceType == EvidenceType.SYNTHETIC);

    public static BounceDataset Load(string manifestPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? ".";
        var manifest = CalibrationJson.Load<BounceManifest>(manifestPath);
        if (manifest.SchemaVersion != "1.0") throw new ArgumentException("Unsupported manifest schema " + manifest.SchemaVersion);
        var sourceIds = manifest.Sources.Select(s => s.SourceId).ToHashSet();
        var records = new List<BounceRecord>();
        var hashInput = new StringBuilder(CalibrationJson.Serialize(manifest));
        foreach (var entry in manifest.Datasets)
        {
            if (string.IsNullOrWhiteSpace(entry.DatasetId)) throw new ArgumentException("Dataset entry without id");
            if (entry.Format != "jsonl") throw new ArgumentException("Dataset " + entry.DatasetId + " declares format '" + entry.Format + "'; this tool loads JSONL records only");
            if (string.IsNullOrWhiteSpace(entry.RecordFile)) throw new ArgumentException("Dataset " + entry.DatasetId + " has no record file");
            string path = Path.IsPathRooted(entry.RecordFile) ? entry.RecordFile : Path.Combine(directory, entry.RecordFile);
            if (!File.Exists(path)) throw new IOException("Missing record file for dataset " + entry.DatasetId + ": " + path);
            hashInput.Append(entry.DatasetId).Append(':').Append(CalibrationJson.HashFile(path)).Append('\n');
            foreach (var record in ReadRecords(path, entry))
            {
                if (string.IsNullOrWhiteSpace(record.SourceId)) throw new ArgumentException("Record " + record.RecordId + " has no sourceId");
                if (!sourceIds.Contains(record.SourceId)) throw new ArgumentException("Record " + record.RecordId + " references unknown source " + record.SourceId);
                if (record.DatasetId != entry.DatasetId) throw new ArgumentException("Record " + record.RecordId + " declares dataset " + record.DatasetId + " but is listed under " + entry.DatasetId);
                if (record.EvidenceType != entry.EvidenceType) throw new ArgumentException("Record " + record.RecordId + " evidence " + record.EvidenceType + " contradicts dataset evidence " + entry.EvidenceType);
                record.Validate();
                records.Add(record);
            }
        }
        var splits = new Dictionary<string, string>();
        foreach (var group in records.Select(GroupKeyOf(manifest)).Distinct().OrderBy(g => g, StringComparer.Ordinal))
        {
            string split = manifest.SplitAssignment.TryGetValue(group, out var assigned) ? assigned : FallbackSplit(group);
            if (split is not ("train" or "validation" or "test")) throw new ArgumentException("Group " + group + " maps to unknown split " + split);
            splits[group] = split;
        }
        var dataset = new BounceDataset(manifest, records, splits, CalibrationJson.Hash(hashInput.ToString()), CalibrationJson.Hash(CalibrationJson.Serialize(manifest)), Path.GetFullPath(manifestPath));
        foreach (var record in records)
        {
            dataset.EvidenceCounts[record.EvidenceType.ToString()] = dataset.EvidenceCounts.GetValueOrDefault(record.EvidenceType.ToString()) + 1;
            dataset.RecordsPerDataset[record.DatasetId] = dataset.RecordsPerDataset.GetValueOrDefault(record.DatasetId) + 1;
        }
        return dataset;
    }

    private static Func<BounceRecord, string> GroupKeyOf(BounceManifest manifest) => record =>
    {
        var entry = manifest.Datasets.First(d => d.DatasetId == record.DatasetId);
        return entry.GroupKey switch
        {
            "experimentId" => record.ExperimentId,
            "datasetId" => record.DatasetId,
            _ => record.SessionId
        };
    };

    private static string FallbackSplit(string group)
    {
        string hash = CalibrationJson.Hash(group);
        int bucket = int.Parse(hash.Substring(0, 4), System.Globalization.NumberStyles.HexNumber) % 5;
        return bucket == 0 ? "test" : bucket == 1 ? "validation" : "train";
    }

    private static IEnumerable<BounceRecord> ReadRecords(string path, BounceDatasetEntry entry)
    {
        int line = 0;
        foreach (string raw in File.ReadAllLines(path))
        {
            line++;
            string text = raw.Trim();
            if (text.Length == 0 || text.StartsWith("#")) continue;
            BounceRecord record;
            try { record = CalibrationJson.Deserialize<BounceRecord>(text); }
            catch (Exception ex) { throw new ArgumentException(path + ":" + line + ": " + ex.Message); }
            if (string.IsNullOrWhiteSpace(record.DatasetId)) record.DatasetId = entry.DatasetId;
            if (string.IsNullOrWhiteSpace(record.BallSpecId)) record.BallSpecId = entry.BallSpecId;
            if (string.IsNullOrWhiteSpace(record.SurfaceId)) record.SurfaceId = entry.SurfaceId;
            yield return record;
        }
    }
}

// Synthetic data with a published truth. Used to exercise the pipeline; never a calibration input.
public sealed class SyntheticSpec
{
    public int Count { get; set; } = 240;
    public uint Seed { get; set; } = 20260915;
    public int Sessions { get; set; } = 12;
    public string DatasetId { get; set; } = "synthetic-type2-flat";
    public string SourceId { get; set; } = "SNT1";
    public string SurfaceId { get; set; } = "synthetic-flat";
    public double TruthEn { get; set; } = 0.83;
    public double TruthMu { get; set; } = 0.35;
    public double TruthBeta { get; set; } = 0.0;
    public double VelocityNoiseMS { get; set; } = 0.25;
    public double AngularNoiseRadS { get; set; } = 15;
    public double SpinMeasuredFraction { get; set; } = 0.7;
    public double SpinAfterObservedFraction { get; set; } = 0.6;
}

public static class Synthetic
{
    public static string ManifestJson(SyntheticSpec spec, string recordFile, string manifestHashNote)
    {
        var manifest = new BounceManifest
        {
            SchemaVersion = "1.0",
            TargetPopulation = "SYNTHETIC_MODEL_SELFTEST",
            Sources = { new BounceSource { SourceId = spec.SourceId, Title = "Synthetic V1 model sample", Publisher = "TennisSim calibration self test", Version = "1", AccessDate = "2026-09-15", Role = "pipeline self test only", License = "not applicable", RawRedistributionAllowed = true, OriginalUnits = "m, m/s, rad/s", ExtractionNotes = "generated by tennis-calibrate synthetic from a published truth vector" } },
            Datasets = { new BounceDatasetEntry { DatasetId = spec.DatasetId, EvidenceType = EvidenceType.SYNTHETIC, RecordFile = recordFile, Format = "jsonl", Description = "seeded synthetic impacts from BounceModel V1; SYNTHETIC, not measured", Generator = "TennisSim.Calibration Synthetic.Generate", GeneratorSeed = spec.Seed.ToString(), BallSpecId = BounceProfiles.NominalBallId, SurfaceId = spec.SurfaceId, GroupKey = "sessionId" } },
            SplitAssignment = { },
            Notes = { "SYNTHETIC dataset. It verifies software behaviour and must never be labelled EMPIRICALLY_CALIBRATED.", manifestHashNote }
        };
        return CalibrationJson.Serialize(manifest);
    }

    public static List<BounceRecord> Generate(SyntheticSpec spec)
    {
        var ball = BounceProfiles.NominalType2();
        var profile = new InteractionProfile { Id = "synthetic-truth", Revision = 1, ModelId = BounceModel.ModelIdV1, BallSpecId = ball.Id, SurfaceId = spec.SurfaceId, Evidence = EvidenceType.SYNTHETIC };
        profile.NormalResponse.Kind = NormalResponseKind.Constant; profile.NormalResponse.En0 = spec.TruthEn;
        profile.FrictionResponse.Kind = FrictionResponseKind.Constant; profile.FrictionResponse.Mu0 = spec.TruthMu;
        if (spec.TruthBeta == 0) profile.TangentialResponse.Kind = TangentialResponseKind.Zero;
        else { profile.TangentialResponse.Kind = TangentialResponseKind.ConstantBeta; profile.TangentialResponse.Beta = spec.TruthBeta; }
        profile.Validate();
        var rng = new SeedRandom(spec.Seed);
        var records = new List<BounceRecord>();
        double[] speeds = { 8, 12, 16, 20, 24, 30 };
        double[] angles = { 8, 12, 16, 22, 30, 45 };
        double[] spinsRpm = { 0, 0, 300, -300, 800, -800, 1500, -1500, 2500, -2500 };
        for (int i = 0; i < spec.Count; i++)
        {
            double speed = speeds[i % speeds.Length] * (0.9 + 0.2 * rng.Next());
            double angle = angles[(i / speeds.Length) % angles.Length] * Math.PI / 180.0;
            double rpm = spinsRpm[(i * 7) % spinsRpm.Length];
            double omegaX = rpm * 2 * Math.PI / 60.0;
            Vec3 normal = new(0, 1, 0);
            Vec3 contact = new(-3.0 + 6.0 * rng.Next(), 0, -6.0 + 12.0 * rng.Next());
            var pre = new ImpactState { PositionM = contact + normal * ball.RadiusM, VelocityMS = new Vec3(0, -speed * Math.Sin(angle), speed * Math.Cos(angle)), AngularVelocityRadS = new Vec3(omegaX, 0, 0), ImpactTimeS = 0 };
            var sample = new SurfaceSample { ContactPositionM = contact, Normal = normal, MaterialId = spec.SurfaceId, LocationId = "synthetic" };
            var truth = BounceModel.Resolve(pre, ball, null, sample, profile, new BounceTolerances());
            if (truth.Status != BounceStatus.RESOLVED) continue;
            bool spinMeasured = rng.Next() < spec.SpinMeasuredFraction;
            bool spinObserved = spinMeasured && rng.Next() < spec.SpinAfterObservedFraction;
            var record = new BounceRecord
            {
                RecordId = spec.DatasetId + "-" + i.ToString("D4"),
                DatasetId = spec.DatasetId,
                SourceId = spec.SourceId,
                EvidenceType = EvidenceType.SYNTHETIC,
                ExperimentId = "synthetic-v1-" + (i % 3),
                SessionId = "session-" + (i % spec.Sessions).ToString("D2"),
                SampleId = "sample-" + i.ToString("D4"),
                BallId = ball.Id,
                BallSpecId = ball.Id,
                SurfaceId = spec.SurfaceId,
                SurfaceConditionId = "dry-controlled",
                AcquisitionMethod = "forward model plus seeded noise",
                ExtractionMethod = "generated",
                PositionM = new[] { pre.PositionM.X, pre.PositionM.Y, pre.PositionM.Z },
                Normal = new[] { normal.X, normal.Y, normal.Z },
                VelocityBeforeMS = new[] { pre.VelocityMS.X, pre.VelocityMS.Y, pre.VelocityMS.Z },
                VelocityAfterMS = Noise3(truth.PostState.VelocityMS, spec.VelocityNoiseMS, rng),
                AngularVelocityBeforeRadS = spinMeasured ? new[] { omegaX, 0.0, 0.0 } : new[] { 0.0, 0.0, 0.0 },
                AngularVelocityAfterRadS = spinObserved ? Noise3(truth.PostState.AngularVelocityRadS, spec.AngularNoiseRadS, rng) : new[] { 0.0, 0.0, 0.0 },
                VelocityAfterStdDevMS = new[] { spec.VelocityNoiseMS, spec.VelocityNoiseMS, spec.VelocityNoiseMS },
                AngularVelocityAfterStdDevRadS = new[] { spec.AngularNoiseRadS, spec.AngularNoiseRadS, spec.AngularNoiseRadS },
                Observed = new ObservationMask { VelocityAfter = new[] { true, true, true }, AngularVelocityAfter = new[] { spinObserved, spinObserved, spinObserved }, AngularVelocityBeforeMeasured = spinMeasured, PositionMeasured = true, NormalMeasured = true },
                QualityFlags = { "SYNTHETIC" }
            };
            record.Validate();
            records.Add(record);
        }
        return records;
    }

    private static double[] Noise3(Vec3 value, double sigma, SeedRandom rng)
    {
        double[] result = { value.X, value.Y, value.Z };
        for (int i = 0; i < 3; i++) result[i] += sigma * (rng.Next() + rng.Next() + rng.Next() - 1.5) * 2 / 1.7320508075688772;
        return result;
    }
}
