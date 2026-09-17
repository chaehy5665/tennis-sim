# Empirical validation: published tennis ball bounce measurements (Cross 2002)

Prepared 2026-09-17. Evidence class: PUBLISHED_MEASUREMENT. This is the first empirical evaluation of the V1
bounce model in this repository; it replaces the previous EMPIRICAL_DATA: MISSING status with LIMITED.

## Source and extraction

- Cross, R., "Measurements of the horizontal coefficient of restitution for a superball and a tennis ball",
  Am. J. Phys. 70(5), 482-489 (2002), DOI 10.1119/1.1450571.
- Fetched copy: https://physics.umd.edu/courses/Phys405/Hill/Fall05/Information/AJP/AJP00482.pdf,
  SHA-256 b5cbf25b165491327335d997a418f4c2d242fc694ad077fdb22243e6309ad536, access date 2026-09-17.
- The PDF text layer was read directly (no OCR). Table I (printed p. 486) and Table II (printed p. 487) were
  transcribed by scripts/build-cross2002-records.py. The script recomputes the derived columns the paper also
  prints (vx2, vx2/vx1, ey, R*omega2/vx2) and refuses to write a record when any of them disagrees. All seven
  records passed: for example Wood 20 degrees publishes vx2 = 2.92 against a recomputed 2.918, vx2/vx1 = 0.84
  against 0.835, ey = 0.80 against 0.799, and the Table I bounce publishes R*omega2/vx2 = 1.41 against 1.406
  (which also fixes the paper's radius at 0.033 m).
- Record file: data/bounce/published/cross2002-records.jsonl. Extraction note with hashes, methods and the
  seven checks: data/bounce/published/cross2002-extraction.json.
- The article itself is not redistributed (publisher terms); only the extracted numbers with provenance.

## What the data is

Seven individual low-speed bounces of a tennis ball incident with zero spin:

| Source | Surfaces | Angles | Incident speed | Spin measured | Position |
|---|---|---|---|---|---|
| Table II, 60 cm drop | wood, emery paper, Rebound Ace court | 20 and 45 degrees | 3.35 - 3.80 m/s | yes (rad/s) | no |
| Table I, 25 cm drop | emery paper | 20 degrees | 2.24 m/s | yes, with uncertainty | no |

Table II reports one typical bounce per surface and angle, not an average. Incident spin is zero by
experimental construction (vertical drop, no torque in flight), so it is declared through the mask rather
than implied. Out-of-plane components are not observed and are declared false in the mask. The papers'
uncertainties are used where published; where they are not (Table II), 2 percent for speeds, 1.5 degrees for
angles and 1 percent for spin are applied and flagged per record as inferred from the methods statement and
Table I.

Split assignment was fixed before fitting, by independent group: emery 60 cm drop trains, emery 25 cm drop
validates in-domain, wood and Rebound Ace are held out as different surfaces.

## Fit

Two candidates were fitted on the two emery training bounces: M1 (constant en, constant mu, beta = 0) and M1B
(constant en, constant mu, constant beta). Selection uses the joint normalised residual (normal, tangential
and angular together), not the normal component alone.

| Candidate | Parameters | Train joint | Validation joint | Selected |
|---|---:|---:|---:|---|
| M1 | 2 | 4.4946 | 8.1530 | no |
| M1B | 3 | 3.7768 | 6.7474 | yes |

M1B coefficients: en = 0.8061 (IDENTIFIED), mu_eff = 0.6 (UPPER_BOUND_ONLY), beta_grip = 0.0495 (IDENTIFIED).
Fitted domain: sn <= 2.62 m/s, st <= 3.32 m/s, su <= 3.32 m/s. No bootstrap: the training split has a single
independent group, so parameter uncertainty is NOT_AVAILABLE and that is what run.json records.

mu_eff = 0.6 is an upper bound, not a measurement: every training bounce was tangential-target-limited
(mode histogram: TANGENTIAL_TARGET_LIMITED = 2, COULOMB_LIMITED = 0), so the friction limit never saturated and
the data cannot pin the coefficient from below. It must not be quoted as a measured friction coefficient.

## Results, record by record

In-domain validation (emery paper, 25 cm drop):

| Quantity | Observed | Predicted | Error |
|---|---:|---:|---:|
| normal rebound speed | 0.6100 m/s | 0.6207 m/s | +0.0107 m/s |
| tangential rebound speed | 1.2300 m/s | 1.3180 m/s | +0.0880 m/s |
| rebound spin | 52.40 rad/s | 42.44 rad/s | -9.96 rad/s (95 rpm) |
| exit angle | 26.4 deg | 27.6 deg | +1.16 deg |

Held-out surfaces (transfer, same ball, different ball-surface combinations):

| Record | normal observed / predicted | tangential observed / predicted | spin observed / predicted (rad/s) |
|---|---|---|---|
| wood 20 deg | 1.0162 / 1.0256 | 2.9181 / 2.1939 | 34.90 / 70.65 |
| wood 45 deg | 1.9494 / 2.1659 | 1.6127 / 1.6864 | 58.20 / 54.31 |
| Rebound Ace 20 deg | 0.8459 / 0.9236 | 1.9548 / 1.9757 | 76.70 / 63.63 |
| Rebound Ace 45 deg | 2.0166 / 2.0519 | 1.7346 / 1.5976 | 50.60 / 51.45 |

Aggregate errors: validation normal 0.0107 m/s, tangential 0.088 m/s, spin 95 rpm, angle 1.16 deg. Test normal
0.1165 m/s, tangential 0.371 m/s, spin 183 rpm, angle 3.45 deg, which exceeds the pre-registered 2 degree
target and is reported as a transfer failure, not as an in-domain result.

## Structural diagnostic

The instruction document section 12.4 residual R_L = I (omega2 - omega1) - r x (m (v2 - v1)) uses the
observations alone. V1 requires it to vanish inside the measurement uncertainty.

| Split | records | median abs(R_L) | median / uncertainty |
|---|---:|---:|---:|
| validation (emery) | 1 | 1.85e-4 N s | 3.04 |
| test (wood, Rebound Ace) | 4 | 2.35e-4 N s | 1.94 (max 4.26) |

The rigid tangential coupling between the friction impulse and the spin impulse is therefore violated by
about three times the stated uncertainty on the in-domain bounce, and by up to 4.3 times on a held-out
surface. This is the same physical statement the source paper makes independently: the real bounce stores and
returns energy in the tangential direction, so the centre-of-mass tangential impulse and the spin impulse are
not rigidly linked. The fitted beta_grip = 0.0495 is a small step in that direction and improves the joint
residual by 17 percent, but it does not remove the inconsistency.

## Status after this work

    EMPIRICAL_DATA: LIMITED (7 published low-speed records, 3 laboratory surfaces, no position, no repeats)
    EMPIRICAL_CALIBRATION: PASS_IN_DOMAIN for the normal response of the emery laboratory surface at 2.1-2.4 m/s; FAIL for transfer to the held-out surfaces
    SPIN_VALIDATION: FAIL_IN_DOMAIN (95 rpm on the in-domain bounce, 183 rpm on held-out surfaces, against measured spins of 335-750 rpm)
    MODEL_ADEQUACY: UNDETERMINED, with a structural residual of about 3 sigma in-domain pointing at INADEQUATE for simultaneous speed and spin reproduction
    PROFILE_RELEASE: BLOCKED (measured profile exported with release BLOCKED; --approve was not used)
    IN_DOMAIN: emery laboratory paper, ball incident with zero spin, 2.1-2.4 m/s, 20 degrees, dry, room temperature

## What must not be claimed

- No court is calibrated: the training surface is laboratory emery paper. Wood and Rebound Ace are held-out
  transfer targets, not fitted combinations.
- The fitted coefficients are not usable at match speeds. The fitted domain ends at 2.62 m/s of normal
  incidence speed; every serve and groundstroke in the simulator is far outside it and the runtime reports
  OUT_OF_DOMAIN instead of extrapolating silently.
- mu_eff is an upper bound; beta_grip rests on two training bounces and has no uncertainty estimate.
- The papers' 45 degree data show the tangential behaviour the paper explains with tangential elasticity;
  V1 reproduces the normal component there but not the spin.

## Next step with real value

Digitise or measure the same ball-surface combination at higher incidence speeds (10, 20, 30 m/s) and at 10,
16, 30 and 45 degrees, with repeated bounces and a stated contact position. That is the smallest addition that
would turn this LIMITED status into an in-domain calibration for a real court, and it would let the M3
candidate (state-dependent en and mu with beta) be tested instead of a three-parameter fit on two records.
