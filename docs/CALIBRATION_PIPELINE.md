# Calibration pipeline

Tool: src/TennisSim.Calibration (assembly name tennis-calibrate), net10.0, referencing TennisSim.Core so the
fitted model and the runtime model are the same code.

    dotnet run --project src/TennisSim.Calibration -- <command> [options]

| Command | Purpose |
|---|---|
| validate-data --dataset <manifest.json> [--out report.json] | load, validate, classify evidence, report splits |
| synthetic --out <dir> [--count --seed --sessions --en --mu --beta --velocity-noise --angular-noise] | write a labelled SYNTHETIC dataset plus its generation record |
| fit --dataset <manifest.json> --config <fit-config.json> --out <dir> [--models M1,M2,M3] [--synthetic] [--approve] [--strict] | staged fit, model selection, run record, profile export gate |
| evaluate --run <dir> --split test|validation | metrics and pre-registered target gate for an existing run |
| virtual-itf --profile <profile.json> [--ball --temperature-c --spin-rev-per-s] | CPR-shaped virtual test from the profile |
| export-profile --run <dir> --out <profile.json> [--table-nodes N] | export with an optional lookup table |
| compare-runtime --profile <profile.json> [--table-nodes N --probes N --en-tolerance --mu-relative-tolerance --beta-tolerance] | table versus analytic prediction error |

Exit codes: 0 ok, 1 target or gate failure, 2 input error, 3 data insufficient or no releasable profile,
4 comparison tolerance exceeded.

## Data contract

data/bounce/manifest.json lists sources (URL/DOI, access date, licence, redistribution permission, units,
extraction notes, reviewer), dataset entries (evidence class, record file, ball/surface binding, group key)
and the split assignment. Records are JSONL, one object per line, per section 8.2 of the instruction document.
data/bounce/README.md documents how to add measured data; schemas/ publishes the record and profile schemas.

Evidence classes are preserved end to end: MEASURED_RAW, PUBLISHED_MEASUREMENT, DIGITIZED_MEASUREMENT,
NORMATIVE_CONSTRAINT, SYNTHETIC, ASSUMED_PRIOR. A record whose incident spin is not measured is treated as
unknown, kept for the normal response only, and never as a zero-spin observation.

## Stages

- **Stage A - measurement frame.** Units, axes, sign, normal normalization, temporal ordering and ball spec
  identity are validated; a record that contradicts its dataset evidence class or source id is rejected. The
  loaders are strict: unknown JSON members, missing sessions, contradictory dataset ids and non-finite numbers
  are rejected rather than defaulted.
- **Stage B - normal response.** Only records with a complete outgoing velocity observation and
  abs(dot(v_before, n)) >= normalApproachFloorMS are used; only the outgoing normal speed residual enters the
  loss. Spin-dependent information is excluded here.
- **Stage C - friction and tangential response.** Records with a measured incident spin are used, with
  residuals on the tangential velocity error and, when observed, on the outgoing spin vector.
- **Stage D - joint refinement.** All free parameters together, then validation-based selection across
  candidate models with the pre-registered complexity gate validationRmseGainForComplexity.
- **Stage E - separated validation and uncertainty.** Splits are fixed by group (session or experiment) before
  fitting and are hashed into the run record; groups are never split across sets. Uncertainty is a
  session-cluster bootstrap with a recorded seed and resample count.

Optimisation is a bounded pattern search: fixed starts, no RNG, maxEvaluations, initialStep, shrink, stopStep.
Residuals are normalised by the per-record observation standard deviations, transformed with a Huber loss
(delta configurable, default 1.5) and optionally regularised towards the design prior. Velocity and
angular-velocity errors are never added without normalisation. Correlated measurement covariance is not
implemented: only the diagonal case is supported, and this is stated in the report.

Parameter identification is reported, not assumed: a parameter whose contact mode never binds is marked
LOWER_BOUND_ONLY or UPPER_BOUND_ONLY, a parameter at a bound is marked AT_BOUNDARY, an unobserved spin output
yields NOT_IDENTIFIED for beta, and a degenerate bootstrap spread is published as NOT_AVAILABLE instead of a
zero standard error.

## Evaluation metrics and gates

Normal outgoing speed RMSE (m/s), tangential outgoing velocity vector RMSE (m/s), outgoing angular velocity
RMSE (rad/s and rpm equivalent), exit angle RMSE (degrees, atan2(dot(v,n), abs(P v)), excluded below a
pre-registered tangential speed floor), P95 absolute values, signed normal bias, per-surface and per-session
breakdowns, angular observation coverage and the number of angle exclusions.

Pre-registered targets (calibration/fit-config.json): 0.5 m/s normal, 1.0 m/s tangential, 300 rpm angular,
2 degrees exit angle, 2 CPR points of spread. Targets gate only measured evidence. A synthetic evaluation is
reported as descriptive, with an explicit note that targets are not gated.

## Release gate

fit without any measured record exits 3 (DATA_INSUFFICIENT) and writes only a refusal run record; no profile
and no coefficient are produced. With labelled synthetic evidence and the explicit --synthetic flag the
profile is exported as SYNTHETIC / FITTED_SYNTHETIC_ONLY / DEV_ONLY. With measured evidence the profile is
CALIBRATED_IN_DOMAIN only when validation passed, and its release stays BLOCKED unless --approve is supplied.
export-profile and the runtime CLI print an explicit warning whenever a profile is not approved for product
use.

## Virtual ITF-shaped test

virtual-itf reproduces the CS 01/02 incidence conditions (30 m/s at 16 degrees, spin up to 3 rev/s) against a
profile and computes e_test, mu_test, the 23 degrees C correction e_23 and the raw CPR. It is not the official
procedure: surface conditioning, the full measurement protocol and any certification claim are absent, no
classification band is asserted, and the raw unrounded CPR is preserved. A synthetic profile makes this
command exit 1 so it cannot be mistaken for a measurement.


## Update 2026-09-17

- Data subsets: validate-data, fit and evaluate accept --datasets id,id. The loaded subset is what gets hashed,
  and evaluate refuses to run when the manifest no longer hashes to the run's dataset hash.
- Candidates: M1 (constant en, mu, beta = 0), M1B (constant en, mu, beta), M2 (state dependent en), M3 (state
  dependent en and mu with beta). Model selection and the complexity gate use the joint normalised residual over
  normal, tangential and angular residuals, so a model that only improves the normal component is not promoted.
- Observation masks are axis aligned and are honoured end to end: an unseen component contributes no residual
  and no sigma weight, and a record without a measured contact position is still usable when the normal is
  measured.
- evaluate --per-record prints observed and predicted normal speed, tangential speed and spin for every record.
  Small published datasets must be reported this way; an average hides a structural failure.
- Every evaluation reports the section 12.4 angular impulse residual R_L = I (omega2 - omega1) -
  r x (m (v2 - v1)) with its propagated uncertainty, computed from the observations alone. A ratio above about 1
  means the rigid tangential coupling of V1 is contradicted by that record.
- EMPIRICAL_DATA classification: MISSING with no measured record, LIMITED below 25 measured records or below
  3 independent measured groups, otherwise SUFFICIENT for this tool. Sample size never establishes a domain.
