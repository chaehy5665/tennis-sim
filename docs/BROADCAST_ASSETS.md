# 2.5D 중계 화면 에셋 사양

상태: **Linux 검증 완료, Unity 미연결(NOT_RUN)**. 게임 루프 판정이 통과한 뒤 마일스톤 3에서 Unity 씬에 연결한다. 이 문서는 무엇을, 어떤 크기와 색으로, 어떤 순서로 그리는지를 정한다. 디자인 규칙과 시안은 디자인 시스템(TennisSim Coach UI)의 "2.5D 중계 화면" 절(broadcast.md)과 BroadcastView 카드에 있다.

## 원칙

- 렌더러는 Core 기록(`frames`, 사건)을 읽기만 한다. 크기 과장(공 11px, 배지), 카메라, 동작 모양만 정하고 위치, 타격 시각, 도달 여부, 점수는 기록 그대로 따른다([REPLAY_CONTRACT.md](REPLAY_CONTRACT.md), AGENTS.md).
- 숫자의 출처는 코드 한 곳이다. 코트 치수는 Core `Court`(`src/TennisSim.Core/Geometry.cs`), 표시용 크기와 시간은 `BroadcastSpec`(`unity/TennisSim.UnityViewer/Assets/TennisSim/Coach/App/BroadcastSpec.cs`)이다. 이 문서에는 이름과 뜻만 적고, 값은 코드에서 읽는다.
- 색은 디자인 시스템 토큰 이름으로 적는다(`court`, `line`, `ball-shadow`, `hud-plate` 등). hex는 토큰 파일과 USS가 가진다.

## 좌표와 화면

- 월드: 원점은 지면의 네트 중앙, x 가로, y 위, z 코트 길이, 단위 m(계약과 같다).
- 화면: 코치 UI 기준 해상도 `ScreenWidth` × `ScreenHeight`, 원점 왼쪽 위, y 아래로. 위쪽 `HeaderHeight`는 코치 UI 머리 띠이고 3D 뷰포트는 그 아래다(`BroadcastSpec.Viewport`).
- 안전 영역: 뷰포트 안쪽 `SafeInset`(`BroadcastSpec.SafeArea`). 모든 HUD 판과 코트 라인은 이 안에 있다.
- 투영: `BroadcastCamera`(같은 폴더)가 핀홀 투영으로 월드 → 화면 px를 계산한다. Unity 카메라를 쓰지 않는 순수 C#이라 Linux에서 검사한다. 마일스톤 3에서 Unity 카메라를 같은 값으로 두고 두 결과를 대조한다.

## 카메라

| 항목 | 값의 출처 | 뜻 |
|---|---|---|
| 위치 | `BroadcastSpec.CameraPosition` | 가까운 엔드 뒤 높은 곳, 코트 중앙선 위 |
| 바라보는 점 | `BroadcastSpec.CameraTarget` | 가까운 서비스 박스 쪽 지면 |
| 세로 화각 | `BroadcastSpec.VerticalFovDegrees` | 도 |
| 담는 깊이 | `BroadcastSpec.FramedDepth` | 네트에서 이만큼 떨어진 선수까지 화면에 담는다 |

- 카메라는 한쪽 끝에 고정한다. 선수가 엔드를 바꾸면 A가 위에도 아래에도 온다. 구분은 배지로 한다.
- **디자인 시스템 v29 시안에서 바뀐 점**: v29는 위치 (0, 13, −34), 바라보는 점 (0, 0, −3), 28°였다. 실제 engine v6 기록(seed 42)에서 선수가 네트에서 |z| 18.9 m까지 물러나(프레임의 약 0.9%가 17 m 너머) 가까운 쪽 선수가 조작 막대 아래로, 먼 쪽 배지가 화면 밖으로 나갔다. 그래서 19 m 떨어진 선수의 발 그림자가 조작 막대 위에, 배지가 안전 영역 안에 오도록 다시 잡았다. 대가로 가까운 베이스라인이 약 15% 짧아졌다(483 → 410 px).
- 지금 값에서의 기준 위치(검사가 1 px 안에서 확인한다): 가까운 베이스라인 y≈527, 먼 베이스라인 y≈269, 네트에서 19 m 떨어진 가까운 선수의 발 y≈693, 먼 선수의 머리 y≈181.

