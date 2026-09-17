using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TennisSim.Core.Bounce
{
    // Normal response model. Constant uses En0 directly. The sigmoid form is a design proposal,
    // not a measured natural law: en = sigmoid(a0 + a1*f1 + a2*f2).
    public sealed class NormalResponseParameters
    {
        public NormalResponseKind Kind { get; set; } = NormalResponseKind.Constant;
        public double En0 { get; set; }
        public double A0 { get; set; }
        public double A1 { get; set; }
        public double A2 { get; set; }

        public NormalResponseParameters Copy() => (NormalResponseParameters)MemberwiseClone();

        public void Validate()
        {
            foreach (double v in new[] { En0, A0, A1, A2 }) if (!Vec3.Finite(v)) throw new ArgumentException("Normal response parameters must be finite");
            if (Kind == NormalResponseKind.Constant && (En0 < 0 || En0 > 1)) throw new ArgumentException("V1 requires 0 <= en <= 1");
        }
    }

    // Effective friction. mu_eff = mu_max * sigmoid(b0 + b1*f3) in the state dependent form.
    public sealed class FrictionResponseParameters
    {
        public FrictionResponseKind Kind { get; set; } = FrictionResponseKind.Constant;
        public double Mu0 { get; set; }
        public double MuMax { get; set; } = 2.0;
        public double B0 { get; set; }
        public double B1 { get; set; }

        public FrictionResponseParameters Copy() => (FrictionResponseParameters)MemberwiseClone();

        public void Validate()
        {
            foreach (double v in new[] { Mu0, MuMax, B0, B1 }) if (!Vec3.Finite(v)) throw new ArgumentException("Friction response parameters must be finite");
            if (Kind == FrictionResponseKind.Constant && Mu0 < 0) throw new ArgumentException("V1 requires mu_eff >= 0");
            if (Kind == FrictionResponseKind.StateDependentSigmoid && MuMax <= 0) throw new ArgumentException("mu_max must be positive");
        }
    }

    // Effective tangential response. Zero is the reference model (slip removed when friction saturates).
    public sealed class TangentialResponseParameters
    {
        public TangentialResponseKind Kind { get; set; } = TangentialResponseKind.Zero;
        public double Beta { get; set; }
        public double C0 { get; set; }

        public TangentialResponseParameters Copy() => (TangentialResponseParameters)MemberwiseClone();

        public void Validate()
        {
            foreach (double v in new[] { Beta, C0 }) if (!Vec3.Finite(v)) throw new ArgumentException("Tangential response parameters must be finite");
            if (Kind == TangentialResponseKind.ConstantBeta && (Beta < 0 || Beta > 1)) throw new ArgumentException("V1 requires 0 <= beta <= 1");
        }
    }

    // Coefficient evaluation domain fixed before fitting. Inputs are never modified, only the
    // parameter evaluation point is bounded and the excursion is recorded.
    public sealed class ProfileDomain
    {
        public double? SnMin { get; set; }
        public double? SnMax { get; set; }
        public double? StMin { get; set; }
        public double? StMax { get; set; }
        public double? SuMin { get; set; }
        public double? SuMax { get; set; }

        public ProfileDomain Copy() => (ProfileDomain)MemberwiseClone();

        public void Validate()
        {
            foreach (double? v in new[] { SnMin, SnMax, StMin, StMax, SuMin, SuMax })
                if (v.HasValue && (!Vec3.Finite(v.Value) || v.Value < 0)) throw new ArgumentException("Domain bounds must be finite, non-negative or absent");
            if (SnMin.HasValue && SnMax.HasValue && SnMin.Value > SnMax.Value) throw new ArgumentException("Reversed sn domain");
            if (StMin.HasValue && StMax.HasValue && StMin.Value > StMax.Value) throw new ArgumentException("Reversed st domain");
            if (SuMin.HasValue && SuMax.HasValue && SuMin.Value > SuMax.Value) throw new ArgumentException("Reversed su domain");
        }
    }

    // Optional lookup-table representation of the state dependent model. Axis nodes, flat value
    // layout, clamping rule and bilinear tie-break rule are part of the exported contract.
    public sealed class ProfileTable
    {
        public double[] SnNodes { get; set; } = Array.Empty<double>();
        public double[] StNodes { get; set; } = Array.Empty<double>();
        public double[] SuNodes { get; set; } = Array.Empty<double>();
        public double[] EnValues { get; set; } = Array.Empty<double>();
        public double[] MuValues { get; set; } = Array.Empty<double>();
        public double[] BetaValues { get; set; } = Array.Empty<double>();

        public int SnCount => SnNodes.Length;
        public int StCount => StNodes.Length;
        public int SuCount => SuNodes.Length;

        public int CellCount => SnCount * StCount * SuCount;

        public ProfileTable Copy()
        {
            var copy = (ProfileTable)MemberwiseClone();
            copy.SnNodes = (double[])SnNodes.Clone(); copy.StNodes = (double[])StNodes.Clone(); copy.SuNodes = (double[])SuNodes.Clone();
            copy.EnValues = (double[])EnValues.Clone(); copy.MuValues = (double[])MuValues.Clone(); copy.BetaValues = (double[])BetaValues.Clone();
            return copy;
        }

        public void Validate()
        {
            if (SnCount < 2 || StCount < 2 || SuCount < 2) throw new ArgumentException("Lookup table requires at least two nodes per axis");
            Ascending(SnNodes, "sn"); Ascending(StNodes, "st"); Ascending(SuNodes, "su");
            foreach (double[] values in new[] { EnValues, MuValues, BetaValues })
            {
                if (values.Length != CellCount) throw new ArgumentException("Lookup table value count must equal sn*st*su cells");
                foreach (double v in values) if (!Vec3.Finite(v)) throw new ArgumentException("Lookup table values must be finite");
            }
        }

        private static void Ascending(double[] nodes, string axis)
        {
            foreach (double v in nodes) if (!Vec3.Finite(v) || v < 0) throw new ArgumentException("Lookup " + axis + " nodes must be finite and non-negative");
            for (int i = 1; i < nodes.Length; i++) if (nodes[i] <= nodes[i - 1]) throw new ArgumentException("Lookup " + axis + " nodes must be strictly increasing");
        }

        // Multilinear interpolation. Outside the node box the nearest edge value is used and the
        // caller records OUT_OF_DOMAIN. Exact node coincidence is exact; midway is the linear mean.
        public void Evaluate(double sn, double st, double su, out double en, out double mu, out double beta, out bool clamped)
        {
            clamped = false;
            double fs = Axis(SnNodes, sn, ref clamped), ft = Axis(StNodes, st, ref clamped), fu = Axis(SuNodes, su, ref clamped);
            en = Sample(EnValues, fs, ft, fu);
            mu = Sample(MuValues, fs, ft, fu);
            beta = Sample(BetaValues, fs, ft, fu);
        }

        private static double Axis(double[] nodes, double value, ref bool clamped)
        {
            if (value < nodes[0]) { clamped = true; return 0; }
            if (value > nodes[nodes.Length - 1]) { clamped = true; return nodes.Length - 1; }
            for (int i = 1; i < nodes.Length; i++) if (value <= nodes[i]) return i - 1 + (value - nodes[i - 1]) / (nodes[i] - nodes[i - 1]);
            return nodes.Length - 1;
        }

        private double Sample(double[] values, double fs, double ft, double fu)
        {
            int s0 = (int)fs, t0 = (int)ft, u0 = (int)fu;
            int s1 = Math.Min(s0 + 1, SnCount - 1), t1 = Math.Min(t0 + 1, StCount - 1), u1 = Math.Min(u0 + 1, SuCount - 1);
            double ds = fs - s0, dt = ft - t0, du = fu - u0;
            double At(int s, int t, int u) => values[(s * StCount + t) * SuCount + u];
            double a = At(s0, t0, u0) * (1 - ds) + At(s1, t0, u0) * ds;
            double b = At(s0, t1, u0) * (1 - ds) + At(s1, t1, u0) * ds;
            double c = At(s0, t0, u1) * (1 - ds) + At(s1, t0, u1) * ds;
            double d = At(s0, t1, u1) * (1 - ds) + At(s1, t1, u1) * ds;
            double low = a * (1 - dt) + b * dt, high = c * (1 - dt) + d * dt;
            return low * (1 - du) + high * du;
        }
    }

    // Uncertainty or identification status of one fitted parameter.
    public sealed class ParameterSummary
    {
        public string Parameter { get; set; } = "";
        public string Status { get; set; } = "NOT_IDENTIFIED";
        public double? Value { get; set; }
        public double? StandardError { get; set; }
        public double? BootstrapLow { get; set; }
        public double? BootstrapHigh { get; set; }
        public bool AtBoundary { get; set; }

        public ParameterSummary Copy() => (ParameterSummary)MemberwiseClone();
    }

    // One validated ball-surface combination. This is the only place where contact coefficients live.
    public sealed class InteractionProfile
    {
        public string Id { get; set; } = "";
        public int Revision { get; set; } = 1;
        public string ModelId { get; set; } = BounceModel.ModelIdV1;
        public string BallSpecId { get; set; } = "";
        public string SurfaceId { get; set; } = "";
        public List<string> SupportedConditions { get; set; } = new List<string>();
        public double VrefMS { get; set; } = 10.0;
        public NormalResponseParameters NormalResponse { get; set; } = new NormalResponseParameters();
        public FrictionResponseParameters FrictionResponse { get; set; } = new FrictionResponseParameters();
        public TangentialResponseParameters TangentialResponse { get; set; } = new TangentialResponseParameters();
        public ProfileDomain? Domain { get; set; }
        public ProfileTable? Table { get; set; }
        public EvidenceType Evidence { get; set; } = EvidenceType.ASSUMED_PRIOR;
        public CalibrationStatus CalibrationStatus { get; set; } = CalibrationStatus.UNCALIBRATED;
        public ValidationStatus ValidationStatus { get; set; } = ValidationStatus.NOT_RUN;
        public ProfileRelease Release { get; set; } = ProfileRelease.BLOCKED;
        public string SourceDatasetHash { get; set; } = "";
        public string CalibrationRunId { get; set; } = "";
        public string Notes { get; set; } = "";
        public List<ParameterSummary> Parameters { get; set; } = new List<ParameterSummary>();

        public InteractionProfile Copy()
        {
            var copy = (InteractionProfile)MemberwiseClone();
            copy.SupportedConditions = new List<string>(SupportedConditions);
            copy.Parameters = new List<ParameterSummary>();
            foreach (var p in Parameters) copy.Parameters.Add(p.Copy());
            copy.NormalResponse = NormalResponse.Copy();
            copy.FrictionResponse = FrictionResponse.Copy();
            copy.TangentialResponse = TangentialResponse.Copy();
            copy.Domain = Domain == null ? null : Domain.Copy();
            copy.Table = Table == null ? null : Table.Copy();
            return copy;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Profile id is required");
            if (Revision < 1) throw new ArgumentException("Profile revision must be positive");
            if (string.IsNullOrWhiteSpace(ModelId)) throw new ArgumentException("Profile model id is required");
            if (!Vec3.Finite(VrefMS) || VrefMS <= 0) throw new ArgumentException("vref must be a positive finite number");
            NormalResponse.Validate();
            FrictionResponse.Validate();
            TangentialResponse.Validate();
            Domain?.Validate();
            Table?.Validate();
            foreach (string condition in SupportedConditions) if (string.IsNullOrWhiteSpace(condition)) throw new ArgumentException("Empty supported condition");
        }

        // Deterministic, culture independent, dependency free text used for the replay content hash.
        public string CanonicalText()
        {
            var sb = new StringBuilder();
            void Line(string key, string value) { sb.Append(key).Append('=').Append(value).Append('\n'); }
            void Num(string key, double value) => Line(key, value.ToString("R", CultureInfo.InvariantCulture));
            Line("id", Id);
            Line("revision", Revision.ToString(CultureInfo.InvariantCulture));
            Line("modelId", ModelId);
            Line("ballSpecId", BallSpecId);
            Line("surfaceId", SurfaceId);
            foreach (string condition in SupportedConditions) Line("condition", condition);
            Num("vrefMS", VrefMS);
            Line("normal.kind", NormalResponse.Kind.ToString());
            Num("normal.en0", NormalResponse.En0);
            Num("normal.a0", NormalResponse.A0);
            Num("normal.a1", NormalResponse.A1);
            Num("normal.a2", NormalResponse.A2);
            Line("friction.kind", FrictionResponse.Kind.ToString());
            Num("friction.mu0", FrictionResponse.Mu0);
            Num("friction.muMax", FrictionResponse.MuMax);
            Num("friction.b0", FrictionResponse.B0);
            Num("friction.b1", FrictionResponse.B1);
            Line("tangential.kind", TangentialResponse.Kind.ToString());
            Num("tangential.beta", TangentialResponse.Beta);
            Num("tangential.c0", TangentialResponse.C0);
            if (Domain != null)
            {
                void Bound(string key, double? value) => Line("domain." + key, value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "none");
                Bound("snMin", Domain.SnMin); Bound("snMax", Domain.SnMax);
                Bound("stMin", Domain.StMin); Bound("stMax", Domain.StMax);
                Bound("suMin", Domain.SuMin); Bound("suMax", Domain.SuMax);
            }
            if (Table != null)
            {
                Line("table.sn", Join(Table.SnNodes)); Line("table.st", Join(Table.StNodes)); Line("table.su", Join(Table.SuNodes));
                Line("table.en", Join(Table.EnValues)); Line("table.mu", Join(Table.MuValues)); Line("table.beta", Join(Table.BetaValues));
            }
            Line("evidence", Evidence.ToString());
            Line("calibrationStatus", CalibrationStatus.ToString());
            Line("validationStatus", ValidationStatus.ToString());
            Line("release", Release.ToString());
            Line("sourceDatasetHash", SourceDatasetHash);
            Line("calibrationRunId", CalibrationRunId);
            foreach (var p in Parameters)
                Line("parameter", p.Parameter + "|" + p.Status + "|" + Fmt(p.Value) + "|" + Fmt(p.StandardError) + "|" + Fmt(p.BootstrapLow) + "|" + Fmt(p.BootstrapHigh) + "|" + p.AtBoundary);
            return sb.ToString();
        }

        private static string Fmt(double? value) => value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "none";

        private static string Join(double[] values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++) { if (i > 0) sb.Append(','); sb.Append(values[i].ToString("R", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }
    }

    public static class ProfileHash
    {
        public static string Compute(InteractionProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            return Sha256Hex(profile.CanonicalText());
        }

        public static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
