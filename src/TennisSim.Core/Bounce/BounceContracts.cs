using System;
using System.Collections.Generic;

namespace TennisSim.Core.Bounce
{
    // Model selection. Legacy is the pre-existing multiplicative bounce (no spin state).
    // ImpulseV1 is the explicit point-mass impulse model of the bounce instruction document.
    public enum BounceModelKind { Legacy, ImpulseV1 }

    // Evidence classes. Values must never be relabelled as measured data.
    public enum EvidenceType { MEASURED_RAW, PUBLISHED_MEASUREMENT, DIGITIZED_MEASUREMENT, NORMATIVE_CONSTRAINT, SYNTHETIC, ASSUMED_PRIOR }

    public enum CalibrationStatus { UNCALIBRATED, FITTED_SYNTHETIC_ONLY, CALIBRATED_IN_DOMAIN, MODEL_INADEQUATE, DATA_INSUFFICIENT }

    public enum ValidationStatus { NOT_RUN, PASS_IN_DOMAIN, FAIL, MODEL_INADEQUATE, NOT_OBSERVED }

    // Operating gate for a profile. Only APPROVED profiles may be promoted to a released build.
    public enum ProfileRelease { BLOCKED, DEV_ONLY, APPROVED }

    // Physical classification of the contact. Structural input rejection uses ArgumentException instead.
    // SETTLED is a resting-contact policy decision, not a measured micro-bounce: see BounceModel.
    public enum BounceStatus { RESOLVED, SETTLED, SEPARATING_NO_IMPULSE, TANGENTIAL_CONTACT_NO_IMPULSE, DEEP_INITIAL_PENETRATION, NUMERICAL_FAILURE }

    // Which design bound produced the tangential impulse. NOT a contact-history claim.
    public enum ActiveImpulseLimit { NONE, NEAR_ZERO_SLIP, COULOMB_LIMITED, TANGENTIAL_TARGET_LIMITED, TIE_WITHIN_TOLERANCE }

    public enum NormalResponseKind { Constant, StateDependentSigmoid }

    public enum FrictionResponseKind { Constant, StateDependentSigmoid }

    public enum TangentialResponseKind { Zero, ConstantBeta, StateDependentSigmoid }

    // Per-physical-quantity tolerances. One dimensionless epsilon is never reused for lengths, speeds and times.
    public sealed class BounceTolerances
    {
        public double NormalApproachEpsilonMS { get; set; } = 1e-9;
        public double SlipEpsilonMS { get; set; } = 1e-9;
        public double DeepPenetrationEpsilonM { get; set; } = 1e-6;
        public double ImpulseToleranceNs { get; set; } = 1e-12;
        public double TangentToleranceNs { get; set; } = 1e-12;
        public double TieRelativeTolerance { get; set; } = 1e-9;
        public double AngularImpulseTolerance { get; set; } = 1e-9;
        public double NormalRotationToleranceRadS { get; set; } = 1e-9;
        public double SlipDenominatorEpsilonM2S2 { get; set; } = 1e-12;
        // A post-contact normal speed below this cannot raise the centre by more than
        // v^2/(2g) = 2e-5 m, far below every modelled length scale.
        public double SettleNormalSpeedMS { get; set; } = 0.02;
        public double EnergyAtolJ { get; set; } = 1e-10;
        public double EnergyRtol { get; set; } = 1e-12;

        public BounceTolerances Copy() => (BounceTolerances)MemberwiseClone();

        public void Validate()
        {
            double[] values = { NormalApproachEpsilonMS, SlipEpsilonMS, DeepPenetrationEpsilonM, ImpulseToleranceNs, TangentToleranceNs, TieRelativeTolerance, AngularImpulseTolerance, NormalRotationToleranceRadS, SlipDenominatorEpsilonM2S2, SettleNormalSpeedMS, EnergyAtolJ, EnergyRtol };
            foreach (double v in values) if (!Vec3.Finite(v) || v < 0) throw new ArgumentException("Bounce tolerances must be finite and non-negative");
        }
    }

    // Ball geometry and inertia. I = kappa * m * R^2; kappa is an approximation, not an ITF constant.
    public sealed class BallSpec
    {
        public string Id { get; set; } = "";
        public int Revision { get; set; } = 1;
        public double MassKg { get; set; }
        public double RadiusM { get; set; }
        public double InertiaFactorKappa { get; set; }
        public EvidenceType Provenance { get; set; } = EvidenceType.ASSUMED_PRIOR;
        public double? MassUncertaintyKg { get; set; }
        public double? RadiusUncertaintyM { get; set; }
        public double? InertiaFactorUncertainty { get; set; }
        public string Notes { get; set; } = "";

        public double InertiaKgM2 => InertiaFactorKappa * MassKg * RadiusM * RadiusM;

