# 리플레이 계약 v1.0

`TennisSim.Core`의 `MatchRecord`는 파일/JSON을 모르고 데이터만 제공한다. CLI의 `ReplayJson`이 camelCase JSON과 enum 문자열로 저장한다. 현재 schema는 `1.0`, 현재 엔진 식별자는 `tennissim-mvp-2`이며 원본 golden fixture는 `tennissim-mvp-1`이다. 동작 변경 시 엔진 버전을 올려야 한다. Git commit만으로 dirty candidate를 식별하지 않고 calibration source manifest와 수동 엔진 버전을 함께 사용한다.

## 최상위 데이터

| 필드 | 의미 |
|---|---|
| schemaVersion / engineVersion | 데이터 형식과 엔진 동작 버전 |
| rules / realismCalibrated | 고정 단식 한 세트 규칙과 보정 여부(false) |
| input | 초기 seed, 모든 SimConfig 값, 선수 프로필, 초기 전술, requestedTick 기반 지시 목록 |
| instructionHistory | 각 지시의 requestedTick, appliedTick, appliedPoint. 미적용=-1 |
| events | sequence 오름차순 확정 사건. 동일 time도 순서 유지 |
| frames | 기본 12ticks(0.1s) 간격 표시용 샘플 상태와 종료 상태 |
| finalScore / stats | 종료 시 점수, 선수/랠리/종료 사유 통계 |
| status / diagnostic | Completed, PointBatchComplete, SimulationLimitExceeded; 제한 원인 |
| finalRandomState | 진단용 xorshift32 상태. 중간 저장 재개 계약이 아님 |

난수는 xorshift32(shift 13/17/5), uint32 상태. seed=0은 0x6D2B79F5로 치환한다. 균등수는 uint/2³². 전역 Random이나 시각 seed를 사용하지 않는다. 컬렉션 생성/순회와 동일 tick 지시 순서를 고정한다. 동일 코드·입력·런타임에서 tick 묶음을 달리해도 전체 JSON이 일치하는지 테스트한다. Linux x64와 Mac의 seed 42에서는 outcome과 허용오차 기반 의미 데이터가 일치했지만 `Math.Exp` 경로 이후 전체 bytes는 달랐다. 판정 범위와 허용오차는 [DETERMINISM.md](DETERMINISM.md)를 따른다.

## 상태

모든 FrameState는 tick(현재 처리하는 구간의 정수 인덱스), time(s), phase, point(1부터), serveAttempt, score, players, ball, tactics를 갖는다. tick은 사건 발생 시각의 floor를 반드시 의미하지 않는다. tick 끝 타격은 `(tick+1)*dt`에 발생할 수 있다. **재생 시간의 기준은 time**이다.

선수는 ID, 현재 End, Position, Velocity, Facing, Energy. 공은 Position, Velocity, 이번 타격 이후 Bounces, NetTouched, CrossedNet. Score는 Games/Points, Server, TieBreak, EndA, Complete, Winner, Display, PointsPlayed다. Winner는 미완료 -1, 완료 선수 인덱스 0/1이다. phase는 BetweenPoints, ServePreparation, Rally, ServeRetry, Complete. 규칙상 다음 엔드 변경과 실제 선수 재배치는 분리한다.

## 사건과 동작 연결

모든 사건에 sequence, time, point, playerId(해당 시), actionId(해당 시), 전체 State가 포함된다. 해당 없는 actionId는 0이다.

