# Unity 코치 UI (2026-09-24)

[GAME_LOOP.md](GAME_LOOP.md) 수직 슬라이스의 UI 구현. 와이어프레임
(https://claude.ai/artifact/GshzaRGoUCtLYDMyZMayU6)과 디자인 시스템 TennisSim Coach UI
(https://claude.ai/artifact/7CHzTq4k81XYbUU416mafM, 토큰 v2)을 Unity UI Toolkit으로 옮겼다.

```text
IMPLEMENTATION_STATUS: SOURCE_IMPLEMENTED_UNVERIFIED
APP_LAYER_CHECKS: PASS (tests/TennisSim.CoachChecks 12/12, C# 9, warnings as errors, actual Core)
UNITY_COMPILE_RESULT: NOT_RUN (no Unity Editor on the Linux host)
UNITY_PLAYMODE_TEST_RESULT: NOT_RUN
UNITY_VISUAL_VERIFIED: false
```

## 구조

리플레이 뷰어와 달리 코치 UI는 경기를 **직접 진행**한다. 그래서 Unity 안에서 실제 Core가 돈다. 결과는 여전히
Core만 결정하고, UI는 기록을 읽어 그린다.

| 계층 | 위치 | UnityEngine | 검증 |
|---|---|---|---|
| Core DLL | `Coach/Plugins/TennisSim.Core.dll` (생성물, gitignore) | 없음 | Core 테스트 70개 |
| 앱 계층 `TennisSim.Coach.App` | `Coach/App/*.cs` | 없음 (`noEngineReferences`) | .NET `tests/TennisSim.CoachChecks`가 같은 소스를 C# 9로 컴파일해 실행 |
| Unity 계층 `TennisSim.Coach` | `Coach/Runtime/*.cs`, `Coach/Resources/*.uss,*.tss` | UI Toolkit | Mac에서 컴파일과 PlayMode 필요 |
| Editor | `Coach/Editor/CoachSceneSetup.cs` | Editor | 메뉴 TennisSim > Create or open coach scene |
| PlayMode 테스트 | `Coach/Tests/PlayMode` | Test Framework | 미실행 |

- `CoachSession`: 경기 전 → 진행 → 체인지오버 → 종료 상태 기계. 프레임 시간만큼 tick을 진행하다가
  (`MatchEngine.AdvanceUntilChangeover`) 체인지오버에서 멈춘다. 그때 상대 코치(`OpponentCoach.DecideWithReasons`)가
  먼저 결정한다. 사용자의 변경은 `QueueTactics`로 기록된다. 프레임 단위로 진행한 경기와 체인지오버 단위로 건너뛴
  경기가 바이트 단위로 같고, 기록만으로 재시뮬레이션된다(CoachChecks로 확인).
- `CoachViews` / `CoachText`: 네 화면의 뷰 모델과 한국어 문장. 모든 수치는 기록에서 읽는다. 상대 코치 알림은
  Core의 이유 코드(`CoachReasonKind`)를 문장으로 옮긴다. 예: "Ember의 백핸드 에러율이 23%로 포핸드 14%보다
  높습니다."
- `CoachApp`: `PanelSettings`, `UIDocument`, 테마를 런타임에 만든다. 따라서 씬에는 이 컴포넌트 하나만 있으면 된다.
- `CourtView` / `HalfCourtView`: Painter2D로 그린다. 원은 다각형으로 그려 Arc와 Angle 오버로드에 의존하지 않는다.
- 경기 상대는 고정이다(`CoachMatchup`): Ember(baseline) 대 Rook(baseline의 포핸드·백핸드 값을 맞바꾼 선수).
  선수 선택은 시즌 계층의 몫이다.

## 디자인 시스템 적용

`TennisSimCoach.uss`의 `:root` 변수가 토큰 v2이고, 디자인 시스템의 Unity 규칙(project/unity.md)을 따른다: 포커스 때
테두리가 두꺼워진 만큼 padding을 줄여 크기를 유지하고, label 문자열은 C#에서 `ToUpperInvariant()`로 바꾸고,
한 줄 요소는 `min-height`로 줄 높이를 맞춘다. Chip(비선택은 court·control-border·line-muted, 선택은
selected·on-selected), InfoBanner(info-surface·info-border), 히트맵(친 선수 색 채움 = 백핸드 공략, line-muted 빈 원 =
그 외, line 빈 원 = 아웃, 범례 글자 포함)을 따른다. USS에 outline이 없어서 포커스 링은 ball 색 2px 테두리로 대신한다.

한글: 디자인 시스템은 시스템 산세리프만 지정한다. Unity 기본 런타임 폰트에는 한글이 없으므로
`Font.CreateDynamicFontFromOSFont`로 OS 폰트를 쓴다(macOS Apple SD Gothic Neo, Windows Malgun Gothic 순).
숫자는 SF Mono, Menlo 순이다. **이 경로는 검증되지 않았다.** 글자가 네모로 나오면 한글 폰트 에셋을 프로젝트에 넣는
결정이 필요하다(OFL 폰트 추가).

## Mac에서 할 일

```bash
git pull --ff-only
scripts/sync-core-to-unity.sh            # Core DLL 생성과 복사
export UNITY_EDITOR="/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity"
scripts/validate-unity-viewer.sh all     # 이제 코치 씬 생성과 Coach PlayMode 테스트도 포함
```

`validate-unity-viewer.sh`는 prepare 단계에서 DLL을 다시 동기화한다. compile 단계 뒤 `CoachSceneSetup.Setup`을
실행해 `TENNISSIM_COACH_SCENE_READY`를 확인하고, PlayMode에서 `TennisSim.Coach.PlayMode`도 돌린다.

Editor에서 처음 열면 새 파일들의 `.meta`가 생성된다. 그 `.meta` 파일들을 커밋한다(GUID를 고정하기 위해).
화면 확인 항목:

1. 경기 전: 칩 9개, 선택 반전, 경기 시작.
2. 경기: 점수판(15/30/40/AD), 코트 위 선수 캡슐과 공, 속도 1×/4×/16×, 일시정지, "다음 체인지오버까지".
3. 체인지오버: 이번 구간과 누적 표, 체력 줄, 상대 코치 알림(Safe로 시작하면 첫 체인지오버에 나옴), 칩과 두 버튼.
4. 리뷰: 구간 흐름, 게임 승자 줄, 착지 히트맵과 범례, 전체 기록.
5. 한글이 네모로 나오지 않는지, 1280×800 기준으로 잘리는 곳이 없는지.

결과는 이 문서에 새 절로 기록한다. 확인 전에는 위 상태 줄을 바꾸지 않는다.
