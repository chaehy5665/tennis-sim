# Calibration execution record — 2026-09-14

One geometry correctness defect was corrected: square expansion at court/service-box corners admitted a circular footprint that could not touch the court. No physical or athlete parameter was changed. The reference mismatch in the default-drag drop scenario remains visible. This is a realism audit and first correction, **not empirical realism calibration**.

## Baseline and source provenance

- Baseline HEAD: `84b367233a96a12efe4fd7fcad265e5f6d33fdb8`; initial `git status --short` empty.
- Environment: Linux x86_64, .NET SDK 10.0.401/runtime 10.0.12, Core netstandard2.1, CLI/tests net10.0. SDK/OS details are in `artifacts/calibration/20260914-audit/manifest.json`.
- Executed candidate snapshot: `38dddf1118f0686c134cfc78fb599ff7ef93654e1f829593e10326e53e580113` (SHA-256 over sorted per-file source hashes; `candidate-source-hashes.json`). Actual source is copied in `candidate-executed-source/`; diff is `candidate-executed.diff`.
- A subsequent diagnostic **input-validation-only** change rejects missing observations instead of accepting DTO defaults. Its final source identity and final tests are separately captured in `manifest.json`; engine physics/geometry was frozen before held-out evaluation. Final-match bytes are compared to the frozen candidate.
- Original sample SHA-256: `a7b086639798233d44fbde48f2529ba38182fd7b2bd5dd50dbca748431d18f60`. No original sample or prior validation evidence was replaced.
- Candidate sample SHA-256: `fcc0c8e0db7c3618789e0ab10300a0c611d6feb1565b130640d5ab21c9ca2958`. Engine v2 changes the file hash; for seed 42 the only JSON change is `engineVersion`.
- Saved full inputs/configs, per-file source hashes, DLL hashes, replay hashes, commands, exit codes and run budgets are in the run directory. HEAD alone is not claimed to identify the dirty candidate. No commit, push, stash, package upgrade or global setting change.

The Linux checkout has neither Unity `ProjectSettings` nor `Packages`. The reported Mac path `/Users/c10/Projects/tennis-sim` is not accessible here. Reported Unity 6000.6.0f1, Test Framework request 1.8.0 and prior 3/1 Unity test counts remain **user-reported history**, not this run's results. The package lock's resolved version is UNKNOWN. Existing docs containing older environment claims were preserved and supplemented.

## Measurement and before/after

Final paired experiments use development seeds **0–9** and held-out seeds **1000–1009**, fixed before selection. Each split has default baseline-vs-defender and symmetric baseline-vs-baseline, each with FirstServer 0/1 × InitialEndA −1/+1: **80 matches per split per engine**. Budget: sequential execution, 1800 seconds maximum per batch, no silent incomplete success. An earlier 80-match development diagnostic pass was repeated after extending measurements; it is preserved as `baseline-dev/`, excluded from the final paired count.

The held-out set is evaluated after the one fixed corner correction; it is not used for tuning. Per-match distributions and exclusions are in `metrics.csv`, summaries in `baseline-summary.json`, `candidate-summary.json` and `comparison.json`. Server/end cells and seed clusters are reported separately; no point/shot independence or population confidence interval is asserted. TARGET_POPULATION=UNSPECIFIED.

