# 전술 밸런스 진단 (2026-09-24)

[GAME_LOOP.md](GAME_LOOP.md)의 수직 슬라이스 첫 작업. 전술 선택이 코치의 판단 문제가 되려면 **상대에 따라 정답이
달라져야** 한다. 이 문서는 현재 Core(engine `tennissim-mvp-3`, legacy bounce)가 그 조건을 만족하는지 측정한다. v4 변경 후 결과는
아래 "v4 결과" 절에 있다.
파라미터는 바꾸지 않았다. `REALISM_CALIBRATED=false`.

## 재현

```bash
dotnet run --project src/TennisSim.Cli --no-build -- balance --count 2000 --seed 100 \
  --out artifacts/balance/balance-2000.json
python3 scripts/summarize-balance.py artifacts/balance/balance-2000.json --out artifacts/balance/report.md
```

- 117개 셀 × 독립 포인트 2,000개 = 234,000포인트. 4코어에서 약 9분. 재실행하면 출력 바이트가 같다.
- 셀 구성: (1) 선수 조합 9개(baseline·server·defender) × A 전술 9개, 상대 B는 Balanced. (2) baseline 대칭전에서
  랠리 전술 6 × 6 보수 행렬.
- 모든 셀이 같은 seed 목록을 쓴다(공통 난수). 서버·엔드는 포인트마다 교대한다.
- 승률은 포인트 기준이고 95% Wilson 구간을 붙였다. "동률"은 구간이 겹친다는 뜻(보수적 판정).
- 200초 포인트 제한(`MaxPointTicks`)에 걸린 포인트는 승률에서 빼고 따로 집계한다.

## 발견된 결함 1: RNG 첫 출력 편향 (Core)

`SeedRandom`은 seed를 그대로 상태로 쓰는 xorshift32다. 작은 seed에서는 첫 출력이 약 seed × 2⁻¹⁹이다.
seed 42는 0.0026, seed 100은 0.0063이다.

- 경기마다 **첫 번째 가중 추첨이 항상 첫 후보를 고른다**. 첫 서브는 전술과 무관하게 Wide가 된다.
- 전체 경기에서는 첫 서브 1개에만 영향이 있다. 하지만 `points` 명령처럼 포인트마다 새 엔진을 만드는
  실험에서는 **모든 포인트의 첫 서브가 Wide**라서 서브 전술 비교가 무의미해진다. 처음 실행한 격자에서
  serve-wide, serve-body, serve-t가 Balanced와 샷 수까지 똑같이 나와 발견했다.
- 이번 진단은 도구 쪽에서 seed를 섞어(lowbias32 해시) 우회했다. Core는 바꾸지 않았다.
- Core를 고치면(생성자에서 seed 해싱 또는 워밍업) 모든 replay와 기준 해시가 바뀐다. 따라서 엔진 버전을
  올리고 기준 해시를 다시 만드는 별도 변경으로 해야 한다.

## 결과

### 1. 백핸드 공략이 사실상 정답이다

| 선수 조합 (A vs B) | 최선 | 최선과 동률 | 최선-최악 차이 (pp) |
|---|---|---|---:|
| baseline vs baseline | backhand | — | 38.2 |
| baseline vs server | backhand | serve-wide | 19.1 |
| baseline vs defender | backhand-aggressive | backhand | 21.5 |
| server vs baseline | backhand | backhand-aggressive | 21.8 |
| server vs server | serve-wide | — | 28.9 |
| server vs defender | backhand-aggressive | aggressive, backhand | 7.3 |
| defender vs baseline | backhand | backhand-aggressive | 19.7 |
| defender vs server | backhand | balanced, backhand-safe, serve-wide, serve-t | 5.1 |
| defender vs defender | backhand-aggressive | — | 54.0 |

9개 조합 중 8개에서 백핸드 공략(backhand 또는 backhand-aggressive)이 최선이거나 동률이다.
baseline 대칭 보수 행렬에서는 **backhand가 B의 모든 전술에 대한 최선 응답(지배 전략)**이다. 카운터 구조가 없다.

