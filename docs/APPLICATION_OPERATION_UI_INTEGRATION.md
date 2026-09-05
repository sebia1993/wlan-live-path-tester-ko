# 앱 작업 조정기 WPF 연결

## 이번 연결 범위

Core `ApplicationOperationCoordinator` 하나를 `MainWindow`가 소유합니다. 기존 내부 DIRECT–프록시 경로 비교·Windows 프록시 가져오기와 아래 기능이 같은 조정기를 공유합니다.

- 내부·외부 다운로드 측정
- 기존 대상별 프록시 경로 판정
- 브라우저 다운로드 관찰

`ApplicationOperationUiSession`은 별도 실행 잠금이 아닙니다. 운영 코드에서는 반드시 `MainWindow`의 `_applicationOperations`를 주입하며, 독립 coordinator 생성은 합성 테스트에만 사용합니다.

## 실행·취소·완료

```text
입력 검증
  → 공통 작업 lease 획득
  → 획득 실패: 측정·WinHTTP·관찰 runner 미호출
  → 다른 탭 잠금
  → 기존 기능 실행
  → 기존 UI·메모리 상태 정리
  → 다른 탭 상태 복원
  → Core lease 해제
```

같은 종류의 중복 실행과 서로 다른 기능의 중복 실행을 모두 막습니다. 취소는 callback을 최대 한 번 요청할 뿐입니다. 실제 작업 종료 전까지 lease와 busy 상태는 유지됩니다.

다운로드 측정의 `OperationCanceledException`은 사용자 취소 토큰이 요청된 경우 일반 오류가 아닌 취소 완료로 표시합니다.

## 탭 잠금

작업을 시작한 탭만 사용 가능하게 유지합니다. 작업 중 늦게 추가된 탭도 잠급니다. 원래 비활성화돼 있던 탭은 완료 후에도 비활성화 상태를 유지합니다.

복원은 기존 `IsEnabled`의 local value와 binding을 기준으로 합니다.

- local value가 없던 탭: `ClearValue`로 상속·스타일 평가 복원
- 명시적 Boolean이 있던 탭: 이전 local value 복원
- binding이 있던 탭: 실행 중 잠시 중지한 binding을 다시 연결하고 최신 source 값으로 평가

작업 중 binding source가 바뀌어도 다른 탭 잠금이 풀리지 않습니다. 완료 시 이전 effective Boolean을 강제로 덮어쓰지 않습니다. CollectionChanged 구독은 복원 시 해제합니다.

## 오래된 진행 알림

`Progress<T>`는 UI Dispatcher에 알림을 예약합니다. 관찰이 이미 끝났거나 새 관찰이 시작된 뒤 이전 알림이 도착할 수 있습니다.

관찰 진행 표시 전 다음 조건을 확인합니다.

- UI lease가 현재 세션의 활성 lease인지
- 관찰 CancellationTokenSource가 같은 인스턴스인지
- 창이 닫히지 않았는지

조건을 만족하지 않으면 알림을 무시합니다. 이전 실행의 늦은 완료·중복 Dispose도 다음 실행의 탭을 풀지 않습니다.

## 창 닫기

이번 UI adapter가 소유한 작업이 실행 중이면 첫 Closing을 취소합니다.

1. shutdown 플래그로 새 작업 시작 차단
2. 취소 callback이 있으면 한 번 요청
3. Dispatcher를 막지 않고 실제 작업 완료를 await
4. 기존 기능 정리와 탭 복원이 끝난 후 lease 해제
5. 창 닫기 재시도

기존 경로 가져오기·경로 보고서의 별도 deferred-close 처리는 유지합니다. 해당 작업에서는 이번 adapter가 중복 Close continuation을 시작하지 않습니다.

기존 프록시 경로 resolver는 동기 WinHTTP 호출입니다. 강제로 handle을 닫거나 즉시 취소됐다고 표시하지 않고 실제 반환까지 기다립니다. UI를 막는 `Task.Wait()` 또는 `.Result`는 사용하지 않습니다. 모든 네이티브 호출을 즉시 취소하는 비동기 WinHTTP 구현은 별도 작업입니다.

최종 Close를 다른 Closing handler가 거절하면 shutdown 플래그를 해제합니다. 이미 요청한 작업 취소를 되돌리는 것은 아닙니다.

## 단계적 이행

기존 경로 비교·Windows 프록시 가져오기는 Core lease를 이미 사용합니다. 이번 변경은 이 기능을 덮어쓰거나 두 번째 coordinator를 만들지 않습니다.

아직 모든 기능을 공통 UI adapter로 옮긴 것은 아닙니다. 다음은 후속 이행 대상입니다.

- 반복 측정과 일반 로컬 경로 확인
- 경로 비교·통합·관찰 등 보고서 저장
- 자동 어댑터 새로고침과 네트워크 환경 수집
- 기능별 버튼 상태 및 Closing handler의 중앙화

호환 기간에는 기존 busy 플래그와 peer-tab 처리를 유지합니다. 따라서 이번 단계를 앱 전체 상태 머신 이행 완료로 표현하면 안 됩니다.

## 자동 테스트

`tests/WlanLivePathTester.UiOperationSmoke`는 실제 WPF STA Dispatcher에서 운영 코드의 UI adapter와 MainWindow를 사용합니다. 창은 표시하지 않으므로 Loaded에서 시작하는 WLAN·어댑터 읽기를 실행하지 않습니다. 측정 runner에는 합성 delegate를 주입하고, 프록시 클릭은 실행권이 이미 점유된 차단 경로만 호출합니다.

검사 그룹:

1. 측정·프록시 판정·관찰 3×3 중복 실행 거부
2. 기존 경로/import와 새 UI adapter가 같은 Core coordinator를 공유
3. 늦게 추가·제거된 탭과 collection listener 정리
4. binding 변경 중 잠금 및 완료 후 최신 값 복원
5. 취소 callback 한 번, 취소 후 busy 유지, 오래된 lease 격리
6. 취소 가능·불가능 작업의 shutdown 대기
7. 예외 발생 시 using 정리
8. worker thread의 UI 상태 변경 거부
9. 실제 측정 진입점의 중복 실행·프록시 클릭 차단
10. 실제 MainWindow Closing이 작업 완료 후에만 창을 닫음

각 비동기 테스트 대기는 상한을 갖습니다. `verify-release.ps1`에서 기존 6개 테스트 프로젝트 다음에 새 테스트를 실행하며 실패 시 배포 검증을 중단합니다. CI 통과 여부는 해당 PR의 정확한 commit 실행 결과로 확인합니다.

## 통신·데이터 경계

새 adapter는 Dispatcher, 탭 상태, 공통 lease와 기존 취소 함수만 사용합니다. 외부 요청·수신 본문·프록시 인증 처리 자체는 바꾸지 않습니다.

런타임 NuGet, AI·로컬 AI, 외부 분석 API, 텔레메트리, 업로드, 자동 업데이트를 추가하지 않습니다. URL·프록시 원문·SSID·BSSID·인터페이스 GUID를 작업 식별자로 저장하지 않습니다.

## 실환경 확인

사용자는 회사 Windows 11에서 실제 측정 중 중지·창 닫기, 브라우저 관찰 중 빠른 재시작, 프록시 판정 중 화면 조작과 절전 복귀를 확인합니다. 합성 UI 테스트 통과를 회사 PAC/WPAD·407·드라이버 검증 완료로 대체하지 않습니다.
