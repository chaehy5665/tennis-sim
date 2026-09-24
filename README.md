# TennisSim MVP

그래픽과 독립된 단식 한 세트 테니스 시뮬레이터. 승자를 먼저 추첨하지 않고, 전술 후보 → 실행 오차 → 궤적 → 이동/타격 가능성 → 규칙 판정으로 득점한다. Unity 프로젝트와 외부 실행 서비스는 없다.

현재 환경에서 검증한 SDK: **10.0.401**, .NET runtime **10.0.12**, Linux x64. Core는 **netstandard2.1 / C# 8.0**, CLI와 테스트는 net10.0이다. Core에 Unity, 파일 IO, 네트워크, 시각, JSON 의존성이 없다. SDK 10에서 빠진 `NETStandard.Library.Ref 2.1.0`만 최초 빌드 시 NuGet에서 복원한다. 별도 게임/테스트 프레임워크 패키지는 없다.

## 실행

저장소 루트에서 실행한다. 전역 SDK를 사용하는 경우 첫 줄은 생략한다.

```bash
export PATH="$PWD/.tools/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

dotnet build TennisSim.sln
dotnet run --project tests/TennisSim.Tests --no-build

# 포인트별 승자·종료 원인·점수, 최종 점수와 JSON
dotnet run --project src/TennisSim.Cli --no-build -- match \
  --seed 42 --out artifacts/sample-42.json

# 선수와 전술 교체. ID는 CLI에서 A/B로 고정하며 이름/능력은 JSON에서 읽는다.
dotnet run --project src/TennisSim.Cli --no-build -- match \
  --seed 42 --player-a examples/players/server.json \
  --player-b examples/players/defender.json \
  --tactics-a examples/tactics/backhand-aggressive.json \
  --instructions examples/instructions.json \
  --out artifacts/tactical-42.json

# 같은 선수 능력, 상대 전술, 시작 서버/엔드, 고정 seed 목록을 네 정책에 재사용
dotnet run --project src/TennisSim.Cli --no-build -- compare \
  --seeds 11,22,33,44,55 --out artifacts/comparison.json

# 독립 포인트마다 새 엔진, 체력 초기화, seed/server/end 교대
dotnet run --project src/TennisSim.Cli --no-build -- points \
  --count 1000 --seed 100 --out artifacts/points-1000.json

# 코칭 텍스트 프로토타입: 체인지오버마다 멈추고 구간 통계를 보여 준 뒤 A의 전술 변경을 받는다.
# B는 OpponentCoach가 코칭한다 (--opponent fixed면 초기 전술 유지).
dotnet run --project src/TennisSim.Cli --no-build -- coach \
  --seed 42 --player-b defender --out artifacts/coached.json

# 상대 코치 AI 평가: 고정 전술 A를 상대로 B 고정 대 B AI 코칭 (docs/BALANCE_DIAGNOSIS.md)
dotnet run --project src/TennisSim.Cli --no-build -- coach-eval \
  --sets 30 --seed 100 --out artifacts/balance/coach-eval-30.json

# 자동 플레이 테스트: 코치 정책별 결정 가치, 지배 전략, 효과 가시성, 상대 AI 안정성 (docs/PLAYTEST.md)
dotnet run --project src/TennisSim.Cli --no-build -- playtest \
  --sets 40 --seed 100 --out artifacts/playtest.json

# Unity 코치 UI: Core DLL을 Unity 프로젝트로 복사 (docs/UNITY_COACH_UI.md)
scripts/sync-core-to-unity.sh
dotnet run --project tests/TennisSim.CoachChecks --no-build

# 전술 밸런스 격자: 선수 조합 × 전술별 독립 포인트 승률 (docs/BALANCE_DIAGNOSIS.md)
dotnet run --project src/TennisSim.Cli --no-build -- balance \
  --count 2000 --seed 100 --out artifacts/balance/balance-2000.json

# 기록 재생: Core를 재실행하지 않고 저장된 득점 사건을 텍스트 표시
dotnet run --project src/TennisSim.Cli --no-build -- replay \
  --input artifacts/sample-42.json

# 재시뮬레이션: 저장된 입력/지시를 다시 실행하고 전체 JSON 비교
dotnet run --project src/TennisSim.Cli --no-build -- resimulate \
  --input artifacts/sample-42.json --chunk 137 \
  --out artifacts/resimulated-42.json
```

