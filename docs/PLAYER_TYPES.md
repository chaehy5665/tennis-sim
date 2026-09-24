# 선수 유형 (2026-09-24)

코치는 선수 한 명을 맡고, 매 경기 상대가 바뀐다([GAME_LOOP.md](GAME_LOOP.md)). 전술 선택이 판단 문제가 되려면 상대마다
약점이 달라서 **누구를 만나느냐에 따라 정답이 달라져야** 한다. 기존 프리셋 세 개는 이 조건을 만족하지 못했다.

- 능력 차이가 너무 컸다. 균형 대 균형 A 포인트 승률이 11%~88%였고, 기본 CLI 경기(Ember 대 Willow)는 seed 42에서 0-6이다.
  이런 조합에서는 전술이 결과를 바꾸지 못한다([BALANCE_DIAGNOSIS.md](BALANCE_DIAGNOSIS.md) 결과 4).
- 약점이 비슷했다. 세 명 모두 "백핸드가 약하다" 아니면 "모든 게 강하다"였다.

그래서 비슷한 전체 실력에 약점이 서로 다른 **유형 6개**를 만들었다. 모든 수치는 engine `tennissim-mvp-5` 기준이다.
main의 v6(서브 리턴 준비 위치 변경)에서는 격자와 playtest를 다시 돌려야 한다.

## 유형

프리셋 이름은 CLI `--player-a/--player-b`와 `PlayerProfile.Preset`에 쓰는 이름이다. 목록은 `PlayerProfile.Archetypes`,
JSON은 `examples/players/<프리셋>.json`이다.

| 프리셋 | 이름 | 정체성 | 스카우팅으로 읽는 단서 |
|---|---|---|---|
| `baseline` | Ember | 포핸드 주도 올라운더 | 포핸드 > 백핸드 (합 1.60 대 1.37) |
| `backhander` | Rook | 백핸드 주도 올라운더 | 백핸드 > 포핸드 (1.60 대 1.37) |
| `big-server` | Flint | 서브와 포핸드, 느린 발 | 서브 파워 최고(.97), 백핸드 약함, 속도 6.0 |
| `retriever` | Moss | 파워 없는 수비수 | 파워 최저(.45), 체력 최고(.95), 반응 빠름 |
| `slugger` | Blaze | 가장 무거운 공, 가장 낮은 컨트롤 | 파워 최고(.97/.90), 컨트롤 최저(.67/.63) |
| `touch` | Wren | 왼손 컨트롤 플레이어, 느림 | 컨트롤 최고(.92), 가장 느림(5.4), 왼손 |

능력치 전체:

| 프리셋 | 서브 P/C | 포핸드 P/C | 백핸드 P/C | 속도 | 가속 | 반응 | 준비 | 체력 | 왼손 |
|---|---|---|---|---:|---:|---:|---:|---:|---|
| baseline | .75/.78 | .80/.80 | .65/.72 | 6.2 | 10 | .20 | .18 | .80 | |
| backhander | .75/.78 | .65/.72 | .80/.80 | 6.2 | 10 | .20 | .18 | .80 | |
| big-server | .97/.82 | .86/.80 | .58/.72 | 6.0 | 9 | .20 | .18 | .72 | |
| retriever | .60/.85 | .45/.88 | .45/.88 | 6.5 | 10.5 | .17 | .18 | .95 | |
| slugger | .88/.74 | .97/.67 | .90/.63 | 6.8 | 11 | .18 | .18 | .80 | |
| touch | .65/.88 | .58/.92 | .60/.92 | 5.4 | 8.5 | .18 | .18 | .80 | ✓ |

- 새 능력치는 추가하지 않았다. 기존 능력치로 모든 유형을 만들 수 있었다.
- `baseline`(Ember)은 바꾸지 않았다. 코치 UI에서 내가 맡는 선수라서 기준점으로 둔다.
- `backhander`는 코치 UI의 Rook(`CoachMatchup`: Ember의 포핸드·백핸드 값을 맞바꾼 선수)과 수치까지 같다. Unity가
  나중에 `PlayerProfile.Preset("backhander", "B")`를 써도 replay가 바뀌지 않는다(테스트로 확인).
