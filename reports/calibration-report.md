# Calibration report - V1 bounce model

Prepared 2026-09-15. Evidence: LABELLED SYNTHETIC ONLY. No measured impact record exists in this repository,
so EMPIRICAL_DATA is MISSING and no empirical calibration claim is made.

## Dataset

- data/bounce/manifest.json: six sources (ITF 2026 technical booklet, ITF 2026 Rules appendix, ITF ball approval
  tests, Cross 2003, Cross 2002, and the synthetic generator) with access dates, licences and redistribution
  flags. Only the synthetic entry carries records.
- data/bounce/synthetic/records.jsonl: 240 records, generator seed 20260915, truth en = 0.83, mu = 0.35,
  beta = 0, velocity noise 0.25 m/s, angular noise 15 rad/s, 12 sessions, 70 percent of records with measured
  incident spin, 60 percent of those with observed outgoing spin.
- Splits fixed before fitting: 8 train sessions (160 records), 2 validation (40), 2 test (40).
- valid-data output: EMPIRICAL_DATA MISSING; 73 records have no measured incident spin and are usable for the
  normal response only.

## Fit result

Command: tennis-calibrate fit --dataset data/bounce/manifest.json --config calibration/fit-config.json
--models M1,M2 --synthetic --out artifacts/calibration/final/run-001

| Candidate | Parameters | Train loss | Validation loss | Train normalised RMSE | Validation normalised RMSE | Evaluations | Selected |
|---|---|---:|---:|---:|---:|---:|---|
| M1 | 2 | 28.4569 | 10.8893 | 0.57208 | 0.42801 | 227 | yes |
| M2 | 4 | 28.4343 | 10.4544 | 0.69313 | 0.42982 | 31780 (budget cap) | no |

M2 was rejected: its validation normalised RMSE is not better than M1 by the pre-registered 3 percent margin,
and its larger parameter count is not justified. The M2 run exhausted the evaluation budget, which is recorded
rather than hidden.

| Parameter | Estimate | Truth | Status | Bootstrap SE | 5-95 percent |
|---|---:|---:|---|---:|---|
| en | 0.828921 | 0.83 | IDENTIFIED | 0.003709 | 0.816811 - 0.833022 |
| mu_eff | 0.350284 | 0.35 | IDENTIFIED | NOT_AVAILABLE | degenerate (see below) |

Predicted contact modes in the training split: COULOMB_LIMITED 73, TANGENTIAL_TARGET_LIMITED 39, which is why
mu is bracketed and reported as IDENTIFIED. The evaluation domain recorded from the training features is
sn <= 23.26 m/s, st <= 32.55 m/s, su <= 39.99 m/s, and the profile is valid only inside it.

Second independent synthetic realisation (seed 777001, same truth) gives en = 0.826780 and mu = 0.349704, i.e.
an across-realisation spread of 2.1e-3 for en and 5.8e-4 for mu. The session-cluster bootstrap did not move
mu across its 40 resamples, so its bootstrap standard error is published as NOT_AVAILABLE rather than 0; the
generator assigns impacts to sessions round-robin, so every session carries nearly the same impact design and
the bootstrap cannot see design-level variation. This is a limitation of the synthetic dataset design, not an
identification result.

## Hold-out performance (pre-registered targets, descriptive only)

| Metric | Validation | Test | Target | Gated |
|---|---:|---:|---:|---|
| Normal outgoing speed RMSE | 0.12544 m/s | 0.13246 m/s | <= 0.5 m/s | no (synthetic) |
| Tangential outgoing velocity RMSE | 0.59741 m/s | 0.21665 m/s | <= 1.0 m/s | no |
| Outgoing angular velocity RMSE | 164.81 rpm | 135.01 rpm | <= 300 rpm | no |
| Exit angle RMSE | 1.746 deg | 1.083 deg | <= 2.0 deg | no |
| Signed normal bias | -0.0338 m/s | -0.0338 m/s | report | no |
| P95 normal / tangential / angle | 0.212 m/s / 0.407 m/s / 2.075 deg | 0.212 m/s / 0.378 m/s / 1.912 deg | report | no |

Angular coverage: 18 of 40 test records observe outgoing spin, 69 of 160 training records. The angular and
spin conclusions therefore rest on partial observations; this is not a complete 3D spin validation.

## Table export and runtime comparison

export-profile --table-nodes 5 writes a 125-cell multilinear table (profile hash
3d25e813ec51464116cd1b4d77571390ca886925ee55fbad0d35dea24d861ec7). On the constant M1 profile the table and
the analytic evaluator agree exactly, which is expected and carries no information.

A state-dependent example (data/bounce/examples/state-dependent-example.json, ASSUMED_PRIOR, UNCALIBRATED)
makes the comparison meaningful: over 729 probes the maximum deviation is en 0.00340, mu 1.79 percent relative,
velocity 0.0392 m/s, beta unchanged. The pre-registered comparison tolerances (en 0.02, mu 5 percent, beta 0.02)
pass. Matching the analytic evaluator is explicitly not a bitwise reproducibility claim.

## Virtual ITF-shaped test

virtual-itf --profile profile.json --temperature-c 23 on the synthetic M1 profile with 3 rev/s spin returns
e_test = 0.828921, mu_test = 0.350284, raw CPR = 62.1335. Because the CS-shaped impact is fully Coulomb or
target limited on a constant profile, these values reproduce the profile constants themselves: the command
verifies the code path and the formula, and carries no independent information about any real court. The
command exits 1 for synthetic evidence and prints that it is not an ITF certification.

## What is not established

- Whether V1 reproduces real measured speeds and spin. Reason: no measured record.
- Any court pace classification, temperature law, wear or humidity effect.
- Population behaviour of players, rallies or outcomes.

## Next step with real value

Acquire one measured ball-surface combination across incidence conditions (10/20/30 m/s, 10/16/30/45 degrees,
0 and +/- 1500 and 3000 rpm), record it with the schema in schemas/bounce-record.schema.json, fix the split
assignment by session, then run validate-data, fit, evaluate and virtual-itf. Until then every profile in this
repository stays UNCALIBRATED or FITTED_SYNTHETIC_ONLY, and RELEASE stays BLOCKED or DEV_ONLY.
