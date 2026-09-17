# Realism audit and first correction

This audit keeps Core authoritative and the replay schema at 1.0. `diagnose` reads existing replay states; it never creates an engine or consumes randomness. `scenarios` calls actual Core functions with fixed inputs. No player, shot, gravity, drag, friction or restitution parameter was tuned. `REALISM_CALIBRATED=false`.

## Commands and outputs

From the repository root, select a native SDK through `DOTNET` or PATH. Linux here used `.tools/dotnet/dotnet`; macOS must use its own native SDK.

```bash
"${DOTNET:-dotnet}" build TennisSim.sln --nologo
"${DOTNET:-dotnet}" run --project src/TennisSim.Cli --no-build -- \
  diagnose --input artifacts/sample-42.json --out artifacts/new-audit.json --source-id PRODUCING_SOURCE_ID
"${DOTNET:-dotnet}" run --project src/TennisSim.Cli --no-build -- \
  scenarios --out artifacts/new-scenarios.json
python3 scripts/run-calibration.py --out artifacts/new-development \
  --seeds 0,1,2,3,4,5,6,7,8,9 --source-id PRODUCING_SOURCE_ID
```

`diagnose` requires a new output sidecar, refuses direct input overwrite and an existing output. Exit 0 = no checked invariant failure, **not realism verified**; 1 = measured invariant failure; 2 = input error. `scenarios` exits 1 for correctness failures; conditional reference mismatch stays visible in JSON/stdout but does not fail mathematical regression. CLI `match` defaults are unchanged. `--source-id` names the producer, not the analyzer; a replay cannot prove its own Git provenance. Missing producer identity is explicitly UNKNOWN. Replay and config SHA-256 are in every report and issue.

The runner uses Python 3 standard library, native .NET and Bash only. It starts sequential processes, records commands/exit codes/environment/hashes, enforces a default 1800s batch ceiling, preserves PARTIAL status on interruption, refuses an existing output directory, and retains first-seed examples plus any diagnostic failure. Other full replays are discarded only after their input, hash and per-match metric summary are saved. `--cli` selects a preserved producing engine, and `--diagnostic-cli` selects the common analyzer for before/after comparison. No full replay bulk is committed.

## Measurement contract

All lengths are metres, simulation times seconds; velocities m/s; accelerations m/s². Event sequence breaks same-time ties. Point numbers begin at 1; sequence begins at 0. Metadata is a sidecar; the original JSON is not changed. Metrics contain definition, unit, quality, sample count, eligible count, exclusion reasons/counts, valid ratio, mean, nearest-rank p50/p90/p99, minimum and maximum. Empty means/percentiles/ratios are null, never zero. An empty invariant check is NOT_MEASURABLE. Descriptive distributions use NOT_APPLICABLE for pass/fail because no empirical target exists.

| Measurement | Method / denominator / limits |
|---|---|
| Launch speed | Actual `BallHit.State.Ball.Velocity` magnitude. Serve, Return, Groundstroke separated. All launched attempts, including fault/let serves. Never a secant across a bounce/hit. |
| Flight | Time from BallHit to same-action first BallBounced, including any net contact. Missing first bounce excluded, not assigned zero. |
| Bounce speeds | Collision Before/State velocity magnitudes; initiating shot kind also reported. Event height, sign, passive kinetic energy and exact action/time duplicate checks. |
| Apex | Maximum recorded centre height from hit to first bounce (or recorded termination): ESTIMATED_LOWER_BOUND. Sparse samples do not determine the exact apex. |
| Net crossing | Frames plus event Before/State split impulses. Same time: frame order then event sequence; next interval approaches first Before. Interpolate Z=0 in each continuous crossing segment, report X/Y/time and `Y−.0335−NetHeight(X)`. ESTIMATED. Outside net span is excluded. Negative clearance is not itself a failure: the ball can hit the net. |
| Net interpolation error | `(g+drag*vmax)*gap²/8*(1+.156/5.029)+1e−8` metres, from the acceleration bound and height-profile slope. vmax is endpoint speed maximum on the free-flight segment. Net collision events themselves remain original recorded sub-tick observations; interpolation is not called exact. |
| Player distance/speed | Frame intervals continuously in Rally only. Chord distance is a lower bound; sum metric values for observed in-play distance. Recorded endpoint speed is observed. No capsule-centre test. Exclude intervals touching point end, reposition, fault/let or non-rally phase. Exclusions intentionally also omit short valid portions at boundaries. |
| Acceleration / direction | Delta frame velocity divided by positive dt is an interval mean, ESTIMATED; it cannot establish peak instantaneous acceleration. Nonzero velocity-vector angle in degrees; zero-speed directions excluded. Frame-only method avoids treating Core's interpolated sub-tick player event state as an independent velocity observation. |
| Position discontinuity | Eligible interval chord must not exceed profile maximum speed × dt + numerical allowance. Resets/point boundaries are excluded, dt=0 recorded as excluded. This detects excess displacement; it cannot prove absence of between-frame excursions. |
| Contact | Actual hit time, player, feet, centre XYZ, outgoing velocity. Non-serve margin `.95−horizontal distance`, plus minimum margin to centre-height bounds. Serves deliberately excluded from rally reach/height check. Tick contact resolution ≤1/120s; exact physical racket impact time NOT_MEASURABLE. |
| Rally length | Core successful rally-hit count: legal serve included, failed serve/let attempts excluded; zero on double fault. Not raw BallHit count. |
| Point time | Sum serve-launch to fault/let/end intervals for completed points. Includes fault/let flight; excludes scripted .6s preparation and retry waiting. |
| Serve rate | Legal/ALL launched attempts and legal/(attempts−lets), separately. Counts attempts, legal, faults, lets, double faults. No fabricated ace/winner/unforced-error labels. |
| Tactics | Same-action intended target versus actual first landing, including out/fault/let; X, Z and horizontal error distributions. Shot categories separated. World X is not a backhand classifier. Existing policy comparison measures candidate choice, not proven performance improvement. |