## 에셋 목록 (뒤에서 앞으로 그리는 순서)

| # | 에셋 | 고정 | 모양과 크기 | 색 토큰 | 출처 |
|---|---|---|---|---|---|
| 1 | 바탕 | 화면 | 뷰포트 전체 | `ground` | — |
| 2 | 코트 면(런오프 포함) | 월드 | 지면 사각형, 반폭 `RunOffHalfWidth`, 반길이 `RunOffHalfLength` | `court` | `BroadcastCourt.RunOffCorners` |
| 3 | 코트 라인 | 월드 | 베이스라인 2, 단식 사이드라인 2, 서비스 라인 2, 센터 서비스 라인, 센터 마크 2(10 cm). 두께는 화면 px로 깊이에 따라 `LineWidth(depth)`(가까운 베이스라인 약 2 px, 먼 쪽 약 1.2 px, 최소 `MinLineWidth`) | `line` | `Court.HalfWidth`, `Court.HalfLength`, `Court.ServiceLine` → `BroadcastCourt.Lines()` |
| 4 | 선수 그림자 | 월드 위치, 크기는 캡슐 기준 | 지면 타원. 가로 반지름 = 캡슐 반폭 × `PlayerShadowWidthScale`, 세로 = 가로 × `PlayerShadowDepthScale` | `ball-shadow` | `BroadcastCamera.Player` |
| 5 | 공 그림자 | 월드 위치, 크기는 화면 | 공 바로 아래 지면(y=0)의 타원 `BallShadowWidth` × `BallShadowHeight` px | `ball-shadow` | `BroadcastCamera.Ball` |
| 6 | 네트 너머의 선수·공 | — | 아래 7~9와 같은 모양. 네트 평면(z=0)보다 멀면 네트보다 먼저 그린다 | — | 깊이 정렬 |
| 7 | 네트 몸체, 윗줄, 기둥 | 월드 | 몸체: 기둥 사이 지면부터 높이 프로필까지(35%). 윗줄: 2 px. 기둥: x = ±`NetPostX`, 3 px | 몸체 `net`, 윗줄 `line`, 기둥 `net` | `Court.NetHeight(x)` → `BroadcastCourt.NetTape()` |
| 8 | 선수 캡슐 | 월드 발 위치, 늘 카메라를 향함 | 높이 `PlayerHeight`, 폭 `PlayerWidth`(m), 양 끝 둥글게 | `player-a` / `player-b` | `frames` 선수 위치 |
| 9 | 공 | 월드 중심, 크기는 화면 | 지름 `BallDiameter` px, 테두리 `BallOutline` px | 채움 `ball`, 테두리 `ground` | `frames` 공 위치 |
| 10 | 공 높이 보조선 | 월드 | 공과 그림자를 잇는 1 px 점선(70%). 공 높이가 `BallHeightGuideAbove`를 넘을 때만 | `ball` | `BroadcastCamera.Ball().HeightGuide` |
| 11 | A/B 배지 | 화면 크기, 머리 위에 붙음 | 지름 `BadgeSize` px, 머리 위 `BadgeGap` px | Badge 컴포넌트(`player-a`/`player-b`, 글자 `on-selected`) | `BroadcastCamera.Player().Badge` |
| 12 | HUD 판 | 화면 | 점수판 `Scoreboard`, 현재 전술 `CurrentTactic`, 직전 포인트 `LastPoint`, 조작 막대 `ControlBar` | 바탕 `hud-plate`, 글자 `line`/`line-muted`, 점수 `score-hud` | `BroadcastSpec.HudPlates` |
| 13 | 머리 띠 | 화면 | 코치 UI의 StepNav 그대로 | 기존 토큰 | 코치 UI |