| Scope | Baseline | Candidate |
|---|---:|---:|
| Circular-footprint corner false IN, controlled cases | 8/8 | 0/8 |
| Seed 42 points / events / frames | 27 / 2309 / 6118 | 27 / 2309 / 6118 |
| Seed 42 score | B 6–0 | B 6–0 |
| Seed 42 mean legal rally length / max | 19.8148 / 84 hits | same |
| Seed 42 mean in-play point duration | 22.0483s | same |
| Seed 42 serve / return / groundstroke mean launch speed | 23.4780 / 22.6964 / 22.6099m/s | same |
| Seed 42 measured non-serve reach violations | 0 / 508 | 0 / 508 |
| Seed 42 bounce checks | 557 events | same |
| Seed 42 eligible movement intervals, each player | 5923 / 6117 (194 excluded) | same |
| No-drag 2.54m-bottom drop rebound, dt 1/60, 1/120, 1/240 | 1.390904m | same |
| Default-drag sampled drop rebound | 1.302080430813m | same; below conditional reference |
| Independent points | 1000 complete / 0 failures / 22256 shots | same |
| Tactical comparison, 5 seeds × 4 policies | 20 completed | same; wins A 3/5, 5/5, 0/5, 1/5 |

Improved: exact corner geometry under the finite circular-footprint assumption. Unchanged: ordinary match metrics and the conditional drop mismatch. Worsened: none observed in executed paired summaries. Unmeasured: empirical athlete fit, actual contact patch, high-frequency movement peaks, real racket collision and candidate visual naturalness. Cross-platform evidence is limited to the later seed 42 Linux/Mac comparison; broader seed/platform generalization is unmeasured. This sparse match sample does not demonstrate the corner error's frequency in real tennis.

The correction is reproduced with `scenarios`, not by pretending seed 42 contains a corner failure. A fixed corner has no match point/event/seek coordinate; this is explicitly NOT_APPLICABLE. Representative seed 42 replay contact/bounce anchors below verify display and observation, not a fabricated visual manifestation of the fixed corner.

## Validation commands

These were executed in Linux with `D=.tools/dotnet/dotnet`. See manifest/logs for exact argv, outputs and codes. Tests are executables, **not `dotnet test`**.

```bash
D=.tools/dotnet/dotnet
"$D" build TennisSim.sln --nologo
"$D" run --project tests/TennisSim.Tests --no-build
"$D" build tests/TennisSim.ViewerChecks --nologo
"$D" run --project tests/TennisSim.ViewerChecks --no-build -- artifacts/sample-42.json
"$D" run --project tests/TennisSim.ViewerChecks --no-build -- \
  artifacts/calibration/20260914-audit/replays/candidate-42.json --general
"$D" run --project src/TennisSim.Cli --no-build -- scenarios \
  --out artifacts/NEW-scenarios.json
"$D" run --project src/TennisSim.Cli --no-build -- resimulate \
  --input artifacts/calibration/20260914-audit/replays/candidate-42.json \
  --chunk 137 --out artifacts/NEW-resimulated-42.json
"$D" run --project src/TennisSim.Cli --no-build -- points \
  --count 1000 --seed 100 --out artifacts/NEW-points.json
"$D" run --project src/TennisSim.Cli --no-build -- compare \
  --seeds 11,22,33,44,55 --out artifacts/NEW-comparison.json
```

`NEW-*` are safe rerun destinations; original executed paths are in the manifest. Repeated chunk 1/137/4096 whole-record checks are also in Core tests. Observation-only seed42 before the geometry edit was byte-identical to baseline, including all events and final RNG state. Baseline's preserved engine resimulation and candidate's own resimulation both pass; current v2 rejecting a v1 resimulation is an intentional version boundary.

Shared viewer checks traverse all sample frame times, collision boundaries, resets, sequence events, controller seek/speeds and complete event traversal; they are not a validation of every unused JSON meaning. `--general` replaces only the fixture-specific score/count assertion with recorded terminal consistency. Golden v1 fixture checks remain. Unity candidate tests additionally load the specified file and traverse it through actual Transforms when run on Mac. The current headless Unity attempt exited 2 before starting an Editor.

## Exact Mac follow-up

Bring the changed source and the **two preserved files** to the Mac checkout using the user's normal transfer workflow; do not overwrite Mac project settings/packages. No automated synchronization was performed here. In the Mac repository root, use a native .NET SDK on PATH or set DOTNET. Check `git status`, `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json` and `Packages/packages-lock.json` first; record requested versus resolved Test Framework version.