테스트는 의도적으로 패키지 없는 실행형 검증 프로그램이다. **`dotnet test`는 이 테스트를 실행하지 않는다.** 위 `dotnet run --project tests/TennisSim.Tests`를 사용한다. 실패가 있으면 exit code 1을 반환한다. CLI는 정상 성공 0, 시뮬레이션 제한/재현성 불일치/배치 실패 1, 입력 오류 2를 반환한다.

`--quiet`은 경기의 포인트별 출력을 생략한다. `--chunk N`은 한 번에 엔진에 전달하는 tick 수이며 물리 dt나 재생 배속이 아니다. `--config examples/config.json`으로 물리/기술 제한을 읽는다. 기본 선수 프리셋은 `server`, `baseline`, `defender`; 전술 프리셋은 `balanced`, `backhand`, `safe`, `aggressive`이다. `help`에서 명령을 확인할 수 있다.

`compare`는 A의 초기 전술만 바꾼다. 기본 A/B는 모두 baseline이다. `--player-a`, `--player-b`, `--tactics-b`, `--config`로 동일하게 적용할 비교 조건을 지정할 수 있다. `--instructions`를 전달하면 모든 조건에 같은 지시를 적용하므로, 지시가 초기 전술을 덮어쓸 수 있음을 주의한다. JSON은 seed별 결과와 분자/분모, 목표 및 실제 첫 착지 좌표를 보존한다. 적은 세트의 승률은 전술 우열의 증거가 아니다.

## 구성과 범위

- `src/TennisSim.Core`: 좌표, 점수/서브 규칙, 궤적, 선수 이동, 전술, 고정 tick 실행, 상태/이벤트 계약.
- `src/TennisSim.Cli`: JSON, 파일, 단일 경기, 독립 포인트, 짝지은 전술 비교, 텍스트 기록 재생.
- `tests/TennisSim.Tests`: 규칙/수학/이동/전술/재현성/통합 검증.
- [MODEL.md](docs/MODEL.md): 단위·공식·선택 가중치·모델 한계.
- [REPLAY_CONTRACT.md](docs/REPLAY_CONTRACT.md): 상태, 사건, 준비 동작 연결 및 재생 권한.
- [DETERMINISM.md](docs/DETERMINISM.md): 동일 플랫폼 byte 결정론과 플랫폼 간 outcome/semantic/bitwise 재현성 계약.
- [VALIDATION.md](docs/VALIDATION.md): 실제 실행 결과와 규모.

큰 기록은 `artifacts/`에 생성되며 `.gitignore`로 제외한다. `.tools/`, `bin/`, `obj/`도 제외한다. 초기 폴더는 Git 저장소가 아니었으며 이 작업은 Git 초기화, commit, push를 수행하지 않는다.

구현 규칙: 어드밴티지 게임, 6게임/2게임 차, 6–6의 7점/2점 차 타이브레이크, 1·2구와 더블 폴트, 대각선 서비스 박스, 서비스 렛, 일반 게임/타이브레이크 서버와 엔드 교대, 라인 접촉 인, 첫 바운드 아웃, 반환 전 두 번째 바운드.

미구현: 발리, 스매시, 드롭샷, 복식, 다세트, 풋폴트, 신체/라켓/네트 접촉 위반, 타이브레이크 뒤 다음 세트, 대회별 규칙, 정교한 네트/라켓/스핀 물리, 애니메이션과 3D 표시, 임의 시점 저장/재개. 타격 접촉 판정은 고정 tick 끝에서 수행하며 최대 8.33ms 시간 양자화가 있다. 네트와 바운드는 tick 내부 충돌 시각을 계산한다. 모든 테니스 규칙 구현을 주장하지 않는다.

`UNITY_RUNTIME_VERIFIED=false`, `REALISM_CALIBRATED=false`. 계수와 가상 선수는 게임용 가정이다. 랠리 길이, 낮은 타점에서의 공격 후보 탈락, 프리셋 간 점수 편향을 실측과 비교하지 않았다. Linux x64와 Mac의 seed 42 비교는 outcome exact 및 semantic-with-tolerance PASS, 전체 replay bitwise FAIL이다. 범위와 증거는 [DETERMINISM.md](docs/DETERMINISM.md)를 따른다.