- 깊이: 2~5는 지면이라 늘 가장 뒤다. 6~10은 카메라 깊이(`ScreenPoint.Depth`)로 정렬하고, 네트는 z=0 평면의 깊이로 친다. 11~13은 장면 위에 늘 맨 앞이다.
- HUD 판은 코트 단식 사다리꼴과 겹치지 않는다(검사). 장면 위에 글자를 바로 쓰지 않는다.
- 공만 크기를 키운다. 선수 캡슐은 실제 치수를 원근 그대로 그린다.

## 동작 상태

`BroadcastMotion.Classify`(같은 폴더)가 기록 사건을 차례로 읽어 선수마다 9가지 상태의 구간(`MotionSpan`)을 만든다. 3D 사람 모양 단계의 준비물이며, 2.5D 캡슐 단계에서는 그리지 않는다.

| 상태 | 시작 | 끝 |
|---|---|---|
| 재배치 | `PlayersRepositioned` | 같은 시각(길이 0). 렌더러는 여기서 컷한다(`MotionTrack.Cuts`) |
| 서브 | 재배치 직후의 서버 | 서버의 `BallHit`(Serve) |
| 대기 | 재배치 직후의 받는 선수, `ServeFault` 뒤 | 다음 상태 |
| 이동 | `ContactPrepared` | 준비 시작 |
| 준비 | 예정 타격 시각 − `PrepareLead` | `BallHit` |
| 타격 | `BallHit`(접촉 프레임 = 사건 시각) | + `FollowThrough` |
| 회복 | 팔로스루 끝 | 다음 `ContactPrepared` 또는 포인트 끝 |
| 놓침 | 닿을 수 없는 공: 예정 시각 − `PrepareLead`. 닿을 수 있던 공: 예정 시각 + `MissGrace`까지 `BallHit`이 없을 때 | `PointEnded` |
| 포인트 뒤 | `PointEnded` | 다음 재배치 |

- 기록에서 확인한 것(engine v6, seed 42):
  - `ServeStarted`와 서브 `BallHit`은 같은 시각이다. 그래서 서브 동작은 `ServeStarted`가 아니라 재배치에서 시작한다. 디자인 시스템 v30 표의 "서브 시작 = ServeStarted"는 이렇게 고쳤다.
  - 받는 선수가 준비했는데 공이 아웃이면 그 선수가 득점한다. 이때는 놓침이 아니라 포인트 뒤로 간다. 예정 시각 전에 포인트가 끝나기 때문이다.
  - 예정 스트로크(`ContactPrepared.stroke`)는 랠리 타격 344번 모두에서 실제와 같았다.
- 모든 전환은 그 시각까지의 사건만으로 정한다. 경기 중 렌더러가 현재 재생 시각을 `upTo`로 넘기면, 끝난 기록을 한 번에 읽은 것과 같은 상태를 얻는다(검사).

## 검사

`tests/TennisSim.CoachChecks`에 들어 있다. Linux에서 돌린다.

```bash
export PATH="/home/c10/projects/tennis/.tools/dotnet:$PATH"
dotnet run --project src/TennisSim.Cli --no-build -- match --seed 42 --quiet --out /tmp/seed-42.json
TENNISSIM_BROADCAST_REPLAY=/tmp/seed-42.json dotnet run --project tests/TennisSim.CoachChecks --no-build
```

환경 변수가 없으면 여기서 seed 42 코치 세션을 직접 돌린 기록을 쓴다. 두 경우 모두 통과해야 한다.

