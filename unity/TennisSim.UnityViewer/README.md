# TennisSim Unity Replay Viewer

**SOURCE_IMPLEMENTED_UNVERIFIED** — Unity Editor를 사용할 수 없어 소스와 .NET 데이터 코드만 검증했다. ProjectSettings/Packages/Replay.unity는 실제 Editor의 준비 단계에서 생성한다. 추측한 Editor/패키지 버전을 넣지 않았다.

[실행·준비·조작·검증 안내](../../docs/UNITY_VIEWER.md)

[실제 검증 결과와 남은 제한](../../docs/UNITY_VALIDATION.md)

[JSON 및 시간축 계약](../../docs/REPLAY_CONTRACT.md)

저장소 루트에서 `scripts/prepare-unity-replay.sh` 실행 후 실제 Editor 경로를 `UNITY_EDITOR`에 설정하고 `scripts/validate-unity-viewer.sh prepare`, `compile` 순서로 실행한다. Editor 메뉴 **TennisSim → Create or open replay scene**, **Play**. Inspector 참조 연결은 필요 없다.