- `PlayersRepositioned`: 서브 준비에서의 명시적 위치/속도 초기화. 이 사건을 가로질러 걸어간 것으로 보간하지 않는다.
- `PointStarted`, `ServeStarted`: 포인트/서브 시작. 서브 토스/실제 라켓 애니메이션은 없다.
- `ContactPrepared`: 반응 시간이 지난 뒤 현재 관측으로 계산한 목표 몸 위치 및 예상 타격 시각(`predictedContactTime`). 예정일 뿐, 타격 보장이 아니다. reason=PredictedReachable 또는 UnreachableContact. 실제 타격과 같은 actionId를 가진다. 놓친 공에는 BallHit이 뒤따르지 않는다.
- `ShotPlanned`: 실제 타점에서의 확정 선택, 후보별 target/launchVelocity/flightSeconds/weight/feasible/rejection. 목표는 intendedTarget, 선택 후보는 reason. 접촉 직전에 나오는 결정이므로 선행 애니메이션 예고로 쓰지 않는다.
- `BallHit`: actionId, playerId, ShotKind(Serve/Return/Groundstroke), Stroke(Serve/Forehand/Backhand), IntendedTarget, PreparationQuality. State.Ball.Position/Velocity는 실제 타점과 타격 직후 속도. State.Players에서 몸 위치와 방향을 얻는다. Before는 타격 직전 상태다.
- `BallBounced`, `NetTouched`: 보간 계산된 충돌 time, 충돌 직전 Before/직후 State. 위치는 연속이고 속도는 impulse로 불연속. 첫 착지는 BallBounced의 Bounces=1, actual position은 State.Ball.Position이다. 해당 타격의 actionId를 유지한다.
- `ServeFault`, `ServeLet`: 해당 서브 결과. 렛은 다음 시도의 attempt를 증가시키지 않는다. 폴트 판정은 실제 첫 착지에 기반한다.
- `PointEnded`: playerId는 승자, reason은 구조화된 종료 코드. 마지막 샷 actionId로 연결한다. State.Score는 득점 전.
- `ScoreChanged`: 같은 point/time에서 득점 후 Score. `EndsChanged`는 다음 포인트 엔드가 바뀌었음을 알린다. 실제 선수 좌표는 뒤의 PlayersRepositioned에서 변경된다.
- `TacticsApplied`: 다음 PointStarted 전에 적용된 전술을 State.Tactics에서 읽는다. 요청/적용의 대응은 instructionHistory.
- `SimulationLimitExceeded`: 진단과 기존 상태. 표시 클라이언트는 승자를 생성하면 안 된다.

## 표시와 재생

A. **재시뮬레이션**은 저장된 input을 새 MatchEngine에 넣는다. `resimulate` 명령은 현재 엔진 버전을 확인하고 전체 record 일치를 검사한다. 지시 이력의 요청 시각을 재사용하며 기록된 적용 시점은 재계산된다.

B. **기록 재생**은 Core를 다시 돌리지 않는다. `replay` 명령은 저장된 PointEnded/ScoreChanged를 텍스트로 표시한다. 미래 3D 클라이언트는 frames와 사건 상태를 함께 사용한다. 현재 CLI에는 3D 재생기나 시각적 보간 구현은 없다.

불연속 사건을 샘플 frame 사이에 삽입해 구간을 나눈다. 사건 시각 직전까지는 Before, 정확히 사건 시각 이후는 State를 사용한다. BallHit/바운드/네트 접촉을 가로지르는 단일 위치/속도 보간을 금지한다. 같은 시각의 사건은 sequence대로 적용한다. 재배치 이전/이후 상태도 나누며, 서브 준비 동안 공은 대기 상태로 표시한다. 포인트 종료 뒤 남은 tick 구간은 종료 위치에서 정지한다.

ContactPrepared는 3D 모션 준비의 단서이며 실제 접촉은 BallHit에 맞춘다. 실제 라켓 접촉, 발 디딤, 캡슐 충돌, TV 카메라, 재생 배속은 아직 검증되지 않았다.

렌더러는 카메라, 표시용 보간, 효과, 경기 시간에 맞춘 애니메이션만 정한다. 공 판정, 타격 시각, 도달 여부, 점수는 Core 사건을 그대로 따른다. 1배/4배 재생은 재생 커서 속도만 바꾸며 Core tickSeconds를 바꾸지 않는다.

이 기록은 완료된 경기의 표시 데이터다. 중간 시점 저장/재개에는 RNG뿐 아니라 규칙 내부 상태, 준비 타이머, 예측 목표, 대기 지시, active action과 tick 상태 등이 필요하며 현재 지원하지 않는다. 공개 record는 전달용 DTO이며 동작 중인 엔진의 Record/Input을 외부에서 수정하는 사용법은 지원하지 않는다.

## Unity Viewer 어댑터 — 2026-09-14 추가

