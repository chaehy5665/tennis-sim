# 결정론과 플랫폼 간 재현성 계약

이 문서는 TennisSim의 결정론을 하나의 `PASS/FAIL`로 축약하지 않고, 실행 범위와 비교 강도에 따라 분리한다. 저장된 replay는 표시와 감사의 권위 있는 기록이며, Unity를 포함한 renderer는 replay를 재시뮬레이션하거나 경기 결과를 다시 결정하지 않는다.

## 용어와 판정

| 판정 | 비교 범위 | PASS 조건 |
|---|---|---|
| Same-platform byte determinism | 동일 소스, 입력, runtime/OS/architecture | 직렬화한 전체 `MatchRecord` bytes와 SHA-256이 동일 |
| Cross-platform outcome reproducibility | 동일 엔진 소스와 입력, 서로 다른 OS/architecture | 사건 종류·순서, point winner, 종료 원인, 최종 RNG 상태, 점수와 승자가 정확히 동일 |
| Cross-platform semantic reproducibility | outcome 조건과 모든 의미 수치 | 이산 필드는 정확히 동일하고 모든 실수 값은 아래 허용오차 이내 |
| Cross-platform bitwise reproducibility | 서로 다른 플랫폼의 전체 record | 직렬화 bytes와 SHA-256이 동일 |

`resimulate`의 `RESIMULATION_IDENTICAL`은 현재 전체 JSON 문자열 동일성을 검사한다. 따라서 `false`는 bitwise 불일치를 뜻하며, 그 자체만으로 사건이나 승자가 달라졌다는 뜻은 아니다. 반대로 결과 요약만 같은 것은 semantic PASS의 충분조건이 아니다.

## Semantic 비교 계약

다음 필드는 정확히 같아야 한다.

- schema/engine version, seed, 전체 match input/config와 instruction history
- 배열 길이, 필드 존재 여부와 타입
- event sequence, kind, point, player/action ID, shot/stroke/reason 및 배열 순서
- frame/event 수, point winner와 모든 점수 전이
- status, final score/winner와 `finalRandomState`

유한 실수 값은 같은 JSON 경로끼리 아래 절대오차로 비교한다.

| 값 | 최대 절대오차 |
|---|---:|
| event/state 시간과 flight time | `1e-9 s` |
| ball/player 위치 및 target | `1e-7 m` |
| ball/player 속도 | `1e-7 m/s` |
| energy, quality, weight 등 기타 무차원 값 | `1e-9` |

이 허용오차는 경기 분기 차이를 감추지 않는다. event가 하나라도 추가·누락·재정렬되거나 판정, point winner, 종료 이유, 최종 RNG 상태 또는 점수가 다르면 수치 오차가 작아도 FAIL이다. NaN/Infinity, 누락 필드, 타입 변화도 FAIL이다. 새로운 물리량은 단위와 허용오차를 이 문서에 먼저 추가해야 한다.

## 2026-09-14 Linux x64 대 Mac 실행

입력은 engine `tennissim-mvp-2`, seed 42 candidate replay다.

- Linux source snapshot: `artifacts/calibration/20260914-audit/candidate-executed-source/`
- Linux replay: `artifacts/calibration/20260914-audit/replays/candidate-42.json`
- Linux SHA-256: `fcc0c8e0db7c3618789e0ab10300a0c611d6feb1565b130640d5ab21c9ca2958`
- Mac resimulation: `artifacts/cross-platform/mac-resimulated-42.json`
- Mac SHA-256: `9c947f201228d9700d98f8f1d1f8e301b8a5b57829ba98819f7d35abae485690`

Mac 결과는 사용자가 Linux의 보존 candidate source를 별도 디렉터리로 복사해 native `dotnet run`으로 생성한 뒤 Linux로 다시 전송했다. 이 run의 Mac `dotnet --info`, project-file hash 및 전체 source-hash 검증 로그는 아직 보존되지 않았으므로 실행 정체성 증거는 부분적이다. replay의 `engineVersion`과 전체 input은 Linux record와 정확히 같다.