| 검사 | 확인하는 것 |
|---|---|
| Broadcast: court corners, lines and net stand inside the title-safe area | 단식 네 귀퉁이, 모든 라인 끝, 네트 윗줄, 기둥 밑이 안전 영역 안 |
| Broadcast: the far baseline looks shorter and higher than the near one | 먼 베이스라인이 더 짧고 위에 있음, 베이스라인이 수평, 코트가 가운데 |
| Broadcast: the numbers in the design system match the camera | 기준 위치 4개(±1 px), 19 m 선수의 발 그림자가 조작 막대 위, 배지가 안전 영역 안, 라인 두께 |
| Broadcast: the ball keeps its recorded place, a fixed 11 px size and a shadow straight below | 공 중심 = 기록 투영, 그림자 = 바로 아래 지면 투영이고 화면에서 공 아래, 지름 11 px, 보조선은 0.3 m 초과일 때만, 실제 크기면 먼 쪽에서 3 px 미만 |
| Broadcast: HUD plates sit inside the safe area and off the court corridor | 판 4개가 안전 영역 안에 있고 단식 사다리꼴과 겹치지 않음 |
| Broadcast: recorded players and their badges stay in view; the ball is counted | 기록된 모든 프레임에서 선수 발과 배지가 뷰포트 안. 공이 뷰포트를 벗어나는 프레임은 1% 이하(seed 42 CLI: 5302 중 6) |
| Motion: every player's spans are ordered and leave no gaps | 구간이 빈틈없이 이어짐 |
| Motion: the contact frame is the BallHit time, and there is no strike without a BallHit | 모든 `BallHit` 시각에 타격 상태이고 접촉 시각이 같음. 타격 구간 수 = `BallHit` 수. CLI 기록과 코치 세션 기록 둘 다 |
| Motion: PlayersRepositioned is a cut, never a walk | 모든 재배치 시각에 컷, 길이 0, 어떤 구간도 컷을 가로지르지 않음 |
| Motion: an unreachable contact shows no strike; a missed one only reaches | 닿을 수 없는 공에 타격 없음. 실제로 친 공은 그 전에 놓침을 보이지 않음 |
| Motion: the serve winds up from the reset, and the prepared stroke mostly matches the one played | 서브 타격 직전이 재배치에서 시작한 서브 구간. 랠리 타격마다 예정 스트로크가 있고 95% 이상 일치 |
| Motion: a live renderer sees the same states as the finished replay | 사건을 앞에서부터 잘라 읽어도 그 시각까지의 상태가 같음 |
| Motion: the broadcast layer leaves the record untouched | 투영과 분류 뒤에도 기록 JSON이 바이트 단위로 같음 |

## 마일스톤 3에서 Unity 쪽에 남는 일

- 씬과 프리팹: 코트 면, 라인(깊이에 따른 두께), 네트, 캡슐, 그림자, 공, 배지, HUD 판. 이 문서의 그리는 순서를 따른다.
- Unity 카메라를 `BroadcastSpec` 값(위치, 바라보는 점, 세로 화각)으로 두고, 몇 점을 `BroadcastCamera.Project`와 대조하는 PlayMode 검사.
- `BroadcastMotion.Classify(events, upTo: 재생 시각)`를 매 프레임(또는 사건이 올 때) 불러 선수 상태를 읽는 연결. 2.5D에서는 상태를 그리지 않고 컷만 쓴다(재배치에서 보간하지 않기).
- "중계 · 탑뷰" 칩과 탑뷰 전환, 직전 포인트 판 3초 표시, 조작 막대.
- 새 C# 파일의 `.meta`는 Mac에서 Unity가 처음 열 때 생긴다. 그때 커밋한다.
- Mac 1280×800과 2x에서 화면 확인.

## 한계

- 2.5D는 조명과 사람 모양이 없다. 동작 상태는 3D 단계의 준비물이고 아직 그림으로 확인하지 않았다.
- 카메라 틀은 seed 42(CLI 기본 선수)와 seed 42 코치 세션 두 기록으로 맞췄다. 다른 선수 조합이 더 멀리 물러나면 `FramedDepth`를 다시 봐야 한다. 검사가 그 경우 실패한다.
- 공은 두 기록에서 프레임의 약 0.1~0.3%가 뷰포트 밖이다(높은 로브나 코트 밖 멀리 나간 공). 그대로 둔다.
- 준비 시간(`PrepareLead`), 팔로스루, 놓침 여유는 디자인 값이고 실제 동작과 대조하지 않았다.
- 배속에서 동작 길이를 줄이는 규칙은 분류기에 없다. 렌더러가 재생 속도에 맞춰 줄인다.
