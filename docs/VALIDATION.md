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
- 플랫폼 간 bitwise 일치는 seed 42에서 실패했다. 같은 run의 outcome과 허용오차 기반 semantic 비교는 통과했지만 다른 seed/플랫폼으로 일반화하지 않는다. 세부 계약과 수치는 [DETERMINISM.md](DETERMINISM.md)를 따른다.
- 중간 저장/재개와 3D 재생 보간은 미검증/미구현.
- 발리/스매시/드롭샷/복식/다세트/풋폴트 및 신체·라켓 접촉 예외 규칙은 생략했다.

다음 최소 단계는 동일 출력의 단순 3D 코트·캡슐·공 표시로 이동/타격 타이밍을 시각적으로 검증하는 것이다.


## 공-표면 충돌 모델 검증 (2026-09-15 추가)

이 절의 수치는 이 실행에서 얻은 결과이며, 실측 보정이 아니다. 명령과 환경: Linux x64, SDK 10.0.401,
저장소 루트에서 실행. 큰 파일은 artifacts/에 생성했다.

### 빌드와 자동 검증

- dotnet build TennisSim.sln: 성공, warning 0 / error 0.
- dotnet run --project tests/TennisSim.Tests --no-build: 57 passed, 0 failed (기존 36 + 충돌 모델/보정 도구 21).
- dotnet run --project src/TennisSim.Cli --no-build -- scenarios: 44 PASS, 1 FAIL. 실패 항목은 이전
  실행에서 이미 보존된 drop.defaultDragReference(기본 항력 낙하 참조 하한)이며 새 결함이 아니다.
- 문서 fixture 4종(슬라이딩/미끄럼 0/접선 반발/과도 탑스핀)은 엔진 좌표로 독립 재계산한 값과 1e-9 이내로 일치.
- 1200 조합 스윕에서 에너지 비증가, 마찰 상한, 각충격량 관계, 법선축 회전 불변, 표면 배치 불변 모두 위반 0.

### 통합과 재현성

- match --seed 42 --surface-model impulse: Completed, 28포인트, 이벤트 2289, resimulate --chunk 137 동일.
- 같은 seed의 기존 모델: Completed, 27포인트, 이벤트 2309. 물리 버전이 다르므로 결과 일치를 요구하지 않는다.
- 기존 경로 수치 동일성: 새 엔진의 seed-42 legacy 재생은 보존된 mvp-2 candidate 재생과
  585057 leaf 중 수치 차이 0, 비수치 차이 0(엔진 태그 문자열만 다름).
- points --count 200: 기존 모델 200/200 완료·실패 0(타격 4364), 충돌 모델 200/200 완료·실패 0(타격 3994).
- 낮은 에너지 접촉은 SETTLED로 표면에 정지하며 스텝당 접촉 상한(12)은 진단 경로로 남아 있다.
- tests/TennisSim.ViewerChecks: 원본 v1 fixture 14/14, v3 legacy 재생 14/14, v3 impulse 재생 14/14.
- 수신자 예측(PredictContact)과 실제 비행은 같은 환경을 사용하며, 위치·시각이 스텝 단위로 일치한다.

### 보정 파이프라인(합성 자료)

- validate-data: EMPIRICAL_DATA MISSING(실측 레코드 0), 합성 레코드 240, 12 세션, 학습 160/검증 40/시험 40.
- fit(합성, 진리 en=0.83, mu=0.35): M1 선택(en=0.8289, mu=0.3503, 두 파라미터 모두 IDENTIFIED,
  두 접촉 모드가 모두 자료에 존재), M2는 사전 등록된 3% 검증 개선 기준을 넘지 못해 제외.
  en의 세션 클러스터 부트스트랩 SE 0.0037; mu는 부트스트랩이 움직이지 않아 NOT_AVAILABLE로 표기했다.
  같은 진리의 두 번째 합성 자료(seed 777001)는 en=0.8268, mu=0.3497로 실현 간 차이 2.1e-3 / 5.8e-4.
- evaluate: 시험 집합 normal 0.1325 m/s, tangential 0.2167 m/s, angular 135.0 rpm, 출사각 1.083도.
  합성 자료이므로 사전 등록 목표는 게이트하지 않고 서술값으로만 보고한다.
- virtual-itf: 합성 M1 프로필에서 e_test=0.8289, mu_test=0.3503, raw CPR=62.1335. 상수 프로필에서는
  프로필 상수를 재현하는 코드 경로 확인이며 실측 정보가 아니다. 합성 증거이므로 종료 코드 1.
- compare-runtime: 상태 의존 예제 프로필에서 729 probe 최대 편차 en 0.0034, mu 상대 1.79%,
  속도 0.0392 m/s로 사전 등록 허용오차(en 0.02, mu 5%) 이내. 조회표는 근사 표현이며 분석적 평가기와
  일치한다는 것이 비트 단위 재현성 주장은 아니다.

### 남은 한계

- 실측 충돌 자료가 없어 경험적 보정과 코트 분류, 온도·마모 효과는 검증되지 않았다.
- 스핀 출구 관측은 160 학습 레코드 중 69, 40 시험 레코드 중 18로 부분 관측이며 완전한 3D 스핀 검증이 아니다.
- Unity Editor/EditMode/PlayMode와 후보 화면 검토는 이 호스트에서 실행하지 않았다(NOT_RUN).
- v3의 플랫폼 간 실행은 NOT_TESTED다. mvp-2의 Linux/Mac 비교 결과는 v3로 이전되지 않는다.
