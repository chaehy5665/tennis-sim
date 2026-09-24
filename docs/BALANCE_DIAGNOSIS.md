# 전술 밸런스 진단 (2026-09-24)

[GAME_LOOP.md](GAME_LOOP.md)의 수직 슬라이스 첫 작업. 전술 선택이 코치의 판단 문제가 되려면 **상대에 따라 정답이
달라져야** 한다. 이 문서는 현재 Core(engine `tennissim-mvp-3`, legacy bounce)가 그 조건을 만족하는지 측정한다.
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

## 한계

- 독립 포인트 실험이다. 경기 중 체력 누적, 체인지오버 지시, 점수 상황의 영향은 포함하지 않았다.
- 대칭 보수 행렬은 baseline 대칭전 하나만 측정했다.
- 진단용 seed 해싱은 도구에만 있다. 전체 경기 결과(`compare`)에는 첫 서브 편향이 남아 있다.