위 기록 형식 **schemaVersion=1.0**과 **engineVersion=tennissim-mvp-1 또는 tennissim-mvp-2**를 직접 읽는다. 새 schema/CLI 옵션/viewer 전용 출력은 없다. 원본 JSON의 전체 input/config/instructions 및 도메인 사건은 그대로 유지된다. 샘플 준비는 원본 파일을 바이트 그대로 복사한다. EngineVersion을 검사하므로 지원하지 않는 엔진의 좌표를 추측해 표시하지 않는다.

`unity/TennisSim.UnityViewer/Assets/TennisSim/Runtime/Data`는 Unity/Core에 의존하지 않는 **표시 전용 어댑터**다. StrictJson → ReplayLoader → ReplayValidator → ReplayTimeline → ReplayStateSampler → ReplayController 순서이며, ReplayPresenter만 Unity 오브젝트를 다룬다. Unity에서 재시뮬레이션하지 않는다.

### 읽는 필수 필드

- root: schemaVersion, engineVersion, input, frames, events, finalScore, status, diagnostic.
- input: seed(uint32), config.tickSeconds(양의 초), players[2].id(고유 ID, 상태 배열과 같은 순서).
- state/frame: time(경기 시작 기준 초), point(1부터), phase, score, players[2].id/position, ball.position.
- position: x/y/z 모두 필수 유한 숫자, 단위 m. 원점은 지면의 네트 중앙; X=좌우, Y=높이, Z=코트 길이. A/B는 엔드를 바꿀 수 있다. 선수 position은 발 기준, 공은 중심이다. 변환은 CoordinateMapper의 identity mapping이다.
- score: display, pointsPlayed, games[2], complete, winner. 점수 규칙을 해석해 다시 계산하지 않고 display와 최종 결과를 표시한다.
- event: sequence(0부터 연속), time, point, kind, playerId, reason, state, before 키. before는 일반 사건에서 null 가능; BallHit/BallBounced/NetTouched에는 필수 상태.
- 사용하지 않는 기존 필드(전술 후보, 통계 등)는 JSON 문법 검사를 거쳐 건너뛴다. 파일 자체에는 유지된다. loader는 전체 원본의 모든 의미 필드에 대한 범용 엔진 validator가 아니다.

필수 키가 없거나 타입이 다르면 FormatException으로 거부한다. 숫자 기본값 0을 채우지 않는다. 중복 JSON 키, 잘린 파일, 뒤따르는 garbage, NaN/Infinity/overflow, 시간 역행, 사건 순서/point/score 불일치, 마지막 결과 불일치, 미지원 형식/엔진/종료 status를 거부한다. 최대 JSON nesting은 64다. 지원 종료 status는 Completed/PointBatchComplete/SimulationLimitExceeded이며 미완료 승자는 만들지 않는다.

### 시간축과 표시 선택

시간은 point-local이 아닌 **전체 경기 초(double)**다. 별도 포인트 오프셋은 필요 없다. tick은 시간 정렬 기준이 아니다. 샘플 마지막 시각은 611.625초다. frame은 기본 0.1초 간격이고 사건은 실제 기록된 시각(충돌 sub-tick 포함)이다.

1. 모든 frame과 사건을 time으로 합친다. 동일 time은 frame의 원래 순서 → 사건 sequence 순서다. 이전 tick의 frame 뒤 다음 tick 사건이 기록될 수 있기 때문이다.
2. 고유 time마다 Before/After를 만든다. 정확히 그 시각에는 **마지막 사건의 State**, 사건이 없으면 마지막 frame을 선택한다. 접촉 시각으로 접근할 때는 첫 접촉 사건의 Before를 사용한다.
3. 구간 내부 위치만 선형 보간한다. 점수/point/phase는 이전 확정 상태를 유지한다. 타격/바운드/네트는 반드시 구간 경계다. 포인트 변경/PlayersRepositioned를 향하는 구간은 이전 위치를 유지하다 정확한 시각에 점프한다. BetweenPoints/ServeRetry는 정지한다. 스무딩·포물선·외삽은 없다.
4. 실제 샘플의 같은 시각 frame/event 중 위치가 다른 frame은 49개이며, 리셋 때문에 차이가 클 수 있다(최대 약 37.78m). 이를 연속 이동으로 채우지 않는다. 따라서 **동시각 이전 frame과 이후 event를 동시에 만족시킨다는 주장은 하지 않는다**. 테스트도 마지막 사건 우선 계약과 원래 frame이 우선되는 나머지 시각을 각각 확인한다.
5. 정방향 이벤트 구간은 `(직전 시각, 현재 시각]`. Restart 직후 첫 Advance만 시작 시각 사건도 포함한다. 끝 사건도 포함하며 한 프레임에 통과한 모든 사건을 sequence 순으로 반환한다. Pause/Seek는 사건을 재발행하지 않는다. 같은 시각 반복 Advance에서 중복되지 않는다.
6. Seek는 과거 재생 순서와 무관하게 좌표/점수/마커를 재구성한다. 사건 탐색은 개별 sequence를 선택하되 표시 상태는 같은 시각의 최종 상태다. 마커는 현재 point에서 선택 사건 또는 커서까지의 가장 최근 타격/바운드/네트 하나다.