```bash
# Files must already exist at these relative paths; hashes are verified by preparation.
scripts/prepare-unity-replay.sh \
  artifacts/calibration/20260914-audit/replays/baseline-42.json audit-baseline-42.json
scripts/prepare-unity-replay.sh \
  artifacts/calibration/20260914-audit/replays/candidate-42.json audit-candidate-42.json

# If the locally recorded Editor version is 6000.6.0f1, this is the conventional Hub path.
# Use the actual recorded/installed version; this command does not install or upgrade it.
export UNITY_EDITOR="/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity"
export TENNISSIM_CANDIDATE_REPLAY="$PWD/unity/TennisSim.UnityViewer/Assets/StreamingAssets/Replays/audit-candidate-42.json"
scripts/validate-unity-viewer.sh all

# Separate native Mac resimulation; hash compare after this is cross-OS execution evidence.
"${DOTNET:-dotnet}" run --project src/TennisSim.Cli -- resimulate \
  --input artifacts/calibration/20260914-audit/replays/candidate-42.json \
  --chunk 137 --out artifacts/mac-candidate-resimulated-42.json
```

Expected copied file hashes are the full baseline/candidate hashes above. Copy hash equality alone is **not** cross-platform determinism. Unity tests without the candidate environment variable are skipped and the XML validator refuses to call that complete. No candidate Unity test pass is inferred from .NET tests or the user's previous screen confirmation.

In the existing HUD, load `audit-baseline-42.json` and `audit-candidate-42.json` separately. Check full playback to **611.625s**, **27 points**, recorded **B 6–0**; pause, restart, .25x/1x/2x, forward/backward event navigation and seek. Specific seed42 point1 anchors: serve **sequence 4, .600s**, first bounce **sequence 6, 1.397604755491s**, return **sequence 8, 1.575s**. Use event navigation for exact time; the slider is approximate. Check ground-centre height .0335m at bounce and recorded contacts; toggle enlarged ball without moving its centre. No diagnostic overlay or invented trajectory is added. Inspect the fixed corner via the independent scenario result, not by expecting it in this match.

Candidate EditMode/PlayMode, full playback/controls, matching final score and human screen assessment must be recorded as a **new** result. Remaining order: native Mac source/settings check → hash-verified replay preparation → Editor compile/EditMode/PlayMode → screen review → native Mac resimulation comparison.

## Cross-platform seed 42 result

The Linux candidate was copied with its expected SHA-256 and resimulated on Mac from the preserved `tennissim-mvp-2` candidate source. The returned Mac replay has SHA-256 `9c947f201228d9700d98f8f1d1f8e301b8a5b57829ba98819f7d35abae485690`; it is not byte-identical to the Linux hash `fcc0c8e0db7c3618789e0ab10300a0c611d6feb1565b130640d5ab21c9ca2958`, and the CLI reported `RESIMULATION_IDENTICAL=false`.

Direct tree comparison found exact input, event skeleton/order, final RNG state and terminal B 6–0 after 27 points. All 117,610 differing leaves are numeric; there are zero non-numeric leaf differences. The maximum absolute numeric delta is `6.944999436653276e-10`. The first difference is event sequence 39 `BallBounced`, immediately after an exact event 38, on the `Math.Exp`-using collision path. Platform math is the likely cause but remains an inference without a runtime-level trace. The single-run semantic result passes the tolerances in [DETERMINISM.md](DETERMINISM.md); bitwise reproducibility fails. Mac SDK/runtime, project hashes and full source-hash verification were not retained with this run, so build identity evidence is partial.

## Final paired results and status

All **320 final-batch matches** completed (160 matched before/after pairs). All 160 pairs have identical match/metric/check summaries, with zero measured match invariant failures. For the held-out 80 pairs, the runner additionally recorded hashes normalized only for the engine-version string: **80/80 identical**. Development full-record normalized hashes were not collected for all rows; only their summaries and retained representative records are compared. Seed42 normalized bytes are independently identical. No uncollected development hash check is claimed.

