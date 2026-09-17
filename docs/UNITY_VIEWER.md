# Unity 3D Replay Viewer — 소스 구현, Unity 미검증

기존 MatchRecord JSON을 직접 읽는 개발용 뷰어다. Core DLL/소스, 난수, 물리, 점수 계산을 Unity에서 실행하지 않는다. Core/CLI 및 원본 샘플은 변경하지 않았다.

**현재 상태: SOURCE_IMPLEMENTED_UNVERIFIED.** 이 환경에는 실행 가능한 Unity Editor를 찾지 못했고 DISPLAY/WAYLAND_DISPLAY도 없다. 정확한 Editor 버전, 라이선스, Unity 컴파일, 패키지 조합, 화면은 검증되지 않았다. 특정 버전을 추측한 ProjectVersion.txt/manifest.json이나 가짜 scene YAML을 넣지 않았다. 현재 프로젝트 폴더는 **Assets 기반 소스 scaffold**이며 아래 준비 단계에서 실제 Editor가 ProjectSettings/Packages를 생성해야 한다. 제공한 `.meta`는 소스 GUID를 유지하기 위한 수동 파일이며 Editor import 검증 전이다.

## 준비 및 실행

프로젝트: `unity/TennisSim.UnityViewer` (저장소 루트 기준).

필요 환경: 로컬 .NET SDK 10.0.401(현재 검증), Python 3, Bash, sha256sum, 실제로 사용 가능한 정식 Unity Editor와 라이선스. 뷰어는 Built-in 3D 렌더 파이프라인과 Standard shader를 사용한다. **검증된 최소 Unity 버전은 아직 없다.** 기존 ProjectVersion.txt가 생기면 그 정확한 Editor를 사용한다. 준비 스크립트가 실행 파일의 `-version`과 ProjectVersion.txt를 대조한다. 시스템 설치나 Unity 다운로드는 자동 수행하지 않는다.

저장소 루트에서:

```bash
# 보존된 실제 샘플을 검증하고 StreamingAssets/Replays/sample-42.json으로 복사.
# 스크립트는 어느 작업 디렉터리에서 호출해도 루트를 스스로 찾는다.
scripts/prepare-unity-replay.sh

# 실제 설치된 실행 파일 경로로 설정한다.
export UNITY_EDITOR="/path/to/installed/Editor/Unity"
scripts/validate-unity-viewer.sh prepare
```

처음에는 선택된 Editor로 별도 빈 프로젝트를 `artifacts/unity-validation/editor-*/BootstrapProject`에 만든 뒤, **실제 생성된** ProjectSettings/Packages만 대상 프로젝트에 복사한다. 기존 설정을 덮어쓰지 않는다. Test Framework가 기본 manifest에 없으면 준비가 BLOCKED로 종료된다. 그 경우 Unity Hub에서 대상 프로젝트를 열고 Package Manager → Unity Registry → Test Framework에서 해당 Editor가 제공하는 호환 버전을 설치한다. 설치 후 manifest 및 packages-lock을 보존하고 다시 실행한다. 테스트 외 외부 패키지는 필요하지 않다.

```bash
scripts/validate-unity-viewer.sh compile
# 또는 compile + EditMode + PlayMode를 순서대로 실행
scripts/validate-unity-viewer.sh all
```

Editor에서 **TennisSim → Create or open replay scene**을 선택하고 Play를 누른다. 메뉴는 `Assets/TennisSim/Scenes/Replay.unity`를 생성/열고 Build Settings에 연결한다. 다시 실행해도 scene root를 추가하지 않는다. 런타임 stage도 중복 생성하지 않는다. Inspector 연결은 필요 없다. 기존 수정 scene은 저장 확인 후 전환한다. 코트/카메라/조명/캡슐/공/마커는 Play 시작 시 생성된다.

## 조작과 표시

- 왼쪽 HUD: Play/Pause, Restart(일시정지 상태), 0.25x/1x/2x, 시간 slider, Previous/Next event.
- 경로 입력과 Load JSON으로 다른 로컬 파일을 연다. 실패하면 이전 경기 표시를 숨기고 명확한 오류를 표시한다.
- 파일명, seed, 선수 ID, 현재/총 포인트, 전체 경기 시각, 기록된 점수와 phase, 최근/선택 사건, 완료 승자 및 종료 status를 표시한다.
- 선수 A는 주황, B는 파랑. 발 위치 위에 1.8m 캡슐을 배치한다. 선수 크기는 표시용 가정이다.
- 공 중심은 그대로이며 기본 지름은 원래 0.067m의 3배 표시다. 체크박스로 원래 크기로 전환한다.
- 빨강=타격, 청록=바운드, 자홍=네트 접촉. 현재 포인트에서 커서 이전 가장 최근 마커 하나를 표시한다. 좌표·시간은 사건 그대로다. HUD에 포인트/종류/시각을 표시한다. 선택 사건이 마커라면 해당 사건이 표시된다.
- 이전/다음 사건은 sequence 단위로 움직이므로 동일 시각 사건도 개별 확인 가능하다. 표시 상태 자체는 항상 그 시각의 마지막 사건 상태다. 선택 사건과 그 시각의 최종 상태는 구별한다.
- 탐색은 재생을 멈추고 점수/좌표/마커를 다시 구성한다. 마커 기록을 누적하지 않는다.

## 데이터 갱신과 데이터 검사

기존 파일을 덮어쓰지 않는 새 경로를 선택한다:

```bash
.tools/dotnet/dotnet build TennisSim.sln --no-restore --nologo
.tools/dotnet/dotnet run --project src/TennisSim.Cli --no-build -- \
  match --seed 42 --out artifacts/new-sample-42.json
scripts/prepare-unity-replay.sh artifacts/new-sample-42.json
```

