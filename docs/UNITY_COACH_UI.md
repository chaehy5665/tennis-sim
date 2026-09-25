# Unity 코치 UI (2026-09-24)

[GAME_LOOP.md](GAME_LOOP.md) 수직 슬라이스의 UI 구현. 와이어프레임
(https://claude.ai/artifact/GshzaRGoUCtLYDMyZMayU6)과 디자인 시스템 TennisSim Coach UI
(https://claude.ai/artifact/7CHzTq4k81XYbUU416mafM, 토큰 v2)을 Unity UI Toolkit으로 옮겼다.

```text
IMPLEMENTATION_STATUS: UNITY_VERIFIED (Mac layout issues closed at 5084857)
APP_LAYER_CHECKS: PASS (tests/TennisSim.CoachChecks 14/14, C# 9, warnings as errors, actual Core)
UNITY_COMPILE_RESULT: PASS (Mac, Unity 6000.6.0f1, 2026-09-24, error CS 0)
UNITY_PLAYMODE_TEST_RESULT: PASS (EditMode 4/4, PlayMode 3/3, validate-unity-viewer.sh all exit 0)
UNITY_VISUAL_VERIFIED: true at 1280x800 1x and 2560x1600 2x for 5084857; every Mac layout issue closed (see Mac 재확인 경과)
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
4. 리뷰: 구간 흐름(카드 안 게임 승자 칩), 착지 히트맵과 범례, 전체 기록.
5. 한글이 네모로 나오지 않는지, 굵은 글자가 이중으로 굵어지지 않는지, 숫자 자릿수가 맞는지, 1280×800 기준으로
   잘리는 곳이 없는지. 폰트 가져오기 설정이 Dynamic인지.

기준 캡처는 키 입력 없이, 포커스 링이 없는 상태로 찍는다(키 입력이 UI 탐색 이벤트로 들어가 버튼에 포커스가 생긴다).
2x는 Game 뷰를 2560×1600으로 두고 본다. PanelSettings가 ScaleWithScreenSize(기준 1280×800)라 2x Retina 창과 같은 배율이다.

스크린샷은 `artifacts/unity-visual/<폴더>/`에 저장한다. 이 폴더는 Syncthing(폴더 ID `nnuye-qmgwq`, Mac Send Only,
Linux Receive Only)으로 Linux 호스트의 같은 경로에 동기화되므로 scp가 필요 없다. `artifacts/`는 gitignore 대상이다.

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
- 스크린샷 6장은 `artifacts/unity-visual/coach-20260924-mac/`에 있다(gitignore, Linux 호스트에 동기화됨).
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

## Mac 재확인 뒤 수정 (리뷰 화면 넘침)

5593cd1(c2833b7 포함) 재확인에서 10개 중 9개가 통과했다. 실패한 것은 리뷰 화면이다(seed 3, 7–6, 구간 8개): 화면이
800px을 넘치면서 UI Toolkit의 기본 `flex-shrink: 1` 때문에 요소가 눌렸다. 디자인 시스템 v14 규칙대로 고쳤다.

- 화면 본문을 세로 `ScrollView`로 감쌌고 머리 띠는 고정이다. 본문의 최소 높이를 뷰포트 높이에 맞춰, 내용이 짧을 때도
  경기 코트와 리뷰 열이 화면을 채운다.
- 패널, Inset, 배너, 표 행, 구간 카드, 범례는 `flex-shrink: 0`이다. 표 행은 `min-height: 32px`이다. 같은 줄의 패널
  (체인지오버 관찰 카드, 리뷰 전체 기록)은 `flex-basis: 0`이어서 글자가 줄바꿈되고 옆으로 넘치지 않는다.
- 구간 카드 묶음은 `align-content: flex-start`이다. 게임별 승자 칸을 각 구간 카드 아래쪽으로 옮기고, 따로 있던 승자 줄은
  없앴다.
- 버튼 줄이 줄바꿈되면(레이아웃에서 감지) `tsc-button-row--stacked`로 바뀌어 모든 버튼이 줄 폭과 같아진다. 폭이 다시
  충분해지면 원래대로 돌아간다. 리뷰 머리의 두 버튼도 같은 버튼 줄을 쓴다.
- 경기 코트는 뷰 크기에 맞춰 비율을 유지하며 커지고, 가장자리 여백은 16px(space-3)이다. 길이 방향 범위는 ±17m에서
  ±16.5m로 줄였다(기록된 선수 최대 위치는 베이스라인 뒤 3.7m, 곧 15.6m). 범위의 비율이 약 3:1이므로, 뷰가 그보다
  넓으면 위아래가 남는다.

## Mac 재확인(cb063db) 뒤 수정

스크롤, 승자 칩, 표 행, 범례, 경기 코트는 통과했다. 남은 세 가지를 디자인 시스템 v18 규칙대로 고쳤다.

- 리뷰 구간 카드: `flex-wrap`을 쓰지 않는다. Yoga가 줄바꿈된 줄 수만큼 부모 높이를 늘리지 않아 카드가 패널 밖으로
  나갔다. 이제 C#이 줄을 직접 만든다: 한 줄 카드 수 n = floor((W+8)/188), 모든 카드는 같은 폭(flex-grow 1,
  basis 0)이다. 마지막 줄 빈칸은 보이지 않는 자리 채움 요소로 채우고, n이 바뀔 때만 다시 만든다. 게임 수에 비례하던
  폭은 없앴다. 카드 안 승자 칸도 줄바꿈 대신 한 줄에 하나씩 쌓는다.
- 버튼 줄: 상태가 없는 방식으로 바꿨다. 한 번 쌓이면 돌아오지 않던 원인은 두 가지였다. (1) 늘어난 버튼 폭을 "원래 폭"으로
  기록했고, (2) 줄의 -8px margin과 내용 기준 폭 때문에 줄 폭이 자기 상태에 따라 달라졌다. 이제 버튼마다 자연 폭
  (`MeasureTextSize` + 좌우 padding + 테두리)을 한 번 재서 고정한다. 줄 폭은 부모가 정한다(열 안에서는 늘림, 행
  안에서는 flex-grow 1과 basis 0). 쌓임 = 줄 폭 < 자연 폭 합 + 8px × (버튼 수 − 1)이다. 음수 margin, layout.y 감지,
  16px 되돌림 조건은 없앴다.
- 스크롤바: 세로 8px에 화살표 버튼은 숨기고, tracker는 ground, dragger는 control-border(hover 때 line-muted),
  모서리는 4px이다. 가로 스크롤러는 숨긴다. 기본 테마 선택자보다 우선하도록 `.tsc-root` 접두어를 붙였다. 실제로
  적용되는지는 Mac에서 봐야 한다.

## Mac 재확인 경과 (2026-09-24)

모든 재확인은 Unity 6000.6.0f1, Color Space Gamma, 1x(1280×800)와 2x(2560×1600)에서 했다. 매번
`validate-unity-viewer.sh all`이 exit 0이었다(EditMode 4/4, PlayMode 3/3, 컴파일 오류 0건, font missing 0건). 1x와
2x의 레이아웃은 매번 픽셀 단위로 같았다(2x = 1x × 2). 스크린샷 폴더는 `artifacts/unity-visual/coach-20260924-mac-<커밋>/`이다.

| 확인한 커밋 | 결과 | 남은 것 |
|---|---|---|
| 5593cd1 (c2833b7 포함) | 10개 중 9개 통과. 2x에서 1px 테두리는 물리 2px로 선명하고, 포커스는 물리 4px | 리뷰 화면이 800px을 넘쳐 요소가 눌림 |
| cb063db | 스크롤, 승자 칩, 표 행 35px, 범례, 코트 뷰 채움 통과 | 구간 카드 둘째 줄이 패널 밖으로 나감. 버튼 줄이 한 번 쌓이면 돌아오지 않음. 코트 그림이 1280×800에서 약 0.7% 작아짐(가로 기준으로 맞춰지는 창에서는 16px 여백이 범위 축소보다 큼) |
| 6dd5d41 (5fd0578 포함) | 카드 8장이 패널 안에 들어감. 버튼 줄이 쌓였다 돌아오고, 1280×800으로 되돌린 화면이 처음과 픽셀 단위로 같음. 스크롤바 8px, 화살표 없음, #8c969e / hover #aab4bb. 경계에서 깜빡임 없음 | 마지막 줄 카드가 더 넓음(189 대 194/195px). 아주 좁은 폭에서 머리 띠 정보가 단계 탭과 겹침 |
| e3e5b6d (7ee100c 포함) | 모든 줄 카드 189px. 좁은 폭에서 정보가 통째로 숨고 겹침 없음 | 넓은 폭에서도 정보가 "하드코…"로 말줄임됨. 원래 몇 px 모자랐던 폭이 overflow visible이라 드러나지 않다가 ellipsis로 드러남(두 캡처의 글자 범위가 같음) |
| 5084857 | 1280×800에서 "Seed 3 · 1세트 · 하드코트"가 다 보임. 말줄임 없이 온전히 보이거나 통째로 숨음. Free Aspect 폭 823px에서 보이고 820px 이하에서 숨음. 깜빡임과 겹침 없음 | 없음 |

체인지오버 버튼이 세로로 쌓이는 경우는 확인 목록에서 뺐다. 오른쪽 패널이 360px로 고정이라 지원하는 창 폭에서는
쌓이지 않는다. 이 문서의 1~5와 디자인 시스템 확인 항목을 합쳐, Mac에서 나온 코치 UI 문제는 5084857에서 모두 닫혔다.
디자인 시스템 쪽 기록은 버전 24까지다. 정보가 숨는 경계가 이전보다 약 80px 넓은 것은 기준 폭 1280보다 훨씬 좁은 경우라 그대로 둔다.

## 체인지오버 판단 근거 (2026-09-24, 브랜치 team/coach-evidence, Mac 확인 NOT_RUN)

Mac 플레이 테스트([GAME_LOOP.md](GAME_LOOP.md) 마지막 절)에서 공격 방향과 서브 코스는 판단할 근거가 화면에 없었다.
engine v5부터 노린 쪽을 약 99% 따르므로, 효과를 점수가 아니라 타구 분포로 보여 준다. 배치와 표기는 디자인 시스템 v25
`changeover.md`를 따랐다.

- **Core `SegmentStats`**(엔진과 replay는 바뀌지 않음, seed 42 바이트 동일):
  - 노린 방향별 `AimStats`: 백핸드 쪽, 포핸드 쪽, 그 외. 타구, 위너, 내 에러, 상대의 받은 포핸드/백핸드, 상대 에러.
  - 첫 서브 코스별 `ServeCourseStats`: 서브, 첫 서브 성공, 획득.
  - 불변식 테스트: 타구 = 위너 + 에러 + 받음, 모든 랠리 타구는 방향 하나, 서브 코스 합 = 서브 포인트, 구간 합 = 경기.
- **체인지오버**:
  - 머리말에 구간 포인트 수를 보인다.
  - 공격 방향 패널: 상대 타구 비율 SplitBar(직전/이번)와 노린 방향별 개수 표.
  - 서브 코스 패널: 와이드/바디/T별 k/n. 안 쓴 코스는 "—".
  - 공격성 · 구간 비교 표: A 직전·이번, B 직전·이번. 직전 값은 line-muted. 평균 랠리는 구간 값이므로 표 대신 아래
    한 줄("평균 랠리 · 직전 6.0구 → 이번 7.3구")로 쓴다.
  - 표본 기준(6포인트, 12구) 미만인 패널에는 SampleTag "참고용"을 달고, 그런 행은 line-muted로 쓴다.
  - 관찰 카드는 없앴다.
  - 버튼: 변경이 없으면 "그대로 계속" 하나, 있으면 "변경 취소"와 "적용하고 계속". 요약에는 바뀐 축만 쓴다.
- **리뷰**:
  - "상대 코치의 변경" 패널: 게임 위치, 바뀐 축, Core `CoachReason`에서 읽은 이유.
  - 히트맵: 선수 색을 없앴다. 백핸드 쪽은 채운 원, 그 외는 빈 원, 아웃은 흰 빈 원이다. 필터 칩(전체 / 백핸드
    공략일 때 / 양쪽일 때, 쓴 것만)과 "백핸드 쪽 k/n구 · %" 범례를 둔다.
- **검증**: Core 73/73, CoachChecks 17/17(패널 값이 SegmentStats와 같음, 직전 열, 표본 표시, 요약 문구, 리뷰 변경
  목록, 필터 분할). Unity 컴파일과 화면은 NOT_RUN이다.

Mac에서 확인할 것:

1. 컴파일과 EditMode/PlayMode.
2. 체인지오버 첫 번째(직전 없음)와 두 번째 이후(직전 있음).
   - SplitBar 두 줄, 공격 방향과 서브 코스 두 패널이 나란히 있는지.
   - 비교 표 4열과 Badge 머리글.
   - "참고용" 태그와 muted 행.
3. 배너가 있을 때 비교 표 아래쪽 스크롤.
4. 버튼 전환: 칩을 바꾸면 "변경 취소"와 "적용하고 계속"이 나오고, "변경 취소"를 누르면 칩이 되돌아가며
   "그대로 계속" 하나만 남는지.
5. 리뷰의 "상대 코치의 변경" 패널.
6. 히트맵 필터 칩 전환과 범례 숫자. 점 색에 선수 색이 없는지.

## 서브 코스 패널의 상대 리턴 위치 (2026-09-25, 브랜치 team/serve-signal, Mac 확인 NOT_RUN)

engine v6부터 서브를 받는 선수는 서버의 최근 서브 코스 쪽으로 최대 1 m 옮겨 선다. 서브 코스를 바꾼 효과는 구간 점수로는
보이지 않으므로, 이 위치를 "상대가 내 서브를 읽고 있다"는 신호로 보인다.

- Core `SegmentStats`: 서버별로 받는 선수의 와이드 쪽 이동(각 포인트 첫 서브 준비 때 |x| − `MatchEngine.ReceiverServeX`,
  와이드 쪽이 +)의 합과 표본 수, 평균. Core 테스트로 확인한다: 기록된 이동이 서브 이력에서 다시 계산한
  `ServeLean × (Wide 비율 − T 비율)`과 매 포인트 같고, 구간 합이 경기와 같다.
- 화면: 디자인 시스템 v34 changeover.md "2. 서브 코스 패널". 서브 표 아래 한 줄로 "상대 리턴 위치 · 와이드 쪽 0.8 m
  (내 서브 7포인트)"를 쓰고, 두 번째 체인지오버부터는 "직전 … → 이번 …"이다. 0.1 m 미만은 "가운데", 이번 구간에 내
  서브가 없으면 "—"이다. 글자는 line이고, 내 서브 6포인트 미만이면 line-muted다. 내 리턴 위치 줄은 결정과 이어지지
  않으므로 두지 않는다.

## Linux에서의 Runtime 컴파일 검사 (tests/TennisSim.RuntimeCompileCheck)

`Coach/Runtime`은 원래 Unity Editor(Mac)만 컴파일했다. 그래서 마일스톤 2가 `HalfCourtView.Set` 누락(CS1061)으로
Mac에서 멈췄다. 이제 이 프로젝트가 `Coach/App`과 `Coach/Runtime` 소스를 Unity 6과 같은 C# 9로, 최소한의
UnityEngine/UIElements 스텁(`UnityStubs.cs`)에 대고 컴파일한다. 솔루션에 들어 있으므로 `dotnet build TennisSim.sln`이
매번 이 검사를 한다. cf913dc 소스로 빌드하면 Mac과 같은 CS1061이 Linux에서 난다.

**한계: 스텁은 Unity가 아니다.**

- 잡는 것: 우리 코드끼리의 오류. 없는 멤버를 부르는 경우, 인자 수나 타입이 맞지 않는 경우, 이름 충돌, C# 9에 없는
  문법, 두 번 이어지는 암시적 변환 같은 것이다.
- 잡지 못하는 것: Unity API 자체의 오류. 스텁에 Unity에 없는 멤버나 다른 시그니처가 들어 있으면 Linux에서는 통과하고
  Mac에서 실패한다. 그 밖에 USS 해석, 레이아웃, 폰트, 렌더링 같은 실행 결과도 잡지 못한다.
- 따라서 **Unity API의 권위는 여전히 Mac 컴파일이다.** Linux 검사가 통과해도 Mac 확인 목록의 컴파일 항목을 빼지 않는다.

스텁에 멤버를 추가하는 규칙:

1. Unity 6 스크립팅 레퍼런스에 실제로 있는 멤버와 시그니처만 넣는다. 반환형, 매개변수, 제네릭 제약, 암시적 변환을
   포함한다. 확인할 수 없으면 넣지 말고 그 호출을 Mac 확인 목록에 올린다.
2. 본문은 비워 둔다(컴파일만 하고 실행하지 않는다).
3. 커밋 메시지에 추가한 멤버를 적는다(예: "stubs: +VisualElement.Children(), +IResolvedStyle.marginLeft"). Mac에서
   어긋나면 그 목록으로 추적한다.

처음 스텁(3a9259c)이 담은 멤버는 UnityStubs.cs 그대로다. Mac 마일스톤 2까지 실제 Unity에서 컴파일된 호출만 모델로 삼았다.


## 플레이 테스트 2 UX 8건 (2026-09-25, 브랜치 team/ux, Mac 확인 NOT_RUN)

디자인 시스템 v37/v38 changeover.md "표본 크기", "2. 서브 코스 패널", "3. 공격성 · 구간 비교 표", "버튼 상태" 규칙이다.
Mac에서 볼 항목은 [MAC_MILESTONE.md](MAC_MILESTONE.md) "마일스톤 3 확인 항목"에 있다.

- 뷰 모델(`CoachViews`): 흐림은 값에만 준다. 패널이 기준 미만이면 `EvidencePanel.MuteAll()`로 그 표본(이번 구간)의
  값, SplitBar 글자, 아래 줄을 모두 흐리고, 아니면 행이나 묶음 단위로만 흐린다. 경기 누적 묶음은 다른 표본이라 태그와
  상관없이 누적 6포인트 기준만 따른다(디자인 시스템 v39). Runtime은 플래그대로 그릴 뿐 판단하지 않는다.
  아래 줄은 `Notes`(줄마다 `Muted`)로 바뀌었다. 표 머리글 묶음은 `Groups`, 72px 칸은 `Compact`, 두 줄 항목 이름은
  `EvidenceRow.Note`다.
- 서브 코스: 이번 구간과 경기 누적 두 묶음, 각 "첫 서브"(들어간 첫 서브/서브)와 "득점"(딴 포인트/서브). 분모가 서브
  수라 "서브" 열은 없앴다. 첫 체인지오버는 두 값이 같으므로 "이번 구간"만 둔다.
- 체력: Core `SegmentPlayerStats.EnergyMin`(구간 frames와 사건 상태의 최저 체력). 속도 줄은 `Movement.SpeedLimit`의
  최저 체력 대 체력 1 비율을 정수 %로 쓴다. 엔진 동작은 바뀌지 않았다(seed 42 기록 바이트 동일).
- 칩 설명: 경기 전 칩과 체인지오버 설명 상자가 `CoachViews.TacticGroups` 한 곳의 문장을 쓴다. 체인지오버 칩은 설명을
  칩 안에 넣지 않고 상자에만 보인다(마우스 → 포커스 → 마지막 클릭 순).
- 변경 요약과 상대 코치 배너, 리뷰의 변경 목록에서 서브 축 이름을 칩 묶음과 같은 "서브 코스"로 바꿨다.