        public BallSpec Copy() => (BallSpec)MemberwiseClone();

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Ball spec id is required");
            if (Revision < 1) throw new ArgumentException("Ball spec revision must be positive");
            if (!Vec3.Finite(MassKg) || MassKg <= 0) throw new ArgumentException("Ball mass must be a positive finite number");
            if (!Vec3.Finite(RadiusM) || RadiusM <= 0) throw new ArgumentException("Ball radius must be a positive finite number");
            if (!Vec3.Finite(InertiaFactorKappa) || InertiaFactorKappa <= 0) throw new ArgumentException("Inertia factor kappa must be a positive finite number");
            foreach (double? value in new[] { MassUncertaintyKg, RadiusUncertaintyM, InertiaFactorUncertainty })
                if (value.HasValue && (!Vec3.Finite(value.Value) || value.Value < 0)) throw new ArgumentException("Uncertainties must be finite and non-negative");
        }
    }

    // State of one physical ball. Never inferred from match counts.
    public sealed class BallCondition
    {
        public string BallId { get; set; } = "";
        public string BatchId { get; set; } = "";
        public double? TemperatureC { get; set; }
        public double? MeasuredPressurePa { get; set; }
        public string ConditionLabel { get; set; } = "";
        public string UsageMetadata { get; set; } = "";
        public int ConditionRevision { get; set; } = 1;

        public BallCondition Copy() => (BallCondition)MemberwiseClone();

        public void Validate()
        {
            foreach (double? value in new[] { TemperatureC, MeasuredPressurePa })
                if (value.HasValue && !Vec3.Finite(value.Value)) throw new ArgumentException("Ball condition values must be finite when present");
            if (MeasuredPressurePa.HasValue && MeasuredPressurePa.Value < 0) throw new ArgumentException("Measured pressure cannot be negative");
            if (ConditionRevision < 1) throw new ArgumentException("Ball condition revision must be positive");
        }
    }

    // Geometry, construction and state of a court surface. Holds no restitution or friction values.
    public sealed class SurfaceDefinition
    {
        public string SurfaceId { get; set; } = "";
        public string MaterialLabel { get; set; } = "";
        public string ConstructionDescription { get; set; } = "";
        public int GeometryRevision { get; set; } = 1;
        public int StateRevision { get; set; } = 1;
        public List<string> ReferenceConditions { get; set; } = new List<string>();
        public List<string> InteractionProfileIds { get; set; } = new List<string>();
        public CalibrationStatus CalibrationStatus { get; set; } = CalibrationStatus.UNCALIBRATED;

        public SurfaceDefinition Copy()
        {
            var copy = (SurfaceDefinition)MemberwiseClone();
            copy.ReferenceConditions = new List<string>(ReferenceConditions);
            copy.InteractionProfileIds = new List<string>(InteractionProfileIds);
            return copy;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SurfaceId)) throw new ArgumentException("Surface id is required");
            if (GeometryRevision < 1 || StateRevision < 1) throw new ArgumentException("Surface revisions must be positive");
            foreach (string condition in ReferenceConditions) if (string.IsNullOrWhiteSpace(condition)) throw new ArgumentException("Empty surface reference condition");
        }
    }

    // One sampled contact location. Spatial variation is decided before the collision, never inside it.
    public sealed class SurfaceSample
    {
        public Vec3 ContactPositionM { get; set; }
        public Vec3 Normal { get; set; } = new Vec3(0, 1, 0);
        public string MaterialId { get; set; } = "";
        public string LocationId { get; set; } = "";
        public string ConditionMetadata { get; set; } = "";
        public string ParameterFieldSampleId { get; set; } = "";

        public SurfaceSample Copy() => (SurfaceSample)MemberwiseClone();

        public void Validate()
        {
            if (!ContactPositionM.IsFinite) throw new ArgumentException("Contact position must be finite");
            if (!Normal.IsFinite) throw new ArgumentException("Surface normal must be finite");
            double length = Normal.Length;
            if (Math.Abs(length - 1) > 1e-9) throw new ArgumentException("Surface normal must be normalized; length=" + length.ToString("R"));
        }
    }

    // Impact input. Coordinates follow the existing engine: X width, Y up, Z court length.
    public sealed class ImpactState
    {
        public Vec3 PositionM { get; set; }
        public Vec3 VelocityMS { get; set; }
        public Vec3 AngularVelocityRadS { get; set; }
        public double ImpactTimeS { get; set; }

        public ImpactState Copy() => (ImpactState)MemberwiseClone();
    }

    // Impact output plus diagnostics. Profile identity is repeated so a replay is self-describing.
    public sealed class BounceResult
    {
        public ImpactState PostState { get; set; } = new ImpactState();
        public double NormalImpulseNs { get; set; }
        public Vec3 TangentImpulseNs { get; set; }
        public Vec3 PreContactSlipMS { get; set; }
        public Vec3 PostContactSlipMS { get; set; }
        public double NormalCorUsed { get; set; }
        public double MuEffectiveUsed { get; set; }
        public double BetaGripUsed { get; set; }
        public double? BetaEffective { get; set; }
        public ActiveImpulseLimit ActiveImpulseLimit { get; set; } = ActiveImpulseLimit.NONE;
        public double EnergyBeforeJ { get; set; }
        public double EnergyAfterJ { get; set; }
        public string ProfileId { get; set; } = "";
        public int ProfileRevision { get; set; }
        public string ProfileHash { get; set; } = "";
        public string ModelId { get; set; } = "";
        public bool OutOfDomain { get; set; }
        public BounceStatus Status { get; set; } = BounceStatus.RESOLVED;
        public List<string> Warnings { get; set; } = new List<string>();

        public BounceResult Copy()
        {
            var copy = (BounceResult)MemberwiseClone();
            copy.PostState = PostState.Copy();
            copy.Warnings = new List<string>(Warnings);
            return copy;
        }
    }
}