Observed numeric checks use 1e−8m position, 1e−8m/s velocity and 1e−7m/s² finite-difference allowances (double timestamp cancellation). Bounce energy uses 1e−7J/kg. These are numerical allowances, not step-size allowances added to permissible reach. Hit decisions must obey their own tick-end gate. Net estimates have their separate sampling error bound. Sparse replay cannot recover unsampled tunnelling, exact acceleration peaks, mesh dynamics or physical racket collision times.

Issue records include code/category/severity, engine/source/config identity, seed, point/sequence/time, observed value, allowed upper bound, units/quality/tolerance, positions/velocities, replay hash/path, reproduction command and seek anchor. A frame issue's sequence anchors the preceding domain event; its simulationTime identifies the measured frame. Scenario failures have fixed input/independent expectation rather than invented match event coordinates.

## Reproducible first correction

`scenarios` calls `Court.SinglesIn` and `Court.ServiceIn` at both ends and both outside corners. The centre is offset `0.8R=.0268m` beyond each of two adjoining edges. Its nearest distance is `sqrt(2)*.0268=.037900923m`, greater than `R=.0335m` by `.004400923m`. Before: **8/8 falsely IN**. After: **0/8 falsely IN**. Axis tangency remains IN; 1µm beyond tangent remains OUT.

Cause: separate axis bounds produced a square expansion, admitting points beyond the assumed circular footprint. Fix: squared Euclidean distance to the legal rectangle. This is a **CORRECTNESS_BUG relative to the existing finite-radius footprint assumption**; it is not an empirical model of deforming ball contact. Two old assertions explicitly expected diagonal square corners IN. They were changed to OUT with an additional straight-edge tangency assertion; no tests were removed or bulk score fixtures replaced.

Priority: a deterministic geometry contradiction has stronger corrective evidence than a guessed athlete parameter target. No out-of-reach successful match hit was observed. The default drag drop reference discrepancy remains **INSUFFICIENT_EVIDENCE for tuning** because the operational surface is unspecified. It is retained in scenario results, not hidden by changing parameters.

The new behavior uses `tennissim-mvp-2`. Loader explicitly supports v1 and v2 with the same display coordinate/schema contract. Current resimulate rejects v1 with an old-engine message; use the preserved v1 binary for v1 resimulation. A v1 file displaying successfully does not prove v1 engine reproduction by v2.

## Fixed scenarios and evidence limits

Executed: court dimensions, net centre/support, line tangency/corners, diagonal services, no-drag analytic parabola, positive-drag passive energy, drop at dt 1/60/120/240, bounce height/sign, high-speed net crossing, target/stop/reversal speed and acceleration, immediately reachable contact, 1µm-outside contact rejection, unreachable prediction, movement end reflection, and server alternation. Existing tests cover hand/end transforms, rules, RNG, contact preparation and fault/let stress.

No-drag 2.54m-bottom drop: fall time about .719610269422s, rebound bottom **1.390904m** at all three dt values; the limited Type 2 range passes. Default drag .08 gives sampled rebound **1.302080430813m**, below 1.35m even allowing ≤.0000852m apex sampling error. This is not a reason to silently remove operational drag. Ball and surface effects remain combined; material conditioning, court pace and external athlete statistics are not validated.

See [CALIBRATION.md](CALIBRATION.md) for this run's source IDs, numerical comparison, validation commands, current status and exact Mac follow-up. See [CALIBRATION_REFERENCES.md](CALIBRATION_REFERENCES.md) for source applicability.