**원인은 약점 공략이 아니라 목표 위치다.** 상대 백핸드가 포핸드보다 강한 선수(baseline의 포핸드·백핸드 값을
맞바꾼 선수)를 상대로도 backhand는 포인트 62.2%를 따고 30세트 중 30세트를 이겼다. balanced는 48.1%다.
Backhand 후보는 가중치 10으로 선택되고, 목표 지점(깊이 9.1m, 상대 백핸드 쪽 사이드)이 기하학적으로 유리하다.
즉 "백핸드 공략"이라는 이름과 실제 효과가 맞지 않는다.

### 2. Safe는 항상 최악이다 (함정 선택지)

9개 조합 모두에서 safe가 최하위이고 backhand-safe가 그다음이다. baseline vs defender에서는 2.2%다.
안전하게 치면 이득이 있어야 할 상황(상대가 공격적이거나 내 선수가 불리할 때)이 존재하지 않는다.

### 3. 서브 방향은 Wide가 항상 이기거나 비긴다

서브 방향 효과가 유의한 조합에서는 모두 Wide가 가장 좋다. server 대칭전에서 Wide 서브 포인트 79.6%,
Body 39.8%. Mixed(섞기)가 이득인 경우가 없다. 상대가 서브를 읽는 모델이 없기 때문이다.

### 4. 선수 능력치 차이가 전술을 압도한다

defender는 baseline 상대로 포인트 85%, server 상대로 95%를 딴다. 포인트 승률 60%만 되어도 세트는 거의 전승이다
(위 확인 실험에서 62% → 30/30 세트). 프리셋끼리의 격차가 너무 커서 이런 조합에서는 전술이 결과를 바꾸지 못한다.

### 5. 수비형끼리는 랠리가 끝나지 않는다

defender 대칭전에서 Balanced끼리도 포인트당 평균 73구, 15%가 200초 제한에 걸린다. Safe끼리는 71%가 제한에
걸린다. 실제 테니스의 평균 랠리는 몇 구 수준이다. 이는 실측 비교 없이도 명백한 비현실성이며 관전 시간 문제로
직결된다.

## 게임 디자인에 대한 결론

현재 전술 공간은 **"백핸드 공략 + Wide 서브, Safe는 쓰지 마라"로 풀려 있다.** 체인지오버 지시(D2)가 의미를
가지려면 다음이 필요하다. 우선순위 순.

1. **전술의 효과를 상대 능력치에 연동**: 백핸드 공략의 이득이 상대의 백핸드 약점 크기에 비례하도록 한다. 강한
   백핸드를 가진 상대에게는 손해가 되어야 한다.
2. **각 선택지에 상황별 존재 이유 부여**: Safe는 상대가 공격적일 때 실책을 유도하는 등 이득이 있어야 한다.
   Aggressive는 상대 수비가 약할 때 이득이 커야 한다.
3. **서브 예측**: 한 방향만 반복하면 리턴이 좋아지는 적응 모델. 섞기가 의미를 갖게 한다.
4. **랠리 종료 압력**: 긴 랠리에서 체력·집중 저하로 오차가 커지도록 해서 수비형 대칭전의 무한 랠리를 없앤다.
5. **프리셋 격차 축소**: 진단용 프리셋은 포인트 승률 40~60% 범위로 맞춰 전술 효과가 보이게 한다.

각 변경 뒤에는 같은 `balance` 격자를 다시 돌려 (a) 조합마다 최선 전술이 달라지는지, (b) 보수 행렬에 지배 전략이
없는지, (c) 제한 도달 포인트가 0에 가까운지 확인한다. 이것이 전술 밸런스의 회귀 기준이 된다.

## v4 결과 (engine `tennissim-mvp-4`, 2026-09-24)

위 결론에 따라 Core를 바꾼 뒤 격자를 다시 돌렸다. 변경 내용은 [MODEL.md](MODEL.md), 요약은 README의
"Tactic model v4" 절에 있다.

