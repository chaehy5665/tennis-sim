# 실행 검증 — 2026-09-14

## 환경과 범위

- 작업 폴더: `/home/c10/projects/tennis`. 초기 파일은 AGENTS.md뿐이며 Git 저장소가 아니었다. 기존 지침은 수정하지 않았다.
- SDK 10.0.401 / 런타임 10.0.12 / Ubuntu 26.04 linux-x64.
- Core netstandard2.1 / C# 8.0; CLI·테스트 net10.0.
- 전역 도구 설치, 원격 push, Unity 프로젝트, 외부 실행 서비스 없이 구현했다. 최초 빌드에 필요한 표준 참조 팩 NETStandard.Library.Ref 2.1.0을 복원했다.

## 빌드와 테스트

`dotnet build TennisSim.sln --no-restore --nologo`: **성공, warning 0 / error 0**.

`dotnet run --project tests/TennisSim.Tests --no-build`: **32 passed, 0 failed**. 테스트는 커스텀 실행형이며 `dotnet test` 결과가 아니다. 로그: `artifacts/build-results.txt`, `artifacts/test-results.txt`.

검증 범위:

- 듀스/어드밴티지/게임, 7–5와 6–6, 타이브레이크 2점 차·서버·엔드 순서.
- 1구 폴트, 2구 렛 이후 2구 유지, 더블 폴트, 대각선 박스/라인 경계.
- 고속 네트·지면 충돌 시각, 전후 위치 연속성과 속도 impulse, 네트 상단 통과, 코트 밖 공중 진행.
- 중력/항력 목표 역산, 공 유한값, 실제 타격 시 거리·바운드·방향·높이·준비 조건.
- 이동 가속/최대 속도, 코트 밖 이동/접촉, 주손/엔드 변환, 도달 불가 예측.
- 중립 정책에서 각 주손·엔드 조합마다 2,000회 paired draws: TargetBackhand 선택률 증가. Safe/Aggressive 1,000회와 서브 코스별 400회 선택 검사.
- seed 42의 전체 결과를 tick 묶음 4,096 / 1 / 137로 실행하여 JSON 전체 일치.
- JSON 실제 파일 저장/로드와 비교용 읽기 전용 필드 보존 회귀 검사.
- 진행 중 전술 지시의 다음 포인트 적용과 지시 포함 재시뮬레이션 일치.
- 제한 초과 시 임의 득점 없이 진단 종료.
- 추가 seed 1, 7, 99, 2026과 시작 엔드 변경 세트 종료.
- 서브 제어 0, 공격/T 코스의 150개 독립 포인트: 폴트 사건 112, 더블 폴트 18, 렛 2. 모두 정상 포인트 종료.

## 샘플 경기

```bash
dotnet run --project src/TennisSim.Cli --no-build -- match \
  --seed 42 --out artifacts/sample-42.json
```

Ember(A, baseline) 대 Willow(B, defender): **B 6–0 승리**, 27포인트. 타격 535회, 이벤트 2,309개, 샘플 상태 6,118개. 종료 이유: UnreturnedBall 22 / Out 4 / Net 1. 랠리 길이 평균 19.81, 최대 84. 비서브 타격 중 공이 코트 외곽에 있는 실제 타격 127회를 기록했다. 이는 모델의 실행 결과이지 현실성의 증거가 아니다.

기록: `artifacts/sample-42.json`, 텍스트: `examples/sample-match.txt`. 큰 JSON은 Git 제외 대상이다.

`resimulate --input artifacts/sample-42.json --chunk 137 --out artifacts/resimulated-42.json`: **RESIMULATION_IDENTICAL=true**. `replay` 명령으로 저장된 포인트/최종 점수를 출력하는 것도 실행했다.

선수 JSON·Wide/Aggressive/TargetBackhand 전술·시간 지시를 사용한 추가 경기(`README`의 두 번째 경기 명령): B 6–0, 30포인트. `artifacts/tactical-42.json`. 요청 tick 240과 1200은 모두 tick 1445, 포인트 2 시작에 적용됐다. 그 이전 포인트의 전술을 소급 변경하지 않았다.

## 독립 포인트 배치