- 왼손은 touch 하나다. 이 엔진에서 왼손 자체는 유불리가 거의 없다(Ember를 왼손으로 바꿔도 균형 대 균형 49.7%).
  Core는 왼손의 백핸드 쪽을 올바르게 계산하므로 "백핸드 공략"은 그대로 작동한다. 대신 화면에서 백핸드가 반대쪽에
  있다는 것을 코치가 읽어야 한다.

### 기존 프리셋 (유형 밖)

`server`(Granite)와 `defender`(Willow)는 바이트까지 그대로 두었다. 기본 CLI 경기, seed 42 기준 해시, 기존 테스트와
문서가 이 값을 쓴다. 다만 강도가 유형 범위 밖이라(균형 대 균형, 조합당 1,000포인트로 Willow는 유형 6개 상대로 69~80%, Granite는 25~40%)
`balance`, `playtest`, `coach-eval` 격자에서는 뺐다. `strong-backhand`는 `backhander`의 옛 이름으로 격자 도구에서만
계속 받는다(표시 이름만 다르다).

## 상대별로 어떻게 코칭하나

아래는 **Ember(baseline)가 상대를 만날 때** 격자의 수치다(A 포인트 승률 %, 상대는 균형 고정, 셀당 2,000포인트,
95% 구간 약 ±2.2%p). 코치 UI에서 내가 맡는 선수가 Ember라서 이 행이 가장 중요하다.

| 상대 | 균형 | 최선 | 피할 것 | 이유 |
|---|---:|---|---|---|
| Ember (baseline) | 47.8 | 백핸드 공략 53.2 | 공격 42.8 | 백핸드가 약하다 |
| Rook (backhander) | 49.5 | 균형 또는 Wide 51.1 | 백핸드 공략 46.2, 공격 40.6 | 백핸드가 강하다. 백핸드를 노리면 오히려 손해 |
| Flint (big-server) | 55.5 | 백핸드 공략 59.2 | 공격 45.6, 안전 49.6 | 백핸드가 약하고 발이 느리다 |
| Moss (retriever) | 45.5 | 백핸드+공격 53.1, 공격 51.8 | 안전 41.3 | 공이 느려서 공격해도 압박이 없다. 버티기 싸움은 진다 |
| Blaze (slugger) | 54.1 | 백핸드+안전 59.1, 안전 57.6 | 공격 42.6 | 공이 무거워 공격하면 실수가 늘고, 버티면 상대가 먼저 실수한다 |
| Wren (touch) | 53.5 | Wide 54.6, 균형 | 안전 27.3 | 컨트롤이 최고라 버티기로는 절대 못 이긴다 |

코치가 읽는 규칙으로 요약하면 다음과 같다.

- **공격 방향**: 상대 포핸드와 백핸드의 차이를 본다. 백핸드가 약하면 백핸드 공략, 강하면 균형.
- **공격성**: 상대의 파워와 컨트롤을 본다. 파워 없는 상대에게는 공격, 파워는 크고 컨트롤이 낮은 상대에게는 안전,
  컨트롤이 높은 상대에게는 안전 금지.
- **서브**: 지금 엔진에서는 거의 항상 Wide다(아래 한계).

선수 자신의 유형도 정답을 바꾼다. 같은 격자에서 slugger가 A이면 6개 상대 중 5개에서 안전 계열이 최선이고
(컨트롤이 낮아 공격하면 실수가 쌓인다), touch가 A이면 6개 모두에서 공격 계열이 최선이다(느린 발을 대신해 먼저 끝낸다).
retriever가 A이면 상대에 따라 안전(Rook 상대)부터 공격(Wren 상대)까지 갈린다. 시즌 루프에서 다른 선수를 맡게 되면 이 차이가
의미를 갖는다.

## 균형 대 균형 승률

`balance` 격자의 balanced 셀(A 포인트 승률 %, 셀당 2,000포인트). 행이 A, 열이 B다.