준비 스크립트는 **기준 seed-42 fixture**를 검사한다. 다른 seed/설정 리플레이는 별도 파일로 저장해 HUD에서 직접 연다. 기준 테스트를 다른 경기로 조용히 교체하지 않는다. 입력이 없으면 생성 명령 안내와 exit 2를 반환한다. 추가 viewer JSON은 필요 없다. 원본·복사본 SHA-256이 일치해야 성공한다.

```bash
# Unity 없는 환경에서 실제 데이터 코드만 검사. Unity 테스트가 아님.
.tools/dotnet/dotnet build tests/TennisSim.ViewerChecks --nologo
.tools/dotnet/dotnet run --project tests/TennisSim.ViewerChecks --no-build -- \
  artifacts/sample-42.json

# 실제 Unity 단계별 검사. 실행 중인 동일 프로젝트 Editor는 먼저 닫는다.
scripts/validate-unity-viewer.sh EditMode
scripts/validate-unity-viewer.sh PlayMode
```

EditMode는 실제 로더/시간축/좌표 변환과 scene setup을 검사한다. PlayMode는 생성된 Replay scene을 로드하고 실제 Presenter/Transform/Controller를 사용하여 전체 사건 순서와 위치/결과를 검사한다. PlayMode 단독 실행 전 compile 단계로 scene을 준비한다. 자동 전 구간 테스트는 시간 커서를 빠르게 전진시키는 통합 검사이며 실제 시간 10분 재생/화면 검토를 대체하지 않는다.

Unity 테스트 실행에는 `-runTests`와 `-quit`를 함께 사용하지 않는다. 스크립트는 종료 코드뿐 아니라 NUnit XML의 완료 시각, 실제 case 개수, 통과/실패 수를 검사한다. 0개 실행·누락·실패·미완료·skip은 성공이 아니다. 로그/XML/버전/패키지/해시는 매 실행 새 evidence 디렉터리에 저장한다. `-nographics`는 시각 검증이 아니다.

## 실제 화면 검증 — 아직 미실행

Editor Game 창을 충분히 넓게 열고 코트 방향, A/B 끝 위치, 공 높이, 타격/바운드 직전·정확 시각·직후, 포인트 리셋, 마지막 점수를 확인한다. HUD 버튼과 slider, 사건 탐색도 조작한다. 전체 재생 후 스크린샷·Editor 버전·입력 해시·Console 오류 여부를 별도 evidence 경로에 기록한다. 실제 확인 전 UNITY_VISUAL_VERIFIED를 true로 바꾸지 않는다.

## 한계와 문제 해결

- 모델은 미보정이다. 긴 랠리/코트 밖 타격 등을 보간으로 숨기지 않는다.
- 기본 샘플은 0.1초 간격 상태 + 사건 전후 상태다. 중간은 직선 보간이며 정확한 연속 궤적/매 tick 상태가 아니다. 타격 시간 양자화는 최대 8.33ms 그대로다.
- 동일 시각 리셋 전 frame과 리셋 후 event가 함께 있을 수 있다. 같은 시각에 두 위치를 동시에 표시하지 않고 마지막 사건을 우선한다. [기록 계약](REPLAY_CONTRACT.md)을 참고한다.
- 공/선수 Collider는 즉시 비활성화 후 제거한다. Rigidbody/NavMesh는 사용하지 않는다.
- 네트는 20개 strip으로 엔진 높이를 근사한다(최대 높이 차 약 7.8mm). 5cm 코트 라인 두께는 표시 가정이고 경계는 엔진의 바깥쪽 edge 값에 맞춘다.
- Built-in 외 렌더 파이프라인은 setup에서 거부한다. 분홍 material이 보이면 파이프라인과 Standard shader를 확인한다. 프로젝트 파이프라인을 자동 교체하지 않는다.
- Editor와 로컬 데스크톱 파일 IO를 대상으로 한 소스다. 실제 Player build도 미검증이다. WebGL/모바일/원격 파일은 지원하지 않는다.
- 프리미티브·IMGUI·카메라 구도 및 Unity API 호환은 실제 Editor 검증이 남아 있다. 입력에 없는 라켓/애니메이션/물리 상태는 생성하지 않는다.

구현 시 참고한 공식 명령 계약: [Editor command line](https://docs.unity3d.com/6000.0/Documentation/Manual/EditorCommandLineArguments.html), [Test Framework command line](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html). 이는 설치/실행 또는 특정 버전 조합 검증의 증거가 아니다.

## Calibration candidate verification (2026-09-14)

The current Linux checkout has no ProjectSettings/Packages from the reported Mac run. Reported 6000.6.0f1 and Test Framework request 1.8.0 are **not verified current files** here; resolved package version is UNKNOWN. Do not regenerate or upgrade existing Mac settings to match this Linux scaffold.

`prepare-unity-replay.sh INPUT OPTIONAL_NEW_FILENAME.json` now supports named audit copies via the complete shared-data checks (`--general`), and refuses to replace an existing different file. Omitted filename retains the original seed-42 golden fixture checks. Python 3 replaces platform-specific hash/path/grep commands; SDK lookup uses DOTNET then PATH, with Linux-only local SDK fallback.

The existing fixture tests remain. Additional Unity candidate tests require `TENNISSIM_CANDIDATE_REPLAY` pointing to the actual candidate; if missing they report skipped/NOT_RUN, never passed candidate verification. A .NET shared-data pass does not replace EditMode/PlayMode or visual review. See [CALIBRATION.md](CALIBRATION.md) for exact files, hashes and Mac commands.