다음 최소 단계: **같은 Core 기록을 단순 3D 코트·선수 캡슐·공으로 표시해 이동과 타격 시각을 대조한다.** 렌더러가 경기 결과를 보정하지 않도록 유지한다.

## Realism diagnostics (2026-09-14)

Read-only `diagnose` sidecars, actual-Core fixed `scenarios`, before/after evidence and the first circular-footprint corner correction are documented in [REALISM_AUDIT.md](docs/REALISM_AUDIT.md) and [CALIBRATION.md](docs/CALIBRATION.md). No physics/player parameters were tuned. Engine v2 retains replay schema 1.0 and viewer loading of the v1 sample. Full realism and candidate Mac visual verification remain unclaimed.


## Court surface and bounce model (2026-09-15)

The engine now contains an explicit ball-surface collision model (V1) and an offline calibration pipeline.
The previous multiplicative bounce remains the default path and is numerically unchanged; the impulse model is
selected explicitly.

    # single impact, profile and full diagnostics
    dotnet run --project src/TennisSim.Cli --no-build -- bounce \
      --input artifacts/impact.json --surface-model impulse \
      --profile profiles/pair.json --out artifacts/bounce.json

    # full set with the impulse model and the built-in UNCALIBRATED design profile
    dotnet run --project src/TennisSim.Cli --no-build -- match \
      --seed 42 --surface-model impulse --out artifacts/impulse-42.json

    # calibration tool
    dotnet run --project src/TennisSim.Calibration --no-build -- validate-data --dataset data/bounce/manifest.json
    dotnet run --project src/TennisSim.Calibration --no-build -- fit \
      --dataset data/bounce/manifest.json --config calibration/fit-config.json \
      --models M1,M2 --synthetic --out artifacts/calibration/run-001
    dotnet run --project src/TennisSim.Calibration --no-build -- evaluate --run artifacts/calibration/run-001 --split test
    dotnet run --project src/TennisSim.Calibration --no-build -- virtual-itf \
      --profile artifacts/calibration/run-001/profile.json --temperature-c 23
    dotnet run --project src/TennisSim.Calibration --no-build -- export-profile \
      --run artifacts/calibration/run-001 --out profiles/pair.json --table-nodes 5
    dotnet run --project src/TennisSim.Calibration --no-build -- compare-runtime \
      --profile profiles/pair.json --table-nodes 5 --probes 9

`EngineVersion` is now tennissim-mvp-3 (replay schema 1.0 unchanged, renderer accepts v1, v2 and v3).
Legacy-path bytes are unchanged apart from the version string. The impulse model is a different physics
version, so resimulate refuses older replays instead of guessing.

Status of this work:

    SOFTWARE_IMPLEMENTED: PASS
    MATHEMATICAL_TESTS: PASS (57 core tests, 44/45 CLI scenarios; the single FAIL is the preserved default-drag reference gap)
    ENGINE_INTEGRATION: PASS (selectable model; default path unchanged)
    EMPIRICAL_DATA: MISSING
    EMPIRICAL_CALIBRATION: NOT_RUN
    SPIN_VALIDATION: NOT_OBSERVED in match play (validated in analytic fixtures only)
    MODEL_ADEQUACY: UNDETERMINED (no measured data)
    PROFILE_RELEASE: BLOCKED (runtime default DEV_ONLY, UNCALIBRATED)
    CROSS_PLATFORM_BITWISE_REPRODUCIBILITY: NOT_TESTED
    CROSS_PLATFORM_NUMERICAL_EQUIVALENCE: NOT_TESTED
    CROSS_PLATFORM_EVENT_EQUIVALENCE: NOT_TESTED

Details: [integration-notes.md](integration-notes.md), [docs/BOUNCE_MODEL.md](docs/BOUNCE_MODEL.md),
[docs/CALIBRATION_PIPELINE.md](docs/CALIBRATION_PIPELINE.md), [reports/model-card.md](reports/model-card.md),
[reports/calibration-report.md](reports/calibration-report.md),
[reports/reproducibility-report.md](reports/reproducibility-report.md).


## Empirical validation with published measurements (2026-09-17)