| A \ B | baseline | backhander | big-server | retriever | slugger | touch | 평균 (대칭 제외) |
|---|---:|---:|---:|---:|---:|---:|---:|
| baseline | 47.8 | 49.5 | 55.5 | 45.5 | 54.1 | 53.5 | 51.6 |
| backhander | 50.9 | 48.6 | 54.1 | 43.4 | 54.4 | 52.9 | 51.1 |
| big-server | 44.9 | 45.7 | 50.3 | 42.9 | 50.1 | 49.0 | 46.5 |
| retriever | 55.9 | 54.9 | 57.0 | 51.9 | 58.9 | 43.8 | 54.1 |
| slugger | 47.1 | 44.8 | 50.9 | 41.4 | 49.2 | 48.5 | 46.5 |
| touch | 46.2 | 44.5 | 52.3 | 57.4 | 50.8 | 50.2 | 50.2 |

- 대칭전이 아닌 30칸은 41.4%~58.9%다. 목표(35~65%) 안이다. 기존 프리셋은 11%~88%였다.
- 대칭전(대각선)이 50에서 최대 2.2%p 벗어나는 것은 표본 잡음이다. 선수 순서를 바꾼 두 칸의 평균으로 보면 쌍별 승률은
  43.0%(big-server 대 retriever)~58.8%(retriever 대 slugger)다.
- 상성이 하나 있다. retriever는 다른 네 유형을 이기지만 touch에게 진다(43.8%). 끈질긴 수비수가 더 정확한 선수에게
  버티기로 지는 구조다.
- 포인트 승률 55%는 세트로 보면 꽤 큰 차이다(BALANCE_DIAGNOSIS의 확인 실험에서 62% → 30/30 세트). 이 범위 안에서 전술이 5~10%p를 바꾸므로,
  불리한 상대도 맞는 전술이면 해볼 만하다.
- 랠리 길이(균형끼리, 포인트당 타수)는 slugger 대칭 4.0 ~ retriever 대칭 22.5다. 랠리 제한 도달 0, 실패 0.

## 매치업별 최선 전술

`balance` 격자, 36개 조합(A 6 × B 6) × A 전술 9개. 동률은 95% Wilson 구간이 최선과 겹친다는 뜻이다.

| A \ B | baseline | backhander | big-server | retriever | slugger | touch |
|---|---|---|---|---|---|---|
| baseline | backhand | serve-wide | backhand | backhand-aggressive | backhand-safe | serve-wide |
| backhander | backhand | serve-wide | backhand | backhand-aggressive | backhand-safe | serve-wide |
| big-server | backhand | serve-wide | backhand | aggressive | backhand | serve-wide |
| retriever | backhand-safe | safe | backhand-aggressive | backhand-aggressive | backhand-safe | aggressive |
| slugger | backhand-safe | safe | backhand-safe | serve-wide | safe | serve-wide |
| touch | backhand-aggressive | backhand-aggressive | backhand-aggressive | aggressive | backhand-aggressive | aggressive |

- 최선 전술은 **6종류**다. serve-wide 8, backhand-aggressive 8, backhand 7, backhand-safe 6, aggressive 4, safe 3(36칸).
  가장 많은 전술도 22%다. 목표(한 전술이 절반 미만)를 만족한다.
- 최선이거나 동률인 칸 수로 보면 serve-wide 18, backhand-safe 16, backhand 15, backhand-aggressive 14, aggressive 13,
  safe 12, balanced 12다. 어느 전술도 "항상 무난한 답"이 아니다.
- 랠리 축만 보면(서브 전술 제외 6개) backhand 10, backhand-aggressive 8, backhand-safe 6, balanced 5, aggressive 4,
  safe 3이다.
- 최선과 균형의 차이는 +1.0~+20.3%p, 셀 안의 최선-최악 차이는 6.3~49.9%p다. retriever, slugger,
  touch가 들어간 조합에서 전술이 결과를 가장 크게 바꾼다.

재현:

```bash
dotnet build src/TennisSim.Cli -c Release
DOTNET_gcServer=1 dotnet run --project src/TennisSim.Cli -c Release --no-build -- balance --count 2000 --seed 100 \
  --out artifacts/balance/types-2000.json
python3 scripts/summarize-balance.py artifacts/balance/types-2000.json --out artifacts/balance/types-report.md
```

