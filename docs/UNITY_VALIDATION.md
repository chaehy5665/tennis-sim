# Unity Viewer 검증 기록 — 2026-09-14

**IMPLEMENTATION_STATUS=SOURCE_IMPLEMENTED_UNVERIFIED.** 데이터 경로와 Unity 소스를 구현했다. 실제 Unity 컴파일·Play Mode·화면 확인은 Editor 부재로 수행하지 못했다. 전체 완료 기준은 아직 충족하지 않았다.

## 실행 환경과 기존 파일 보존

- 작업 폴더 `/home/c10/projects/tennis`. `git status --short`는 exit 128: Git 저장소가 아니다. Git 초기화/commit/push를 하지 않았다.
- 실제 로컬 SDK `.tools/dotnet/dotnet`: 기존과 동일한 .NET 10 환경. Core netstandard2.1, CLI/기존 tests net10.0. ViewerChecks도 net10.0이며 공유 소스는 C# 8.0으로 빌드했다.
- Unity 디렉터리는 처음에 비어 있었다. ProjectVersion.txt 및 package manifest가 없었다. PATH의 Unity/unity/unity-editor와 확인한 표준 설치 경로에서 Editor를 찾지 못했다. DISPLAY/WAYLAND_DISPLAY도 설정되지 않았다.
- Unity 라이선스 상태는 UNKNOWN: 실행 파일이 없어 확인 불가. 자격증명/라이선스 내용을 읽지 않았다. 설치/전역 설정 변경은 하지 않았다.
- Core/CLI/기존 tests의 모든 소스 해시와 원본 `artifacts/sample-42.json`의 해시를 저장해 작업 후 동일함을 확인했다. 증거: `artifacts/unity-validation/baseline/source-hashes.json`, `summary.json`.

## 실제 결과

| 검사 | 결과 | 로그/증거 (`artifacts/unity-validation/` 기준) |
|---|---|---|
| 변경 전 솔루션 build | exit 0, 경고 0 / 오류 0 | baseline/build.log |
| 변경 전 기존 테스트 | exit 0, 32 passed / 0 failed | baseline/tests.log |
| 변경 전 seed 42 생성 | exit 0, B 6–0, 27포인트 | baseline/match.log, baseline/sample-42.json |
| 변경 후 솔루션 build | exit 0, 경고 0 / 오류 0 | core-build.log |
| 변경 후 기존 테스트 | exit 0, 32 passed / 0 failed | core-tests.log |
| 실제 공유 뷰어 데이터 소스 build | exit 0, 경고 0 / 오류 0 | viewer-build.log |
| 실제 공유 로더/시간축/controller 검사 | exit 0, 14 passed / 0 failed | viewer-checks.log |
| 변경 후 seed 42 | exit 0, B 6–0, 27포인트 | match.log, sample-42.json |
| chunk 137 재시뮬레이션 | exit 0, RESIMULATION_IDENTICAL=true | resimulate.log, resimulated-42.json |
| 독립 1,000포인트 | exit 0, 1,000 완료 / 실패 0 | points.log, points-1000.json |
| 전술 비교 seeds 11,22,33,44,55 | exit 0, 4정책 × 5세트 모두 완료; 기존 comparison.json과 JSON 구조 전체 일치 | compare.log, comparison.json, summary.json |
| 샘플 준비 | exit 0, 원본/복사본 해시 일치 | prepare-replay.log |
| shell 문법 및 입력 없는 실패 경로 | bash -n exit 0; 없는 입력 exit 2, nested cwd에서도 안내 | commands.json, script-failure-checks.json |
| Unity XML 판독기 | 합성 fixture 5개에서 성공/0개/실패/잘림/미완료 구분 확인 | xml-parser-checks.json |
| Unity 자동 검증 시도 | exit 2, UNITY_EDITOR 없음으로 BLOCKED | editor-attempt.log, editor-*/blocked.txt |
| Unity compile / EditMode / PlayMode | NOT_RUN — 환경 차단 | 실제 Unity XML 없음 |
| 실제 화면 / Player build | NOT_RUN — Editor/그래픽 세션 없음 | 캡처 없음 |

**ViewerChecks는 Unity 테스트가 아니다.** `Runtime/Data/*.cs`와 공유 검사 코드를 그대로 링크한 .NET 실행 파일이다. Unity stubs, 가짜 로더나 별도 sampler를 검사한 결과가 아니다. Unity 테스트 소스는 작성했으나 컴파일 및 실행하지 않았다. 합성 XML fixture도 Unity 테스트 실행 증거가 아니다.

공유 검사 14개는 실제 sample 파싱, 미지원 버전/엔진, 필수값 누락, 손상 JSON/중복 키/잘못된 수치, 시간·sequence 오류, 첫/끝 상태, 모든 frame 시각의 명시적 사건 우선순위, 모든 타격/바운드/네트 경계, 모든 위치 리셋, 단일 프레임 전체 사건 통과, 재생/정지/속도/재시작/되감기, 동일 시각 개별 사건 탐색, 30/144Hz 및 0.73초 간격 전체 이벤트 순서, 좌표 변환을 포함한다. 위치 오차 기준 1e-7m. Unity용 float 검사는 1e-6m로 작성했으나 미실행이다.

