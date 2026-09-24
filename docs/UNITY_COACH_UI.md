# Unity 코치 UI (2026-09-24)

[GAME_LOOP.md](GAME_LOOP.md) 수직 슬라이스의 UI 구현. 와이어프레임
(https://claude.ai/artifact/GshzaRGoUCtLYDMyZMayU6)과 디자인 시스템 TennisSim Coach UI
(https://claude.ai/artifact/7CHzTq4k81XYbUU416mafM, 토큰 v2)을 Unity UI Toolkit으로 옮겼다.

```text
IMPLEMENTATION_STATUS: UNITY_VERIFIED_LAYOUT_FIXES_PENDING_RECHECK
APP_LAYER_CHECKS: PASS (tests/TennisSim.CoachChecks 13/13, C# 9, warnings as errors, actual Core)
UNITY_COMPILE_RESULT: PASS (Mac, Unity 6000.6.0f1, 2026-09-24, error CS 0)
UNITY_PLAYMODE_TEST_RESULT: PASS (EditMode 4/4, PlayMode 3/3, validate-unity-viewer.sh all exit 0)
UNITY_VISUAL_VERIFIED: true at 1280x800 1x for a0a11cd; 2x Retina NOT_RUN; 8 layout issues fixed in 6090fcc, Mac re-check pending
```

## 구조

리플레이 뷰어와 달리 코치 UI는 경기를 **직접 진행**한다. 그래서 Unity 안에서 실제 Core가 돈다. 결과는 여전히
Core만 결정하고, UI는 기록을 읽어 그린다.

| 계층 | 위치 | UnityEngine | 검증 |
|---|---|---|---|
| Core DLL | `Coach/Plugins/TennisSim.Core.dll` (생성물, gitignore) | 없음 | Core 테스트 70개 |
| 앱 계층 `TennisSim.Coach.App` | `Coach/App/*.cs` | 없음 (`noEngineReferences`) | .NET `tests/TennisSim.CoachChecks`가 같은 소스를 C# 9로 컴파일해 실행 |
| Unity 계층 `TennisSim.Coach` | `Coach/Runtime/*.cs`, `Coach/Resources/*.uss,*.tss` | UI Toolkit | Mac 컴파일 통과 |
| Editor | `Coach/Editor/CoachSceneSetup.cs` | Editor | 메뉴 TennisSim > Create or open coach scene |
| PlayMode 테스트 | `Coach/Tests/PlayMode` (Core DLL을 precompiled 참조로 명시) | Test Framework | Mac 통과 |

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

서체(디자인 시스템 v8 `type.fonts`): `Coach/Resources/Fonts/`에 원본 그대로 넣었다(서브셋이나 수정 없음, OFL 예약 이름
조항). sha256은 디자인 시스템 목록과 jsdelivr 원본 둘 다와 일치한다.

| 파일 | 용도 | sha256 |
|---|---|---|
| Pretendard-Regular.otf | sans 400 (body) | 3ffbacde…1c93 |
| Pretendard-Bold.otf | sans 600/700 (headline, title, label, 버튼) | 2e91915f…1cc7 |
| JetBrainsMono-Medium.ttf | mono 500 (stat) | 31c92d01…4d3d |
| JetBrainsMono-Bold.ttf | mono 700 (score) | 5590990c…6dcb |
| Pretendard-LICENSE.txt, JetBrainsMono-OFL.txt | SIL OFL 1.1 | |

`CoachApp.ApplyFont`가 글자 단계로 파일을 고른다. 굵기는 파일이 정하므로 USS의 `-unity-font-style`은 normal이다
(Bold 파일을 다시 굵게 만들지 않기 위해서). score와 stat은 JetBrains Mono로 그리되, 한글이 섞인 문자열은 Pretendard로
그린다. 레거시 Font 에셋의 기본 가져오기 설정(Character: Dynamic)을 쓰므로, 한글 전체를 굽지 않고 그린 글자만 동적
아틀라스에 올린다. OS 폰트를 런타임에 불러오던 코드는 없앴다. 서브 표시는 글리프(●) 대신 공 색 점으로 그린다.

## Mac에서 할 일

```bash
git pull --ff-only
scripts/sync-core-to-unity.sh            # Core DLL 생성과 복사
export UNITY_EDITOR="/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity"
export TENNISSIM_CANDIDATE_REPLAY="$PWD/unity/TennisSim.UnityViewer/Assets/StreamingAssets/Replays/sample-42.json"
scripts/validate-unity-viewer.sh all     # 이제 코치 씬 생성과 Coach PlayMode 테스트도 포함
```

`TENNISSIM_CANDIDATE_REPLAY`가 없으면 리플레이 뷰어의 candidate 테스트가 Skipped가 되고,
`check-unity-test-results.py`는 skip도 실패로 보므로 EditMode에서 멈춰 PlayMode까지 가지 않는다. 코치 UI만 확인할
때는 추적 중인 고정 리플레이 `sample-42.json`을 넣는다. 이것은 candidate 검증이 아니다. 실제 candidate는
[UNITY_VALIDATION.md](UNITY_VALIDATION.md)를 따른다.

`validate-unity-viewer.sh`는 prepare 단계에서 DLL을 다시 동기화한다. compile 단계 뒤 `CoachSceneSetup.Setup`을
실행해 `TENNISSIM_COACH_SCENE_READY`를 확인하고, PlayMode에서 `TennisSim.Coach.PlayMode`도 돌린다.

Editor에서 처음 열면 새 파일들의 `.meta`가 생성된다. 그 `.meta` 파일들을 커밋한다(GUID를 고정하기 위해).
화면 확인 항목:

1. 경기 전: 칩 9개, 선택 반전, 경기 시작.
2. 경기: 점수판(15/30/40/AD), 코트 위 선수 캡슐과 공, 속도 1×/4×/16×, 일시정지, "다음 체인지오버까지".
3. 체인지오버: 이번 구간과 누적 표, 체력 줄, 상대 코치 알림(Safe로 시작하면 첫 체인지오버에 나옴), 칩과 두 버튼.
4. 리뷰: 구간 흐름, 게임 승자 줄, 착지 히트맵과 범례, 전체 기록.
5. 한글이 네모로 나오지 않는지, 굵은 글자가 이중으로 굵어지지 않는지, 숫자 자릿수가 맞는지, 1280×800 기준으로
   잘리는 곳이 없는지. 폰트 가져오기 설정이 Dynamic인지.

결과는 이 문서에 새 절로 기록한다. 확인 전에는 위 상태 줄을 바꾸지 않는다.

## Mac 확인 결과 (2026-09-24)

Unity 6000.6.0f1, Color Space Gamma. Game 뷰 1280×800, 1x(물리 픽셀 1:1). 커밋 a0a11cd 기준.

- 빌드와 테스트: `validate-unity-viewer.sh all` exit 0. 컴파일 오류 0건. `overrideReferences`를 켠 뒤에도 NUnit 참조가
  들어온다. EditMode 4/4, PlayMode 3/3 통과(`CoachPlayModeTests.PreMatchChangeoverAndReviewScreensBuild` 포함).
  `TENNISSIM_SCENE_READY`와 `TENNISSIM_COACH_SCENE_READY`를 확인했다. 증거는 Mac의
  `artifacts/unity-validation/editor-20260924T101528-el3s6h`.
- 화면 1~5: 모두 동작한다. 같은 seed로 다시 돌리면 첫 포인트들이 같다.
- 디자인 시스템 확인 항목은 모두 통과했다.
  - 서체 4개가 Dynamic이고 Include Font Data가 켜져 있다. Console에 font missing, 경고, 에러가 없다.
  - `·`와 `–`가 정상으로 나온다. 굵기가 이중으로 겹치지 않는다.
  - 16×에서 점수 칸 오른쪽 끝이 고정된다(0/15/30/40/AD).
  - ScreenCapture PNG에서 잰 여섯 색이 토큰 hex와 정확히 일치한다.
  - 포커스 때 바뀌는 픽셀은 칩 테두리 영역 안에만 있어 크기와 위치가 변하지 않는다. 선택 칩과 primary 버튼 모두 같다.
  - 1px 테두리가 선명하다. 배너, 서브권 점, StepNav, 히트맵(청록 없음, 범례 글자)이 규칙대로 나온다.
- 스크린샷 6장은 Mac 로컬 `artifacts/unity-visual/coach-20260924-mac/`에 있다(gitignore).
- 확인하지 않은 것: 2x Retina 창.

열린 레이아웃 문제. 디자인 시스템 v10의 unity.md "Mac 확인 기록"과 README "글과 배치"에 규칙이 있고, 수정은 UI
세션이 맡는다.

1. 체인지오버 오른쪽 패널의 "적용하고 계속" 버튼이 패널 밖으로 약 23px 나간다.
2. 같은 패널의 부제가 단어 중간에서 끊긴다("다음 체인/지오버"). 그 아래 "공격 방향" label과 간격이 없다.
3. 배너 아이콘이 "I"로 나온다. `tsc-label`이 대문자로 바꾸기 때문이다(`CoachApp.cs`의 `Text("i", "tsc-label", ...)`).
4. 한글이 음절 단위로 줄바꿈된다("읽/히지", "바꿉니/다").
5. 리뷰에서 한 게임짜리 구간 카드가 좁아 줄바꿈이 심하고 카드 높이가 제각각이다.
6. 리뷰 히트맵의 코트 그림 아래쪽이 잘린다. 의도한 것인지 확인해야 한다.
7. 경기 화면 아래쪽 약 35%가 비어 있다.
8. 체인지오버 제목 "Rook"과 B 배지 사이에 간격이 없다.

히트맵의 "백핸드 공략" 수는 버그가 아니다. Core는 균형 전술에서도 상대 백핸드 쪽 후보를 가중치 2로
고른다(백핸드 공략 전술은 3.6). 뷰는 `hit.Reason == "Backhand"`인 샷을 센다. 그래서 이 수는 전술이 아니라 겨냥한
방향이다. 디자인 시스템은 범례를 "백핸드 쪽 샷 / 그 외 / 아웃"으로 정했다.

## Mac 확인 뒤 수정 (커밋 6090fcc)

위 열린 레이아웃 문제 8건을 아래처럼 고쳤다(디자인 시스템 v10 "글과 배치" 절, unity.md "한글 줄바꿈").

| # | 문제 | 수정 |
|---|---|---|
| 1 | 체인지오버 버튼이 패널 밖으로 약 23px 나감 | 버튼 줄(`.tsc-button-row`)이 줄바꿈되고 오른쪽 정렬, 간격 8px, 부모 폭을 넘지 않음. 칩 줄도 오른쪽 여백을 패널 padding에 맞춤 |
| 2 | 부제와 다음 머리말 사이 간격 없음 | 원인: `.tsc-root .unity-text-element { margin: 0 }`가 간격 클래스를 덮음. 간격 클래스에 `.tsc-root` 접두어를 붙임 |
| 3 | 배너 아이콘이 "I" | 아이콘에서 `tsc-label`을 뺌. 크기는 USS, 굵기는 Bold 파일 |
| 4 | 한글 음절 줄바꿈 | 표시 문자열에서 한글과 이웃 글자 사이에 U+2060(WORD JOINER)을 넣음(`CoachText.KeepAll`). Pretendard 두 파일 모두 U+2060을 cmap에 가짐을 확인했다. Panel Text Settings의 한글 규칙은 쓰지 않았다: 여기서 검증할 수 없고, 런타임에 텍스트 설정을 새로 만들면 기본값을 잃을 위험이 있다 |
| 5 | 한 게임짜리 구간 카드가 좁음 | 카드 최소 폭 180px, 같은 높이, 긴 구간은 더 넓게, 넘치면 다음 줄로 |
| 6 | 히트맵 아래쪽 잘림 | 한쪽 코트만 보이는 것은 의도다. 뷰가 패널 높이를 채우고, 네트부터 베이스라인 바깥까지(아웃 점이 더 멀면 그만큼 넓혀서) 비율을 유지해 그림 |
| 7 | 경기 화면 아래 약 35% 빔 | 코트 뷰가 남는 세로 공간을 채우고, 비율을 유지해 가운데에 그림 |
| 8 | 제목과 B 배지 사이 간격 없음 | 글 뒤에 오는 배지(`tsc-badge--trailing`)는 왼쪽에 8px |

히트맵 범례를 "백핸드 공략"에서 "백핸드 쪽 샷"으로 바꿨다. 채운 점은 샷 선택 이유가 Backhand인 샷이다. Balanced
전술에서도 나오므로 전술 이름과 겹치면 잘못 읽힌다. 수정 후 Mac 재확인은 아직이다.