직접 JSON tree 비교 결과:

| 항목 | 결과 |
|---|---:|
| Input / rules / engine version | exact |
| Events | 2,309 / 2,309; 이산 skeleton exact |
| Frames | 6,118 / 6,118 |
| Instruction history | exact |
| Final RNG state | exact |
| Terminal result | B, 0–6, 27 points; exact |
| Non-numeric leaf differences | 0 |
| Numeric leaf differences | 117,610 |
| Maximum absolute numeric delta | `6.944999436653276e-10` |
| Final event-time delta | `-1.1368683772161603e-13 s` |

최초 차이는 event sequence 39 `BallBounced`에서 발생했다. 직전 event 38 `ContactPrepared`까지는 전체 값이 동일했다. 첫 bounce time은 Linux `10.08799942648778`, Mac `10.087999426487766`으로 delta는 `-1.4210854715202004e-14 s`다. 이후 차이는 위치·속도·통계 값으로 전파되지만 event skeleton, RNG 진행 및 경기 결과는 갈라지지 않았다.

`BallPhysics.At`과 충돌 root 탐색은 `Math.Exp`를 사용한다. 동일한 직전 상태에서 첫 차이가 이 경로에서 나타났으므로 OS/CPU 수학 구현 차이가 원인이라는 강한 증거지만, 별도의 runtime-level trace가 없으므로 이는 원인 추론이다.

이번 한 경기의 판정은 다음과 같다.

```text
SAME_PLATFORM_BYTE_DETERMINISM: PASS
CROSS_PLATFORM_OUTCOME_REPRODUCIBILITY_SEED42: PASS
CROSS_PLATFORM_SEMANTIC_REPRODUCIBILITY_SEED42: PASS_WITH_TOLERANCE
CROSS_PLATFORM_BITWISE_REPRODUCIBILITY_SEED42: FAIL
CROSS_PLATFORM_GENERALIZATION_BEYOND_SEED42: NOT_ESTABLISHED
```

## 운영 원칙

- 배포·공유·Unity 재생에는 원본 replay bytes를 전달하고, 소비자가 결과를 재계산하지 않는다.
- 동일 플랫폼 회귀에서는 byte comparison을 유지한다. semantic comparator로 기존 강한 검사를 대체하지 않는다.
- 플랫폼 간 감사에서는 bitwise 결과와 semantic 결과를 둘 다 기록한다.
- semantic PASS를 전체 플랫폼·전체 seed 보증으로 일반화하지 않는다. 지원하려는 OS/architecture/runtime 조합과 여러 고정 seed를 별도 matrix로 실행한다.
- 향후 lockstep networking이나 플랫폼 간 byte-identical save regeneration이 요구되면, `Math.Exp`/`Math.Sqrt` 등 platform math를 그대로 둔 채 반올림만 추가하지 않는다. deterministic math 또는 fixed-point 설계, 분기 안정성 분석, golden replay 재생성 및 calibration 재검증을 별도 변경으로 수행한다.

## 재현 명령

Linux 원본과 Mac 결과가 같은 저장소에 있을 때:

```bash
sha256sum \
  artifacts/calibration/20260914-audit/replays/candidate-42.json \
  artifacts/cross-platform/mac-resimulated-42.json

cmp \
  artifacts/calibration/20260914-audit/replays/candidate-42.json \
  artifacts/cross-platform/mac-resimulated-42.json
```

현재 기대값은 SHA-256 불일치와 `cmp` exit 1이다. 이는 bitwise FAIL의 재현이며 semantic PASS는 위 필드/허용오차 계약을 모두 검사해야 한다. 현재 repository에는 전용 semantic-comparison CLI가 없으므로 이번 판정은 보존된 두 JSON에 대한 직접 tree comparison 결과다.