## 입력과 재생 상태 비교

- seed=42; 27포인트; 2,309사건; 6,118 frame; 길이 **611.625초**.
- 최종 기록 display=`0-6 0-0 Complete`, winner=1(B).
- SHA-256: `a7b086639798233d44fbde48f2529ba38182fd7b2bd5dd50dbca748431d18f60`.
- 원본 sample, 변경 전 생성, 변경 후 생성, 재시뮬레이션, Unity StreamingAssets 복사본이 **바이트 단위 동일**.
- 관측 옵션은 추가하지 않았다. 기존 기록을 읽기만 하므로 RNG 호출이나 엔진 입력에 변화가 없다. 모든 도메인 사건과 입력도 위 전체 파일 일치에 포함된다.
- 같은 시각 이전 frame과 이후 event 위치가 다른 49개 경우에는 문서화한 마지막 사건 우선순위가 적용된다. 원본 frame이 우선하는 나머지 시각은 원본 위치와 일치한다. 이 선택을 숨기거나 허용 오차로 무마하지 않았다.
- 전술 비교 승리 수는 Balanced 3/5, Backhand 5/5, Safe 0/5, Aggressive 1/5. 기존 결과와 동일하며 현실성/전술 우열 보정의 근거로 사용하지 않는다.

## 정확한 실행 명령

명령별 exit code와 로그 매핑은 `artifacts/unity-validation/commands.json`에 저장했다. 아래는 저장소 루트에서 실제 실행한 주요 명령이다. `--no-build`는 성공한 build 이후에 사용했다.

```bash
.tools/dotnet/dotnet build TennisSim.sln --no-restore --nologo
.tools/dotnet/dotnet run --project tests/TennisSim.Tests --no-build
.tools/dotnet/dotnet run --project src/TennisSim.Cli --no-build -- match --seed 42 --out artifacts/unity-validation/baseline/sample-42.json
.tools/dotnet/dotnet build tests/TennisSim.ViewerChecks --nologo
.tools/dotnet/dotnet run --project tests/TennisSim.ViewerChecks --no-build -- artifacts/sample-42.json
scripts/prepare-unity-replay.sh
scripts/validate-unity-viewer.sh all
.tools/dotnet/dotnet build TennisSim.sln --no-restore --nologo
.tools/dotnet/dotnet run --project tests/TennisSim.Tests --no-build
.tools/dotnet/dotnet run --project src/TennisSim.Cli --no-build -- match --seed 42 --out artifacts/unity-validation/sample-42.json
.tools/dotnet/dotnet run --project src/TennisSim.Cli --no-build -- resimulate --input artifacts/unity-validation/sample-42.json --chunk 137 --out artifacts/unity-validation/resimulated-42.json
.tools/dotnet/dotnet run --project src/TennisSim.Cli --no-build -- points --count 1000 --seed 100 --out artifacts/unity-validation/points-1000.json
.tools/dotnet/dotnet run --project src/TennisSim.Cli --no-build -- compare --seeds 11,22,33,44,55 --out artifacts/unity-validation/comparison.json
bash -n scripts/prepare-unity-replay.sh scripts/validate-unity-viewer.sh
```

Core 관련 명령은 `&&` 체인 전체 exit 0으로 확인했다. Unity wrapper만 exit 2이며 실제 Editor 프로세스는 시작되지 않았다. 새 검증 증거는 기존 `docs/VALIDATION.md` 및 기존 artifacts를 덮어쓰지 않고 별도 디렉터리에 저장했다. 본 문서의 명령을 다시 실행할 때도 새 출력 경로를 선택한다.

## 상태

```text
IMPLEMENTATION_STATUS: SOURCE_IMPLEMENTED_UNVERIFIED
CORE_BUILD_RESULT: PASS (0 warnings, 0 errors)
CORE_TEST_RESULT: PASS (32/32 before and after)
CORE_REGRESSION_RESULT: PASS (1000 points; resimulation and all replay bytes identical; 20 tactical sets)
REPLAY_SCHEMA_CHANGE: NONE (existing schema 1.0 direct adapter)
VIEWER_REPLAY_FILE: unity/TennisSim.UnityViewer/Assets/StreamingAssets/Replays/sample-42.json
UNITY_EDITOR_VERSION: UNKNOWN / executable unavailable
UNITY_COMPILE_RESULT: NOT_RUN (environment blocked)
UNITY_EDITMODE_TEST_RESULT: NOT_RUN (environment blocked)
UNITY_PLAYMODE_TEST_RESULT: NOT_RUN (environment blocked)
UNITY_RUNTIME_VERIFIED: false
UNITY_VISUAL_VERIFIED: false
SAMPLE_REPLAY_RESULT: shared-data checks PASS; B 6-0; 27 points; 2309 events; Unity playback NOT_RUN
REALISM_CALIBRATED: false
EVIDENCE_FILES: artifacts/unity-validation/commands.json, summary.json, environment.json, logs
KNOWN_LIMITATIONS: Unity import/compile/packages/scene/rendering/Player unverified; sampled linear interpolation only
NEXT_SMALLEST_STEP: licensed real Editor -> prepare -> compile -> EditMode -> PlayMode -> graphical review
```