```bash
dotnet run --project src/TennisSim.Cli --no-build -- points \
  --count 1000 --seed 100 --out artifacts/points-1000.json
```

**1,000 / 1,000 완료, 실패 0**. seed 100..1099. 서버는 i%2, A 시작 엔드는 두 포인트마다 반전. 매 포인트 새 엔진과 초기 체력. 타격 22,256회, 엔진 tick 호출 3,051,984회. 종료 이유 UnreturnedBall 726 / Out 235 / Net 39. 이 기본 제어 능력 배치에는 더블 폴트가 없었으며 별도의 낮은 제어 테스트에서 처리 경로를 검증했다.

요약 JSON을 직접 로드해 count, failures, shots, ticks, 원인 필드가 저장됐음을 확인했다.

## 전술 비교

A/B 모두 baseline, 상대 전술 Balanced/Mixed, 시작 서버 A, 시작 EndA=-1. seeds **11,22,33,44,55**를 모든 정책에 재사용. 4정책 × 5세트 = **20세트 모두 완료**. A의 초기 전술만 바꾸고, 동적 전술 지시는 넣지 않았다.

| A 정책 | A 세트 승리 | Backhand 후보 선택 | 평균 타격 속도 m/s | 의도 목표 평균 abs(X) m | 실제 첫 착지 평균 abs(X) m |
|---|---:|---:|---:|---:|---:|
| Balanced | 3/5 | 472/1439 = 32.8% | 22.947 | 1.953 | 1.986 |
| TargetBackhand | 5/5 | 553/753 = 73.4% | 22.984 | 2.280 | 2.300 |
| Safe | 0/5 | 326/1517 = 21.5% | 20.667 | 1.341 | 1.407 |
| Aggressive | 1/5 | 299/886 = 33.7% | 23.583 | 2.035 | 2.084 |

백핸드 선택률 분모는 A의 비서브 타격. 속도와 목표/착지는 서브를 포함한다. 좌표 평균은 설명용 요약이며 전체 좌표 분포는 `artifacts/comparison.json`의 intendedTargets/actualFirstLandings에 보존한다. 매 세트 점수·상태, 선택 수, 종료 사유와 초기 조건도 기록했다. 비교 JSON을 다시 읽어 20세트 완료와 각 정책 입력을 확인했다.

TargetBackhand의 선택률 증가 및 Safe/Aggressive의 실제 속도 차이를 관측했다. 이 5개 seed의 승률을 일반적인 전술 우열로 해석하지 않는다. 공격 성향이 언제나 승률을 높여야 한다는 테스트는 없다. 시작 엔드를 뒤집은 별도 20세트 비교는 실행하지 않았다.

## 해결한 검증 중 결함

최초 CLI JSON 설정이 읽기 전용 속성을 일괄 제외해 익명 객체인 비교/배치 요약이 비어 있었다. 실제 생성 파일을 읽어 발견했고 Vec3의 계산 편의 속성만 제외하도록 범위를 좁혔다. 파일 저장/로드 및 읽기 전용 요약 속성 회귀 검사를 추가하고 **배치·비교·샘플 파일을 수정된 코드로 다시 생성**했다. 현재 위 수치는 최종 파일에서 읽은 값이다.

## 남은 검증과 한계

- UNITY_RUNTIME_VERIFIED=false. Unity 컴파일/실행, 애니메이션/라켓/발 접촉은 검증하지 않았다.
- REALISM_CALIBRATED=false. 긴 랠리, 프리셋 강도 차이, 낮은 타점의 공격 후보 탈락률과 계수는 실측 보정 전이다.
- 접촉은 고정 tick 끝에서 검사하며 네트/바운드만 sub-tick 계산한다. 몸은 네트 방향 고정, 준비와 이동을 병행하는 단순 모델이다.
- 플랫폼 간 부동소수점 일치, 중간 저장/재개, 3D 재생 보간은 미검증/미구현.
- 발리/스매시/드롭샷/복식/다세트/풋폴트 및 신체·라켓 접촉 예외 규칙은 생략했다.

다음 최소 단계는 동일 출력의 단순 3D 코트·캡슐·공 표시로 이동/타격 타이밍을 시각적으로 검증하는 것이다.