The calibration pipeline now runs on real published data. Cross 2002 (Am. J. Phys. 70, 482) reports tennis
ball bounces with zero incident spin, measured rebound speed, rebound angle and rebound spin, on wood, emery
paper and a Rebound Ace court surface. Seven records were extracted with a verified text-layer method and
registered as PUBLISHED_MEASUREMENT, so the repository no longer reports EMPIRICAL_DATA: MISSING.

    # extraction is reproducible and refuses a mismatched source PDF
    python3 scripts/build-cross2002-records.py --pdf <downloaded AJP00482.pdf>

    # published-measurement fit, chosen from the dataset subset in the manifest
    dotnet run --project src/TennisSim.Calibration --no-build -- validate-data \
      --dataset data/bounce/manifest.json --datasets cross2002-tennis-ball-surfaces
    dotnet run --project src/TennisSim.Calibration --no-build -- fit \
      --dataset data/bounce/manifest.json --datasets cross2002-tennis-ball-surfaces \
      --config calibration/fit-config-cross2002.json --models M1,M1B --out artifacts/calibration/cross2002-run
    dotnet run --project src/TennisSim.Calibration --no-build -- evaluate \
      --run artifacts/calibration/cross2002-run --split validation --per-record

Results: en = 0.8061 and beta_grip = 0.0495 on the emery training bounces, with mu_eff only upper bounded at
0.6 because no low-speed bounce saturates the friction limit. The normal response reproduces the in-domain
bounce to 0.011 m/s, but V1's rigid tangential coupling misses the measured spin by 95 rpm in-domain, and the
observational residual of section 12.4 exceeds its uncertainty by about 3 sigma. Transfer to wood and Rebound
Ace fails the 2 degree exit-angle target. No court is calibrated: the training surface is laboratory emery
paper and the fitted domain ends at 2.6 m/s.

    EMPIRICAL_DATA: LIMITED
    EMPIRICAL_CALIBRATION: PASS_IN_DOMAIN (normal response, emery service, 2.1-2.4 m/s); transfer FAIL
    SPIN_VALIDATION: FAIL_IN_DOMAIN
    MODEL_ADEQUACY: UNDETERMINED (structural residual about 3 sigma in-domain)
    PROFILE_RELEASE: BLOCKED

Full detail: [reports/empirical-validation-cross2002.md](reports/empirical-validation-cross2002.md).


## Tactic model v4 (2026-09-24)

`EngineVersion` is now tennissim-mvp-4 (schema 1.0 unchanged, viewer accepts v1 to v4). The
[balance diagnosis](docs/BALANCE_DIAGNOSIS.md) showed that under v3 one tactic (target the backhand) was
effectively always right, Safe was always wrong, rallies between defenders never ended, and the first random draw
of every engine was biased by small seeds. v4 changes, all described in [MODEL.md](docs/MODEL.md):

- `SeedRandom` mixes the seed before xorshift32. The calibration tool keeps the old stream (`SeedRandom.Legacy`).
- TargetBackhand shifts weight between symmetric Backhand and Forehand side candidates without adding displacement.
- Execution error depends on incoming pace (aggression punishes easy balls and is punished by hard ones, Safe
  absorbs pace) and grows slowly with rally length.
- A receiver who sees the same serve or rally direction repeatedly reacts faster (pattern reading).
- Players wait for a comfortable contact height when they have time; previously every rally ball was taken at
  about 0.46 m, which disabled the attack candidate.

All v3 replays remain viewable; resimulate refuses them. Tactic balance and rally length are game design targets,
not empirical calibration: `REALISM_CALIBRATED=false`.

## Receiver stance v5 (2026-09-24)

`EngineVersion` is now tennissim-mvp-5 (schema 1.0 unchanged, viewer accepts v1 to v5). A receiver used to run onto
the predicted contact point, so forehand or backhand was decided by tiny arrival differences and a shot aimed at
one side was played on that side only about 69% of the time. Now the receiver keeps the ball on the side it
arrives on and stands 0.35 m beside it (`Movement.Stance`); the aimed side is followed about 99% of the time.
Targeting the backhand now pays against weak backhands and costs against strong ones
([BALANCE_DIAGNOSIS.md](docs/BALANCE_DIAGNOSIS.md), "v5 결과"). v4 replays remain viewable; resimulate refuses them.

