# Reproducibility report - V1 bounce model

Prepared 2026-09-15 on Linux x64, .NET SDK 10.0.401 / runtime 10.0.12.

## Classifications used

- SAME_PLATFORM_REPEATABILITY: same binary, same input, repeated execution.
- CROSS_PLATFORM_BITWISE_REPRODUCIBILITY: identical bytes on two platforms.
- CROSS_PLATFORM_NUMERICAL_EQUIVALENCE: abs(a-b) <= atol + rtol max(abs(a),abs(b)) per field.
- CROSS_PLATFORM_EVENT_EQUIVALENCE: same event kinds, order, decisions and RNG state.
- NOT_TESTED: no execution on the second platform.

## Same platform (Linux x64) - PASS

| Check | Command | Result |
|---|---|---|
| Whole-record repeatability | resimulate --input v3-legacy-42.json --chunk 137 | RESIMULATION_IDENTICAL=true |
| Whole-record repeatability | resimulate --input v3-impulse-42.json --chunk 137 | RESIMULATION_IDENTICAL=true |
| Tick chunk independence | core tests, chunk 1 / 137 / 4096 | identical serialised records |
| Legacy numeric identity | v3 legacy seed-42 replay versus the preserved mvp-2 candidate replay | 585057 leaves, 0 numeric differences, 0 non-numeric differences, only the engine tag differs |
| Bounce model determinism | four documented fixtures | identical to below 1e-9 across runs |
| Fit determinism | two fits on the same dataset and configuration | identical parameters, losses and profile hash |
| Scenario set | CLI scenarios | 44 PASS, 1 preserved pre-existing FAIL (default-drag drop reference) |
| Core tests | tests/TennisSim.Tests | 57 passed, 0 failed |
| Independent points | 200 seeds, legacy and impulse | 200 / 200 completed, 0 failures in both models |
| Viewer data checks | v1 golden, v3 legacy, v3 impulse replays | 14 / 14 passed in each |

The impulse-model seed-42 set completed with 28 points and 2289 events; the legacy set has 27 points and 2309
events. A physics-version change is expected to change outcomes; the two versions are not required to agree.

## Cross platform - NOT_TESTED for this model version

No second platform was executed for tennissim-mvp-3. The earlier Linux/Mac comparison covers mvp-2 only and is
reported in docs/CALIBRATION.md: outcome exact, semantic within tolerance, bitwise FAIL, first divergence
attributed to platform math on an exponential path. That result does not transfer to v3 and is not claimed
here.

Numbers that a cross-platform run must compare are already fixed and independent of the platform:

- Replay: final score, per-event kinds and order, finalRandomState, and per-field numeric deltas.
- Bounce model: the four fixtures, the 1200-case invariant sweep, the table-versus-analytic comparison and the
  session-bootstrap parameters, all of which are pure functions of their inputs.
- Calibration: dataset hash, manifest hash, split hashes and the exported profile hash, which are content
  hashes and must match byte for byte on any platform.

## Reproducing this evidence

    dotnet build TennisSim.sln --nologo
    dotnet run --project tests/TennisSim.Tests --no-build
    dotnet run --project src/TennisSim.Cli --no-build -- scenarios --out artifacts/scenarios.json
    dotnet run --project src/TennisSim.Cli --no-build -- match --seed 42 --quiet --out artifacts/v3-legacy-42.json
    dotnet run --project src/TennisSim.Cli --no-build -- match --seed 42 --surface-model impulse --quiet --out artifacts/v3-impulse-42.json
    dotnet run --project src/TennisSim.Cli --no-build -- resimulate --input artifacts/v3-impulse-42.json --chunk 137 --out artifacts/v3-impulse-resim.json
    dotnet run --project src/TennisSim.Cli --no-build -- points --count 200 --seed 100 --surface-model impulse --out artifacts/points-impulse-200.json
    dotnet build tests/TennisSim.ViewerChecks --nologo
    dotnet run --project tests/TennisSim.ViewerChecks --no-build -- artifacts/v3-impulse-42.json --general
    dotnet run --project src/TennisSim.Calibration --no-build -- validate-data --dataset data/bounce/manifest.json
    dotnet run --project src/TennisSim.Calibration --no-build -- fit --dataset data/bounce/manifest.json --config calibration/fit-config.json --models M1,M2 --synthetic --out artifacts/calibration/run-001
    dotnet run --project src/TennisSim.Calibration --no-build -- evaluate --run artifacts/calibration/run-001 --split test
    dotnet run --project src/TennisSim.Calibration --no-build -- virtual-itf --profile artifacts/calibration/run-001/profile.json --temperature-c 23

The synthetic dataset is regenerated deterministically by:

    dotnet run --project src/TennisSim.Calibration --no-build -- synthetic --out data/bounce/synthetic --count 240 --seed 20260915 --en 0.83 --mu 0.35 --beta 0

## Unverified in this run

- Unity Editor compile, EditMode and PlayMode tests: NOT_RUN (no Unity on this host). Only the shared data code
  that the Unity tests also compile was executed, through tests/TennisSim.ViewerChecks.
- Candidate visual review, animation, racket and foot contact: NOT_RUN.
- Cross-platform v3 execution: NOT_TESTED.