셀 360개(36조합 × 9전술 + 대칭 보수 행렬 36) × 2,000포인트. 8코어를 다른 세션과 나눠 쓰는 상태에서 8분 24초.
기존 격자(셀 144개)의 약 2.5배다.

## 자동 플레이 테스트

[PLAYTEST.md](PLAYTEST.md)의 `playtest`를 유형 36개 매치업으로 돌렸다. B는 코치 UI처럼 `OpponentCoach`가 코칭한다.
36 매치업 × 정책 12 × 40세트 = 17,280세트, 실패 0, 13분 23초(부하 상태).

```bash
DOTNET_gcServer=1 dotnet run --project src/TennisSim.Cli -c Release --no-build -- playtest --sets 40 --seed 100 \
  --out artifacts/playtest-types-40.json
```

**지배 전략.** 매치업별 최선 고정 전술은 strong 10, backhand 7, serve-wide 6, backhand-safe 5, aggressive 3, safe 3,
balanced 2다. 7종류이고 가장 많은 것도 36개 중 10개(28%)다. v5 기준값(12 매치업)에서는 Wide를 포함한 전술이 절반이었다.

**결정 가치.** A 유형별 평균(%p, A 포인트 승률):

| A | 최선 고정 − 균형 고정 | reader − 균형 고정 | reader − 최선 고정 |
|---|---:|---:|---:|
| baseline (Ember) | +4.6 | +2.0 | -2.5 |
| backhander | +4.2 | +1.6 | -2.6 |
| big-server | +3.7 | +1.1 | -2.6 |
| retriever | +8.0 | -4.5 | -12.5 |
| slugger | +10.1 | -0.4 | -10.5 |
| touch | +8.5 | -9.7 | -18.1 |

- 맞는 고정 전술을 고르면 균형보다 평균 3.7~10.1%p 더 딴다. 상대를 보고 고를 이유가 있다.
- Ember가 AI 코칭 retriever를 만나면 균형 고정은 36.9%, aggressive 고정은 52.2%다(+15.3%p). 이번 격자에서 한 번의
  판단이 가장 크게 결과를 바꾸는 매치업이다. Ember 대 slugger는 safe 고정이 +4.1%p다.
- `reader`(OpponentCoach 규칙을 A에게 적용)는 retriever, slugger, touch에서 크게 진다. 규칙이 "컨트롤이 높으면 Safe"처럼
  **자기 선수 유형을 모르기 때문**이다. slugger에게는 Safe가 필요하고(규칙은 Balanced), touch에게는 Safe가
  최악이다(규칙은 Safe). 코치 AI를 새 유형에 맞추는 일은 아래 한계에 적었다.

**효과 가시성과 AI 안정성.** 공격 방향 신호/잡음 1.01, 공격성 2.44/2.37, 서브 코스 0.01~0.24로 v5 기준값(1.00, 2.45/2.23,
0.11~0.34)과 비슷하다. B가 공격 방향을 되돌린 세트는 15.6%로 v5 기준값 9.0%보다 많다. retriever와 touch는 포핸드와
백핸드가 거의 같아서, 누적 에러율 비교(1.25배 규칙)가 잡음으로 자주 뒤집히는 것으로 보인다(따로 측정하지는 않았다).

## 한계

- **서브 코스는 아직 선택이 아니다.** 서브 축만 비교하면 36개 조합 중 32개에서 Wide가 최선이다. 매치업별 최선의
  serve-wide 8칸은 대부분 "랠리 전술로는 균형보다 나은 게 없는 상대"(backhander, touch)에서 나온다. 유형으로 풀 문제가
  아니라 엔진 문제이고, main의 v6가 이 부분을 바꾼다.