```bash
dotnet run --project src/TennisSim.Cli --no-build -- balance --count 2000 --seed 100 \
  --out artifacts/balance/v4-2000.json
python3 scripts/summarize-balance.py artifacts/balance/v4-2000.json --out artifacts/balance/v4-report.md
```

v4 격자는 두 가지가 다르다. (1) 포인트를 10개씩 이어서 한 엔진에서 친다. 한 포인트마다 엔진을 새로 만들면 패턴
읽기와 랠리 사이 피로가 쌓이지 않기 때문이다. (2) 상대에 strong-backhand(baseline의 포핸드·백핸드 값을 맞바꾼
선수)를 추가했다. 셀 144개 × 2,000포인트이고, 랠리 제한 도달 0, 실패 0이다.

과정: 조정은 600포인트 격자로 5회 반복했다(`artifacts/balance/v4-iter1..5`). 2회차에서 Safe가 지배적이
되었다. 원인을 추적해 보니 랠리 타점의 80%가 약 0.46m였고, 그래서 공격 후보(타점 ≥0.85m)가 6경기에서 단
1회만 가능했다. 편안한 타점 규칙을 넣은 뒤 Attack이 실제로 쓰이기 시작했다. 이어서 Attack 목표를 라인에서
안쪽으로 옮겼다(승자 18% 대 실수 34%로 손해 보는 샷이었다).

| 기준 | v3 | v4 |
|---|---|---|
| 모든 선수 조합에서 최선과 동률인 전술 | 없음(단, 9개 중 8개가 백핸드 공략) | 없음. 12개 조합의 최선 전술이 6종류 |
| 대칭전 지배 전략 | backhand가 모든 열의 최선 응답 | backhand·balanced가 항상 최선과 동률(약한 지배) |
| 공격성 축의 상성 | Safe가 모두에게 짐 | Aggressive > Safe (57.4%), Balanced > Aggressive (57.8%), Balanced > Safe (54.0%) |
| Safe의 쓸모 | 없음 | defender 조합 4개 중 3개에서 최선 또는 동률 |
| 랠리 제한 도달 | defender 대칭 balanced 15%, safe 71% | 모든 셀 0% |
| 포인트당 평균 타수 (balanced끼리) | 4.8~73.2 | 3.8~19.0 |
| 프리셋 격차 (balanced끼리 A 포인트 승률) | 5%~96% | 11%~88% |

**아직 풀리지 않은 것 (정직한 판정)**:

1. **백핸드 공략의 효과가 작다.** baseline 상대 51.3% 대 balanced 49.5%, strong-backhand 상대 50.3% 대
   51.8%. 방향은 맞지만 차이가 95% 구간 안이다. 받는 선수가 공의 정확한 위치로 달려가므로 실제로 포핸드와
   백핸드 중 어느 쪽으로 칠지가 목표 방향을 약 70%만 따른다. 다음 단계는 받는 선수가 공을 몸 옆에 두도록 위치를
   잡는 것이다.
2. **Wide 서브가 거의 항상 좋다.** 12개 조합 중 9개에서 serve-wide가 최선이거나 동률이다. 패턴 읽기(반응 시간
   최대 60% 단축)로는 빠른 Wide 서브를 충분히 벌하지 못한다. 서브 쪽에는 다른 읽기 효과(리턴 준비 품질 등)가
   필요하다.
3. **대칭전에서 Balanced가 무난한 기본값이다.** 공격성 축에서 Safe > Balanced 관계가 없다. 게임 디자인으로 보면
   "무난한 기본값 + 상대별로 더 나은 선택"도 허용할 만하지만, 완전한 가위바위보는 아니다.
4. 조정 기준은 세 가지 프리셋과 하나의 대칭전뿐이다. 새 선수 유형이 생기면 같은 격자를 다시 돌려야 한다.

1번은 v5에서 일부 풀렸다(아래 절). 2~4번은 그대로다.

## v5 결과: 받는 선수 위치 (engine `tennissim-mvp-5`, 2026-09-24)

