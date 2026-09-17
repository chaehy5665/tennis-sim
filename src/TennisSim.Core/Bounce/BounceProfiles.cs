using System;

namespace TennisSim.Core.Bounce
{
    // Built-in design values. Every value here is an ASSUMED design prior, not a measurement.
    public static class BounceProfiles
    {
        public const string NominalBallId = "type2-nominal";
        public const string DesignSurfaceId = "court-flat-design";

        public static BallSpec NominalType2() => new BallSpec
        {
            Id = NominalBallId,
            Revision = 1,
            MassKg = 0.0577,
            RadiusM = 0.0335,
            InertiaFactorKappa = 0.55,
            Provenance = EvidenceType.ASSUMED_PRIOR,
            Notes = "nominal_not_measured; Type 2 regulation range is 56.0-59.4 g and 6.54-6.86 cm diameter; kappa=0.55 is a literature approximation, not an ITF constant"
        };

        public static SurfaceDefinition FlatDesignSurface() => new SurfaceDefinition
        {
            SurfaceId = DesignSurfaceId,
            MaterialLabel = "hard/clay/grass are UI and material tags, not formula branches",
            ConstructionDescription = "flat homogeneous design surface; no directional, loam or wear field",
            GeometryRevision = 1,
            StateRevision = 1,
            CalibrationStatus = CalibrationStatus.UNCALIBRATED
        };

        // UNCALIBRATED M1 profile used by the runtime impulse path when no fitted profile is supplied.
        // mu0 is chosen so the V1 sliding impulse reproduces the legacy tangential speed factor (0.88)
        // at the reference impact vn=-8 m/s, vt=20 m/s: q=0.12*20*m, Jn=1.74*m*8 -> mu0=0.1724.
        // This is a design assumption, not a measurement, and no surface is calibrated by it.
        public static InteractionProfile DesignUncalibrated()
        {
            var profile = new InteractionProfile
            {
                Id = "design-unc-v1",
                Revision = 1,
                ModelId = BounceModel.ModelIdV1,
                BallSpecId = NominalBallId,
                SurfaceId = DesignSurfaceId,
                VrefMS = BounceModel.NominalVrefMS,
                Evidence = EvidenceType.ASSUMED_PRIOR,
                CalibrationStatus = CalibrationStatus.UNCALIBRATED,
                ValidationStatus = ValidationStatus.NOT_RUN,
                Release = ProfileRelease.DEV_ONLY,
                Notes = "M1 design profile: constant en, constant mu, beta=0. Not fitted to any measured impact and not surface specific."
            };
            profile.NormalResponse.Kind = NormalResponseKind.Constant;
            profile.NormalResponse.En0 = 0.74;
            profile.FrictionResponse.Kind = FrictionResponseKind.Constant;
            profile.FrictionResponse.Mu0 = 0.1724;
            profile.TangentialResponse.Kind = TangentialResponseKind.Zero;
            profile.SupportedConditions.Add("design intent only; no validated ball-surface-state combination");
            profile.Parameters.Add(new ParameterSummary { Parameter = "en", Status = "FIXED", Value = 0.74 });
            profile.Parameters.Add(new ParameterSummary { Parameter = "mu_eff", Status = "FIXED", Value = 0.1724 });
            profile.Parameters.Add(new ParameterSummary { Parameter = "beta_grip", Status = "FIXED", Value = 0.0 });
            profile.Validate();
            return profile;
        }
    }

    // Runtime surface context. Spatial variation is resolved before the collision through Sample();
    // no per-bounce noise is added here.
    public sealed class SurfaceEnvironment
    {
        public BounceModelKind Model { get; set; } = BounceModelKind.ImpulseV1;
        public BallSpec Ball { get; set; } = BounceProfiles.NominalType2();
        public BallCondition? Condition { get; set; }
        public InteractionProfile Profile { get; set; } = BounceProfiles.DesignUncalibrated();
        public SurfaceDefinition? Surface { get; set; }
        public BounceTolerances Tolerances { get; set; } = new BounceTolerances();

        public string ProfileContentHash => ProfileHash.Compute(Profile);

        public SurfaceSample Sample(Vec3 contactPositionM) => new SurfaceSample
        {
            ContactPositionM = contactPositionM,
            Normal = new Vec3(0, 1, 0),
            MaterialId = Profile.SurfaceId,
            LocationId = "court-uniform",
            ConditionMetadata = Condition == null ? "" : Condition.ConditionLabel,
            ParameterFieldSampleId = ""
        };

        public SurfaceEnvironment Copy()
        {
            var copy = (SurfaceEnvironment)MemberwiseClone();
            copy.Ball = Ball.Copy();
            copy.Condition = Condition == null ? null : Condition.Copy();
            copy.Profile = Profile.Copy();
            copy.Surface = Surface == null ? null : Surface.Copy();
            copy.Tolerances = Tolerances.Copy();
            return copy;
        }

        public void Validate()
        {
            Tolerances.Validate();
            Ball.Validate();
            Profile.Validate();
            Condition?.Validate();
            Surface?.Validate();
            if (Profile.BallSpecId.Length > 0 && Profile.BallSpecId != Ball.Id)
                throw new ArgumentException("Interaction profile is bound to ball spec '" + Profile.BallSpecId + "', not '" + Ball.Id + "'");
            if (Surface != null && Profile.SurfaceId.Length > 0 && Surface.SurfaceId != Profile.SurfaceId)
                throw new ArgumentException("Interaction profile is bound to surface '" + Profile.SurfaceId + "', not '" + Surface.SurfaceId + "'");
        }
    }
}
