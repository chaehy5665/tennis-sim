# TennisSim MVP

그래픽과 독립된 단식 한 세트 테니스 시뮬레이터. 승자를 먼저 추첨하지 않고, 전술 후보 → 실행 오차 → 궤적 → 이동/타격 가능성 → 규칙 판정으로 득점한다. Unity 프로젝트와 외부 실행 서비스는 없다.

현재 환경에서 검증한 SDK: **10.0.401**, .NET runtime **10.0.12**, Linux x64. Core는 **netstandard2.1 / C# 8.0**, CLI와 테스트는 net10.0이다. Core에 Unity, 파일 IO, 네트워크, 시각, JSON 의존성이 없다. SDK 10에서 빠진 `NETStandard.Library.Ref 2.1.0`만 최초 빌드 시 NuGet에서 복원한다. 별도 게임/테스트 프레임워크 패키지는 없다.

## 실행

저장소 루트에서 실행한다. 전역 SDK를 사용하는 경우 첫 줄은 생략한다.

```bash
export PATH="$PWD/.tools/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

dotnet build TennisSim.sln
dotnet run --project tests/TennisSim.Tests --no-build

# 포인트별 승자·종료 원인·점수, 최종 점수와 JSON
dotnet run --project src/TennisSim.Cli --no-build -- match \
  --seed 42 --out artifacts/sample-42.json

# 선수와 전술 교체. ID는 CLI에서 A/B로 고정하며 이름/능력은 JSON에서 읽는다.
dotnet run --project src/TennisSim.Cli --no-build -- match \
  --seed 42 --player-a examples/players/server.json \
  --player-b examples/players/defender.json \
  --tactics-a examples/tactics/backhand-aggressive.json \
  --instructions examples/instructions.json \
  --out artifacts/tactical-42.json

# 같은 선수 능력, 상대 전술, 시작 서버/엔드, 고정 seed 목록을 네 정책에 재사용
dotnet run --project src/TennisSim.Cli --no-build -- compare \
  --seeds 11,22,33,44,55 --out artifacts/comparison.json

# 독립 포인트마다 새 엔진, 체력 초기화, seed/server/end 교대
dotnet run --project src/TennisSim.Cli --no-build -- points \
  --count 1000 --seed 100 --out artifacts/points-1000.json

# 기록 재생: Core를 재실행하지 않고 저장된 득점 사건을 텍스트 표시
dotnet run --project src/TennisSim.Cli --no-build -- replay \
  --input artifacts/sample-42.json

# 재시뮬레이션: 저장된 입력/지시를 다시 실행하고 전체 JSON 비교
dotnet run --project src/TennisSim.Cli --no-build -- resimulate \
  --input artifacts/sample-42.json --chunk 137 \
  --out artifacts/resimulated-42.json
```

테스트는 의도적으로 패키지 없는 실행형 검증 프로그램이다. **`dotnet test`는 이 테스트를 실행하지 않는다.** 위 `dotnet run --project tests/TennisSim.Tests`를 사용한다. 실패가 있으면 exit code 1을 반환한다. CLI는 정상 성공 0, 시뮬레이션 제한/재현성 불일치/배치 실패 1, 입력 오류 2를 반환한다.

`--quiet`은 경기의 포인트별 출력을 생략한다. `--chunk N`은 한 번에 엔진에 전달하는 tick 수이며 물리 dt나 재생 배속이 아니다. `--config examples/config.json`으로 물리/기술 제한을 읽는다. 기본 선수 프리셋은 `server`, `baseline`, `defender`; 전술 프리셋은 `balanced`, `backhand`, `safe`, `aggressive`이다. `help`에서 명령을 확인할 수 있다.

`compare`는 A의 초기 전술만 바꾼다. 기본 A/B는 모두 baseline이다. `--player-a`, `--player-b`, `--tactics-b`, `--config`로 동일하게 적용할 비교 조건을 지정할 수 있다. `--instructions`를 전달하면 모든 조건에 같은 지시를 적용하므로, 지시가 초기 전술을 덮어쓸 수 있음을 주의한다. JSON은 seed별 결과와 분자/분모, 목표 및 실제 첫 착지 좌표를 보존한다. 적은 세트의 승률은 전술 우열의 증거가 아니다.

## 구성과 범위

- `src/TennisSim.Core`: 좌표, 점수/서브 규칙, 궤적, 선수 이동, 전술, 고정 tick 실행, 상태/이벤트 계약.
- `src/TennisSim.Cli`: JSON, 파일, 단일 경기, 독립 포인트, 짝지은 전술 비교, 텍스트 기록 재생.
- `tests/TennisSim.Tests`: 규칙/수학/이동/전술/재현성/통합 검증.
- [MODEL.md](docs/MODEL.md): 단위·공식·선택 가중치·모델 한계.
- [REPLAY_CONTRACT.md](docs/REPLAY_CONTRACT.md): 상태, 사건, 준비 동작 연결 및 재생 권한.
- [VALIDATION.md](docs/VALIDATION.md): 실제 실행 결과와 규모.

큰 기록은 `artifacts/`에 생성되며 `.gitignore`로 제외한다. `.tools/`, `bin/`, `obj/`도 제외한다. 초기 폴더는 Git 저장소가 아니었으며 이 작업은 Git 초기화, commit, push를 수행하지 않는다.

구현 규칙: 어드밴티지 게임, 6게임/2게임 차, 6–6의 7점/2점 차 타이브레이크, 1·2구와 더블 폴트, 대각선 서비스 박스, 서비스 렛, 일반 게임/타이브레이크 서버와 엔드 교대, 라인 접촉 인, 첫 바운드 아웃, 반환 전 두 번째 바운드.

미구현: 발리, 스매시, 드롭샷, 복식, 다세트, 풋폴트, 신체/라켓/네트 접촉 위반, 타이브레이크 뒤 다음 세트, 대회별 규칙, 정교한 네트/라켓/스핀 물리, 애니메이션과 3D 표시, 임의 시점 저장/재개. 타격 접촉 판정은 고정 tick 끝에서 수행하며 최대 8.33ms 시간 양자화가 있다. 네트와 바운드는 tick 내부 충돌 시각을 계산한다. 모든 테니스 규칙 구현을 주장하지 않는다.

`UNITY_RUNTIME_VERIFIED=false`, `REALISM_CALIBRATED=false`. 계수와 가상 선수는 게임용 가정이다. 랠리 길이, 낮은 타점에서의 공격 후보 탈락, 프리셋 간 점수 편향을 실측과 비교하지 않았다. 플랫폼 간 비트 단위 재현성도 주장하지 않는다.

다음 최소 단계: **같은 Core 기록을 단순 3D 코트·선수 캡슐·공으로 표시해 이동과 타격 시각을 대조한다.** 렌더러가 경기 결과를 보정하지 않도록 유지한다.