Per engine, existing profiles give A wins **0/40 development and 0/40 held-out**; symmetric profiles give **20/40 and 20/40**. Symmetric per-cell A wins are development 4,3,7,6 /10 and held-out 3,5,5,7 /10 in order (server0,end−1),(server0,end+1),(server1,end−1),(server1,end+1). These are descriptive results of fixed paired configurations, not evidence of a population-calibrated 50% target. Per engine: development **3813 points**, held-out **3768 points**. Geometry correction did not change those distributions.

Final source identity: `50ee7c31f33ada2000b63bfb5b58d84befc866d543ee6be7c8fcca0a781477df`, defined by `final-source-hashes.json`. The final parser guard version rebuilt with **0 warnings/errors**, **36/36 Core tests**; final seed42 bytes and measured summaries match the executed candidate. Viewer source checks remain **14/14 original + 14/14 candidate**. Scenarios: **27/28 PASS**, with the one conditional default-drag reference mismatch retained; all mathematical/correctness scenarios pass. No new parameter search was performed.

```text
IMPLEMENTATION_STATUS: COMPLETE
SCOPE: REALISM_AUDIT_AND_FIRST_CORRECTION
BASELINE_SOURCE_ID: 84b367233a96a12efe4fd7fcad265e5f6d33fdb8
CANDIDATE_SOURCE_ID: 50ee7c31f33ada2000b63bfb5b58d84befc866d543ee6be7c8fcca0a781477df
DIAGNOSTICS_RESULT: 320 final-batch matches, 0 measured match invariant failures
MEASUREMENT_COVERAGE: observed impulses/contacts; sampled movement/apex/net; instantaneous acceleration and physical racket impact NOT_MEASURABLE
PHYSICS_SCENARIO_RESULT: correctness PASS; conditional default-drag reference below range
MOVEMENT_SCENARIO_RESULT: PASS for executed internal-limit and reach scenarios
DETERMINISM_RESULT: PASS on Linux; observation unchanged; repeated candidate bytes identical
CROSS_PLATFORM_REPRODUCIBILITY: seed42 outcome PASS; semantic PASS_WITH_TOLERANCE; bitwise FAIL; broader generalization NOT_ESTABLISHED
REGRESSION_RESULT: PASS; Core36/36, shared14/14 each, 1000 points each, 20 tactical matches each
ISSUES_CONFIRMED: circular-footprint corner false-IN correctness bug; separate conditional drop reference gap
ISSUES_FIXED: 1 kind, 8/8 fixed corner cases corrected
PARAMETERS_CHANGED: NONE
HELD_OUT_VALIDATION_RESULT: PASS, 80 pairs; summaries and normalized full replay hashes identical
REFERENCE_CHECKS_PASSED: court/net dimensions; limited drag=0 2.54m-bottom rebound1.390904m at dt1/60,1/120,1/240
EMPIRICAL_CALIBRATION_STATUS: NOT_ATTEMPTED
UNITY_CANDIDATE_TEST_RESULT: shared-data PASS14/14; actual Editor/EditMode/PlayMode NOT_RUN
UNITY_CANDIDATE_VISUAL_VERIFIED: false
REALISM_CALIBRATED: false
EVIDENCE_FILES: artifacts/calibration/20260914-audit/{manifest,baseline-summary,candidate-summary,comparison,issues}.json; metrics.csv; replays/; logs/; source snapshots
KNOWN_LIMITATIONS: unspecified surface/population, sparse sampling, simplified finite footprint/net, single cross-platform seed and partial Mac build-identity evidence
NEXT_SMALLEST_STEP: retain Mac SDK/project/source identity; add a reusable semantic replay comparator; expand the OS/architecture/seed matrix
```