엔진 위치 비교 허용 오차는 **1e-7m**, Unity float Transform 비교는 **1e-6m**다. 확대 공은 지름만 3배이며 중심/사건 위치는 동일하다. 표시 해상도는 엔진 타격의 최대 8.33ms 시간 해상도를 개선하지 않는다. 정확한 매 tick 궤적이 필요해지면 별도 관측 출력이 필요하지만 이번에는 기존 기록과 단순 보간만 사용한다.

코트 메타데이터는 JSON에 없으므로 검증된 engineVersion의 `src/TennisSim.Core/Geometry.cs` 값에 계약을 묶는다: HalfWidth=4.115m, HalfLength=11.885m, ServiceLine=6.4m, BallRadius=0.0335m. 네트는 원본 높이 프로파일을 20 strip으로 근사하며 렌더링만 담당한다. 이전 섹션의 “미구현 3D 클라이언트”는 초기 기록이며, 현재 소스 구현과 Unity 검증 상태는 [UNITY_VIEWER.md](UNITY_VIEWER.md), [UNITY_VALIDATION.md](UNITY_VALIDATION.md)를 따른다.

## Engine v2 — realism audit correction

`tennissim-mvp-2` corrects circular footprint intersection at outside court/service-box corners. Schema 1.0, dimensions, coordinates and event semantics are unchanged. The viewer explicitly accepts both v1 and v2. Current-engine `resimulate` requires v2; displaying a v1 replay does not require recreating its outcomes. Original v1 sample remains the golden fixture. Diagnostic sidecars are not required by the viewer. Details: [REALISM_AUDIT.md](REALISM_AUDIT.md).


## Engine v3 — surface model and bounce diagnostics

tennissim-mvp-3 keeps schema 1.0 and adds optional fields. The viewer accepts tennissim-mvp-1, -2 and -3.
tennissim-mvp-4 keeps schema 1.0 with no new fields: seed mixing, the tactic model, pattern reading and
comfortable contact change outcomes only. Candidate lists may now contain a Forehand candidate. The viewer
accepts tennissim-mvp-1 to -4; resimulate accepts only the current engine version.

- MatchInput.Surface (optional): the surface environment for the explicit impulse model — ball spec, ball
  condition, interaction profile and tolerances. Absent (null) for the legacy multiplicative bounce. It is
  part of input, so a replay is self-describing and resimulation reproduces it.
- MatchEvent.Bounce (optional, BallBounced only): the impulse-model diagnostics for that contact — post state,
  normal and tangential impulses, pre/post contact slip, used en / mu_eff / beta, beta_effective, the active
  impulse limit, energies, profile id / revision / content hash, model id, outOfDomain, status and warnings.
- BallState.AngularVelocity (optional): spin, carried through flight. Omitted while zero, which is why a
  legacy-path replay keeps its previous bytes.

Both optional fields are omitted when unused, so no existing replay changes and no viewer is forced to read
them. Missing required keys are still rejected exactly as before.

Contact semantics: RESOLVED applies the impulse; SETTLED is a resting contact with no impulse (warning
SETTLED_CONTACT) and still counts as a surface contact, so the first/second bounce rules are unaffected;
SEPARATING_NO_IMPULSE, TANGENTIAL_CONTACT_NO_IMPULSE and DEEP_INITIAL_PENETRATION apply no impulse and, being
penetration or resting corrections rather than resolved contacts, do not create a bounce event. A physics
version change is expected to change outcomes, so resimulate refuses a replay whose engine version differs
instead of guessing, and the two models are not required to agree.
