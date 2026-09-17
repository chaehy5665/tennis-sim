# Model card - V1 ball-surface bounce model

Scope: tennissim-mvp-3, engine-side collision model and its calibration tooling. Prepared 2026-09-15.
Evidence in this repository is synthetic or a design assumption. No measured impact record exists here.

## What the model is

An effective-impulse model of a point-mass ball against a stationary local plane, evaluated in the preserved
engine frame (X width, Y up, Z court length). Inputs: pre-impact position, velocity and angular velocity, ball
spec (mass, radius, inertia factor), the sampled surface contact (position, unit normal, material/location
metadata) and a validated ball-surface interaction profile. Output: post-impact velocity and angular velocity
plus diagnostics (impulses, contact slip, used coefficients, classification mode, energies, profile identity).

Not an ITF-certified procedure, not a finite-element or deformable-ball contact, not a measurement.

## Model selection

| Candidate | Parameters | Notes |
|---|---|---|
| M1 | en, mu_eff (constant), beta = 0 | reference model |
| M2 | en(a0,a1,a2) state dependent, mu_eff constant, beta = 0 | added only when validation improves past the gate |
| M3 | M2 plus mu_max(b0,b1) and beta | requires observed outgoing spin |
| M4 | contact patch, extra torque, off-centre normal reaction | not implemented |

The fitting tool selects the simplest candidate whose validation residual is not worse, and only accepts a
richer model when the relative validation gain passes validationRmseGainForComplexity. Complexity is never
added because data is missing.

## Assumptions and their provenance

| Item | Value | Provenance | Status |
|---|---|---|---|
| Ball mass | 0.0577 kg | nominal Type 2 midpoint, not a sample | nominal_not_measured |
| Ball radius | 0.0335 m | nominal Type 2 midpoint | nominal_not_measured |
| Inertia factor kappa | 0.55 | literature approximation, not an ITF constant | ASSUMED_PRIOR |
| vref | 10 m/s | numerical normalisation design constant | design |
| Feature definitions f1, f2, f3 | log/ratio forms above | implementation proposal | design |
| Runtime default en | 0.74 | carried over from the previous restitution value | ASSUMED_PRIOR, UNCALIBRATED |
| Runtime default mu_eff | 0.1724 | reproduced from the previous tangential speed factor 0.88 at vn = 8 m/s, vt = 20 m/s | ASSUMED_PRIOR, UNCALIBRATED |
| Runtime default beta | 0 | reference model | design |
| Settling speed threshold | 0.02 m/s | below a 20 micrometre rise; numerical policy | design |

Runtime default profile identity: design-unc-v1, revision 1, evidence ASSUMED_PRIOR, calibrationStatus
UNCALIBRATED, validationStatus NOT_RUN, release DEV_ONLY.

## Failure modes and how they are handled

- Structural invalid input (NaN, non-positive mass or radius, unnormalized normal, en or beta outside [0,1],
  non-finite state, negative tolerance): ArgumentException, no result is invented.
- Approach speed inside the tolerance, already separating, tangential grazing, deep initial penetration: a
  named non-impulse status; no impulse is applied and no collision event is created from penetration alone.
- Resolved bounce too weak to lift the ball (below the settle threshold): SETTLED resting contact with the
  policy published in the warning list.
- Non-finite output or an invariant violation beyond tolerance: NUMERICAL_FAILURE with warnings; the engine
  never silently corrects it.
- More than twelve contacts inside one step: SimulationLimitExceeded with a diagnostic, so no unbounded loop.

## Validation performed

- Four documented analytic fixtures recomputed independently and reproduced to below 1e-9 in engine
  coordinates (see docs/BOUNCE_MODEL.md and the bounce scenarios).
- 1200-case sweep (vt, vn, spin, mu, beta): energy non-increase, friction bound, impulse/angular/tangent and
  surface-placement invariants, spin component along the normal unchanged.
- Rotation covariance and mirror handedness of the polar spin vector.
- Runtime integration: exactly one resolution per contact, profile identity in every BallBounced event,
  chunk-independent replay, predictor and flight agreement, deterministic resimulation.
- Legacy path neutrality: 0 numeric differences and 0 non-numeric differences against the preserved
  tennissim-mvp-2 seed-42 candidate replay (585057 leaves), engine tag aside.

## Limits of validity

- Single synthetic ball-surface combination (synthetic-flat) and the uncalibrated runtime default. No court,
  ball batch, temperature, humidity or wear condition is validated.
- The model does not reproduce measured spin behaviour by construction; whether V1 is adequate for real
  measured impacts is UNDETERMINED because no measured data exists here.
- Only one platform (Linux x64) was executed for this model version.


## Update 2026-09-17 - first empirical check

The model has now been evaluated against published measurements (source C2002, seven low-speed bounces with
zero incident spin on wood, emery paper and Rebound Ace). What changed in the status:

- EMPIRICAL_DATA moves from MISSING to LIMITED. The training combination is laboratory emery paper at
  2.1-2.4 m/s, 20 degrees, dry, room temperature. Every court relevant to gameplay stays uncalibrated.
- The normal response is reproduced in-domain to 0.011 m/s against a published component precision of
  0.03 m/s, so en is identified on that surface and the M1/M2 normal model is not contradicted.
- V1's rigid coupling between the tangential impulse and the spin impulse is contradicted by the data: the
  section 12.4 residual |R_L| = |I (omega2 - omega1) - r x (m (v2 - v1))| reaches 3.0 times its uncertainty on
  the in-domain bounce and up to 4.3 times on a held-out surface. A constant tangential restitution
  (beta_grip = 0.0495) improves the joint residual by 17 percent without removing the inconsistency.
- mu_eff remains UPPER_BOUND_ONLY from this dataset because none of the low-speed bounces saturate the friction
  limit; the fitted 0.6 is not a measured friction coefficient.
- MODEL_ADEQUACY stays UNDETERMINED overall, with the spin result pointing at INADEQUATE for simultaneous
  speed and spin reproduction. M4 (finite contact patch, extra torque, moving reaction point) would be the
  next candidate if a decision is needed, and it must be justified by measured data rather than by this
  seven-record sample.

Details and per-record numbers: reports/empirical-validation-cross2002.md.