- **상대 코치 AI와 touch가 충돌한다.** `OpponentCoach`의 `SteadyPlayer` 규칙(평균 컨트롤 .85 이상이면 Safe)이 touch와
  retriever에게 걸린다. retriever에게는 맞는 선택이지만(Safe 59.5% 대 균형 55.9%, Ember 상대), touch에게는 최악이다
  (Safe 31.8% 대 균형 46.2%). 그래서 playtest에서 AI가 코칭하는 touch를 상대로는 A가 균형 고정만으로 평균 64.8%를 딴다
  (격자의 균형 대 균형은 43.8~53.5%). touch 대칭전은 78.2%다. 지금 상태로는 touch가 "쉬운 상대"가 된다.
  규칙은 Debug 세션의 `OpponentCoach` 소관이라 바꾸지 않았다. 유형 쪽에서 touch의 컨트롤을 .85 밑으로 내려 피할 수도
  있지만, 그러면 "컨트롤 최고"라는 정체성이 retriever(.88)에게 넘어간다. 규칙이 자기 선수의 공격성 정답(slugger는 안전,
  touch는 공격)을 알게 하는 쪽이 맞다고 본다.
- 효과 크기는 여전히 포인트 승률 몇 %p인 칸이 많다(Ember 대 Rook, Ember 대 Wren은 최선과 균형의 차이가 2%p 미만).
  체인지오버 한 구간의 점수로는 읽을 수 없다. 이 두 상대에서 코치의 결정은 "하지 말아야 할 것"(백핸드 공략, 안전)을
  피하는 것이다.
- 전술 효과가 선수 자신의 유형에 크게 좌우된다(slugger는 안전, touch는 공격). 코치가 Ember만 맡는 지금 슬라이스에서는
  상대에 따른 차이만 보인다.
- 체력과 준비 시간은 유형을 가르는 데 거의 쓰이지 않았다. Ember의 체력을 .80에서 .95로 올려도 Ember 상대 포인트 승률이
  -2.0%p로 잡음(±1.6%p) 수준이었다(능력치 하나씩 바꾼 민감도 측정, 4,000포인트). 체력으로 정체성을 만들려면 긴 경기 피로 모델이 필요하다.
- 조정은 균형 대 균형 행렬과 Ember 행을 보며 손으로 6회 반복했다. 최적화 도구를 쓰지 않았고, 조합마다 1,500~3,000포인트
  수준으로 판단했다.
- 모두 가상의 능력치다. 실제 선수나 실측 통계와 대응하지 않는다. `REALISM_CALIBRATED=false`.

## v6 재측정 (engine `tennissim-mvp-6`, 2026-09-24)

위 수치는 v5 기준이다. 서브 위치 읽기(v6)가 들어간 main에서 같은 조건으로 다시 쟀다.
`artifacts/balance/types-v6-2000.json`(360셀, 랠리 제한 0, 실패 0), `artifacts/playtest-types-v6-40.json`(36 매치업 × 12정책
× 40세트, 실패 0).

| | v5 | v6 |
|---|---|---|
| 균형 대 균형 승률(대칭전 제외) | 41.4~58.9% | 40.8~59.2% |
| 서브 전술 중 최선 (36 매치업) | Wide 26, Mixed 8, Body 1, T 1 | Mixed 17, Wide 15, Body 2, T 2 |
| Wide − Mixed 매치업 평균 | +0.65%p | −0.19%p |
| 고정 전술 전체 중 최선 | 7종류, 최대 strong 10 | 8종류, 최대 backhand 10 |
| B가 공격 방향을 되돌린 세트 | 15.6% | 15.6% |

- 유형의 강도 폭은 그대로다. "서브 코스는 아직 선택이 아니다"(위 한계)는 v6에서 풀렸다. 서브 코스 최선이 Mixed와 Wide로
  거의 반씩 나뉜다.
- `reader` − 최선 고정은 평균 −8.0%p다. A가 retriever, slugger, touch일 때 −10 ~ −28%p로 크다. `reader`는 상대 코치 AI의
  규칙을 A에게 적용한 것이라, 같은 규칙으로 코칭받는 B도 이 유형들을 잘 다루지 못한다(예: touch 대칭전에서 균형 고정
  A가 77.6%). AI 규칙은 Debug 세션이 고치고 있다.