계기는 Mac 플레이 테스트(2026-09-24, [GAME_LOOP.md](GAME_LOOP.md))다. 백핸드 공략으로 바꿔도 다음 구간에서 상대의
백핸드 비율이 오히려 줄었다. 측정해 보니 노린 쪽으로 실제로 친 비율이 baseline 대칭전 16경기에서 69%,
기본 경기(Ember 대 Willow) 8경기에서 50%였다. 원인은 받는 선수가 공이 떨어질 지점 **위로** 달려가는 것이었다.
타구 순간 몸과 공의 거리 중앙값이 0.15~0.2m라 포핸드와 백핸드가 도착 순간의 작은 위치 차이로 갈렸다.

변경([MODEL.md](MODEL.md)): 받는 선수는 공이 오는 쪽을 보고 칠 손을 정하고, 공 옆 0.35m(`Movement.StrokeOffset`)에
선다. 타점 품질의 거리 항은 그 간격에서 벗어난 정도로 바꿨다. 간격 0.6m로 먼저 해 봤는데, 도달 판정의 여유
(0.62m)와 겹쳐 받는 선수가 팔 길이 밖에 멈췄다. 받지 못한 공으로 끝난 포인트가 41%에서 67%로 늘어 버렸다.

baseline 대칭전 16경기(seed 1~16):

| | v4 | v5 |
|---|---:|---:|
| 노린 쪽으로 친 비율 (백핸드 / 포핸드) | 68.3% / 69.4% | 99.3% / 99.4% |
| 받지 못한 공으로 끝난 포인트 | 40.8% | 43.2% |
| 아웃 / 네트 | 52.3% / 6.8% | 51.7% / 5.0% |
| 서브 포인트 승률 | 49.6% | 48.2% |
| 타점 품질 평균 / .65 미만 비율 | .823 / 18.7% | .850 / 14.8% |

격자(`artifacts/balance/v5b-2000.json`, 셀 144개 × 2,000포인트, 랠리 제한 0, 실패 0)에서 **백핸드 공략 승률 − 균형
승률**(%p, A 기준, 상대는 균형 고정):

| A | B | v4 | v5 |
|---|---|---:|---:|
| baseline | baseline | +1.8 | +5.5 |
| baseline | server | +1.7 | +3.3 |
| baseline | defender | +2.8 | -1.1 |
| baseline | strong-backhand | -1.5 | -3.2 |
| server | baseline | -1.0 | +1.8 |
| server | server | +2.5 | +4.7 |
| server | defender | -0.1 | -0.5 |
| server | strong-backhand | -0.3 | -2.6 |
| defender | baseline | +3.4 | +2.0 |
| defender | server | +1.3 | +1.7 |
| defender | defender | +0.1 | +1.3 |
| defender | strong-backhand | +1.0 | -3.9 |

- 백핸드가 약한 상대(baseline, server) 6칸은 모두 플러스, 백핸드가 강한 상대(strong-backhand) 3칸은 모두
  마이너스, 양손이 비슷한 defender 3칸은 0 근처다. v4에서는 부호가 섞여 있었다. 이제 공격 방향은 상대를 보고
  고르는 선택이다.
- 칸 하나의 차이는 여전히 작다(최대 5.5%p). 셀마다 신뢰 구간이 약 ±2.2%p라 한 칸만으로는 유의하지 않고,
  부호가 상대 유형에 따라 일관된다는 점이 근거다. 체인지오버 한 구간(5~18포인트)의 **점수**로 이 효과를 읽을 수는
  없다. 대신 상대가 백핸드로 친 비율은 이제 지시를 그대로 따르므로, UI가 타구 분포를 보여 주면 효과가 보인다.
- 대칭전 공격성 상성은 그대로다: Aggressive > Safe 57.6%, Balanced > Aggressive 56.5%, Balanced > Safe 53.4%.
- **Wide 서브는 여전히 지배적이다**(위 2번). 이번 변경과 무관하다.