## 다음 최소 작업

사용 가능한 정식 Editor 실행 파일을 `UNITY_EDITOR`로 지정해 **prepare** 단계에서 실제 버전과 패키지 구성을 확정한다. 이어 compile/EditMode/PlayMode를 통과시키고 Game 창을 직접 검토한다. [실행 안내](UNITY_VIEWER.md)의 순서와 증거 규칙을 따른다. 이 단계 전까지 전체 구현 완료나 Unity 실행/시각 검증 성공을 선언할 수 없다.


## 2026-09-17 v3 handoff (prepared here, not executed)

This section is the v3 procedure prepared on the Linux host, not a result. No Unity Editor exists on this
machine, so compile, EditMode, PlayMode and screen review were NOT_RUN and must be recorded by a Mac session.
The files and hashes below were produced by the engine at commit 432f0fe; scripts/prepare-unity-replay.sh
verified the copies byte for byte.

### What to bring to the Mac

- The source tree up to commit 432f0fe, or at minimum unity/TennisSim.UnityViewer/Assets/TennisSim/**,
  scripts/prepare-unity-replay.sh and scripts/validate-unity-viewer.sh.
- The two local replay files (both are git-excluded; on the Mac they are recreated by the prepare script):

| File | SHA-256 | Contents |
|---|---|---|
| artifacts/bounce/final/v3-legacy-42.json | f9bc7179634483c8bd845b0caf1e1741578a10dbcdd0c8e8f00bc56d07062b0b | legacy multiplicative bounce, 27 points, 2,309 events |
| artifacts/bounce/final/v3-impulse-42.json | 40a5cf567e5755bddadb9c912c99f737161416316a6b1a6d81d44bb08c05391e | V1 impulse model, 28 points, 2,289 events |

### Commands to run on the Mac

    git -C <mac-repo> status --short && git -C <mac-repo> log -1 --oneline
    cat unity/TennisSim.UnityViewer/ProjectSettings/ProjectVersion.txt
    cat unity/TennisSim.UnityViewer/Packages/manifest.json

    # Replay preparation, with hash verification. An existing destination only passes when the bytes match.
    scripts/prepare-unity-replay.sh artifacts/bounce/final/v3-legacy-42.json bounce-v3-legacy-42.json
    scripts/prepare-unity-replay.sh artifacts/bounce/final/v3-impulse-42.json bounce-v3-impulse-42.json

    export UNITY_EDITOR="/Applications/Unity/Hub/Editor/<installed version>/Unity.app/Contents/MacOS/Unity"
    export TENNISSIM_CANDIDATE_REPLAY="$PWD/unity/TennisSim.UnityViewer/Assets/StreamingAssets/Replays/bounce-v3-impulse-42.json"
    scripts/validate-unity-viewer.sh all

    # Native Mac resimulation of the same record on a second platform. Use the Mac's own SDK on PATH, or set
    # DOTNET to it, exactly as the existing calibration docs describe.
    dotnet run --project src/TennisSim.Cli -- resimulate \
      --input artifacts/bounce/final/v3-impulse-42.json --chunk 137 --out artifacts/mac-v3-impulse-resimulated-42.json

### Expected values

- v3 legacy: final score B 6-0, 27 points, 2,309 events, replay length 611.625 s.
- v3 impulse: final score B 6-0, 28 points, 2,289 events. Every BallBounced event carries a bounce object with
  profileId design-unc-v1 and a profileHash equal to the hash of input.surface.profile in the same replay. Some
  contacts are status SETTLED (resting contact, zero impulse); that is a policy outcome, not a new event kind.
- The renderer accepts v1, v2 and v3. On screen the ball centre sits at Y = 0.0335 m at a bounce, and the
  enlarged ball changes only its drawn diameter.
- The Mac resimulation is NOT required to be byte identical to the Linux hash. Record event kinds and order,
  the final score, finalRandomState and per-field tolerances (1e-7 relative) only. Cross-platform status for v3
  stays NOT_TESTED until that comparison exists.

### What to record

Only new results: Editor version, resolved package lock version, EditMode and PlayMode counts and failures,
loading and full playback of both replays, the controls (pause, restart, speeds, event navigation, seek), the
screen judgement, and the Mac resimulation comparison. Earlier v1 and v2 sessions are not evidence for v3.