## 상대 코치 AI 평가 (2026-09-24)

`OpponentCoach`(Core, [Coaching.cs](../src/TennisSim.Core/Coaching.cs))는 체인지오버마다 사람 코치와 같은 정보만
보고 B의 전술을 바꾼다. 규칙은 세 가지다.

- **공격 방향**: 상대의 누적 에러율(에러/타수)이 백핸드 쪽이 25% 이상 높으면 백핸드 공략, 포핸드 쪽이 높으면 균형.
  양쪽 타수가 12개 미만이면 스카우팅 프로필의 포핸드·백핸드 차이로 정한다.
- **공격성**: 위 격자의 상성을 따른다. 상대가 Safe면 Aggressive, Aggressive면 Balanced, Balanced면 Balanced로
  간다. 단, 컨트롤이 0.85 이상인 선수는 Safe.
- **서브**: 서브 파워 0.9 이상이면 첫 체인지오버에서 Wide로 간다. 고정 코스가 한 구간에서 서브 포인트를 절반 넘게
  잃으면 Mixed로 돌아간다.

AI는 난수를 쓰지 않는다. 지시는 `QueueTactics`로 기록되어 replay가 정확히 재시뮬레이션된다. 처음에는 "공격 중
실수가 많으면 진정" 규칙도 있었지만 뺐다. 6포인트짜리 첫 구간의 잡음에 반응해, Safe 상대에 대한 올바른
공격 전환을 뒤집었기 때문이다.

```bash
dotnet run --project src/TennisSim.Cli --no-build -- coach-eval --sets 30 --seed 100 \
  --out artifacts/balance/coach-eval-30.json
```

A는 9개 고정 전술 중 하나를 끝까지 유지하고, B는 Balanced 고정과 AI 코칭 두 가지로 같은 seed의 세트를 친다.
선수 조합 12개 × A 전술 9개 × 30세트 × 2모드이며, 실패는 0이다.

| | B 포인트 승률 | B 세트 승 |
|---|---:|---:|
| B 고정 (Balanced) | 50.6% | 1,721 / 3,240 |
| B AI 코칭 | 52.4% | 1,879 / 3,240 |

- 가장 크게 이득을 보는 상대는 **Safe를 고집하는 A**다(+4.3%p, 세트 203 → 241 / 360). Aggressive를 고집하는
  A 상대로는 +0.6%p로 거의 차이가 없다. 이미 Balanced가 그 상대에 대한 답이기 때문이다.
- 선수 조합으로 보면 B가 defender일 때 +4.4~5.9%p다(컨트롤이 높아 Safe로 간다). defender 대칭전에서는 세트가
  107 → 182 / 270이 된다.
- 유의하게 손해를 보는 조합은 없다. 가장 나쁜 경우가 server vs defender의 -1.0%p(±1.0)다.
- v5(`artifacts/balance/coach-eval-30-v5.json`, 같은 조건): B 고정 50.3%, 세트 1,668 / 3,240. B AI 코칭 53.0%,
  세트 1,939 / 3,240. AI 이득이 +1.8%p에서 +2.7%p로 커졌다. 공격 방향 규칙이 이제 실제 타구로 이어지기 때문으로
  보지만, 규칙별로 나눠 측정하지는 않았다.
- 한계: 상대가 고정 전술인 쉬운 평가다. 사람처럼 AI의 변화에 다시 대응하는 상대를 상대로는 측정하지 않았다.
  이득(+1.8%p)은 작은 편이다. 약한 코치 수준이며, 난이도를 조절하는 장치는 아직 없다.

## 한계

- v3 결과는 독립 포인트 실험이다. 경기 중 체력 누적, 체인지오버 지시, 점수 상황의 영향은 포함하지 않았다.
- 대칭 보수 행렬은 baseline 대칭전 하나만 측정했다.
- v3 진단의 seed 해싱은 도구에만 있었다. v4부터는 `SeedRandom`이 직접 seed를 섞으므로 도구 쪽 해싱을 제거했다.
