# 앱 전역 단일 실행 작업 조정기

`ApplicationOperationCoordinator`는 다운로드 측정, 브라우저 관찰, 경로 비교, Windows 프록시 가져오기와 보고서 저장이 동시에 실행되지 않도록 만드는 Core 상태 머신입니다.

이 클래스 자체는 UI, Windows API, DNS, HTTP, 파일 저장 또는 프록시 기능을 호출하지 않습니다. 각 기능이 가진 기존 취소 함수를 등록하고 시작·취소·완료·종료 대기 상태만 관리합니다.

## 작업 종류

```text
DownloadMeasurement
ProxyRouteResolution
RepeatedMeasurement
BrowserObservation
RouteEvidence
RouteComparison
WindowsProxyImport
RouteComparisonReportSave
DiagnosticReportSave
NetworkAdapterDiagnostics
NetworkEnvironmentCapture
```

`None`은 idle 스냅샷에만 사용하며 시작할 수 없습니다. 정의되지 않은 enum 값도 거부합니다.

## 단일 lease

작업 시작은 다음 형태입니다.

```csharp
ApplicationOperationStartResult start = coordinator.TryBegin(
    ApplicationOperationKind.RouteComparison,
    requestCancellation: cancellationTokenSource.Cancel);
```

가능한 결과:

```text
Started
Busy
ShutdownPending
```

### Started

- 고유한 증가형 `OperationId` 할당
- 작업 종류와 시작 시각 기록
- `ApplicationOperationLease` 반환
- 취소 callback 존재 여부만 스냅샷에 기록

### Busy

다른 작업이 활성 상태입니다. 새 lease를 만들지 않고 현재 활성 작업의 안전한 스냅샷을 반환합니다.

### ShutdownPending

창 종료 또는 앱 종료 대기가 시작됐습니다. 현재 작업 유무와 관계없이 새 작업을 시작하지 않습니다.

## 완료

작업은 `ApplicationOperationLease.Complete()` 또는 `Dispose()`로 끝냅니다.

```csharp
using ApplicationOperationLease lease = start.Lease!;
try
{
    await operation();
}
finally
{
    lease.Complete();
}
```

보장 사항:

- 같은 lease의 중복 완료는 첫 번째만 유효
- 이전 작업의 stale lease가 새 작업을 완료할 수 없음
- 완료 시 `Completion` task 종료
- coordinator 스냅샷이 idle로 전환
- 종료 대기자는 실제 lease 완료 뒤에만 진행

작업 ID를 비교하므로 늦게 실행된 `finally`, 중복 이벤트 또는 취소 callback이 새 작업의 상태를 해제하지 않습니다.

## 취소

취소 가능한 작업은 시작 시 callback을 등록합니다.

```text
Requested
AlreadyRequested
NotSupported
NotActive
CallbackFailed
```

### Requested

취소 callback을 정확히 한 번 호출했습니다.

### AlreadyRequested

같은 작업에 이미 취소를 요청했습니다. callback을 다시 호출하지 않습니다.

### NotSupported

활성 작업은 있지만 취소 callback이 등록되지 않았습니다. 작업 상태를 임의로 완료하지 않습니다.

### NotActive

idle 상태이거나 요청한 lease가 더 이상 활성 작업이 아닙니다.

### CallbackFailed

취소 callback이 예외를 발생시켰습니다.

- 예외를 coordinator 밖으로 전파하지 않음
- 예외 메시지나 사용자 데이터를 저장하지 않음
- `CancellationRequested=true`
- `CancellationCallbackFailed=true`
- 활성 작업은 실제 완료될 때까지 유지
- callback을 자동 재시도하지 않음

동시에 여러 스레드가 취소를 요청해도 callback은 최대 한 번만 실행합니다.

## 종료 대기

```csharp
ApplicationOperationShutdownResult result =
    await coordinator.RequestShutdownAsync();
```

동작 순서:

1. `ShutdownRequested=true`
2. 이후 새 작업 시작 거부
3. 활성 작업에 취소 callback이 있으면 한 번 요청
4. 활성 lease의 실제 완료까지 대기
5. idle·shutdown 스냅샷 반환

`requestCancellation:false`를 사용하면 새 작업은 차단하지만 현재 작업 취소는 요청하지 않고 자연 완료를 기다립니다.

대기 호출자의 `CancellationToken`은 종료 대기만 취소합니다. 활성 작업을 완료하거나 shutdown 상태를 자동 해제하지 않습니다.

창 닫기가 사용자 판단으로 취소된 경우 다음을 호출할 수 있습니다.

```csharp
coordinator.CancelShutdownRequest();
```

이 호출은 이미 취소된 작업을 복원하지 않습니다. 단지 shutdown 플래그를 해제해 현재 작업 완료 뒤 새 작업을 허용합니다.

## 상태 스냅샷

```text
IsBusy
ShutdownRequested
OperationId
Kind
StartedAt
SupportsCancellation
CancellationRequested
CancellationCallbackFailed
```

idle 상태:

```text
IsBusy=false
OperationId=null
Kind=None
```

스냅샷에는 다음 값이 없습니다.

- URL
- 프록시 문자열 또는 호스트
- SSID·BSSID
- 인터페이스 GUID·이름·설명
- 파일 경로
- 예외 메시지
- 취소 delegate

작업 종류는 고정 enum이며 사용자가 입력한 label을 받지 않습니다.

## 상태 알림

`StateChanged`는 다음 전이에 발행됩니다.

- 작업 시작
- 취소 요청
- 취소 callback 실패
- 작업 완료
- shutdown 요청·해제

observer callback은 coordinator lock 밖에서 실행합니다. 하나의 observer가 예외를 발생시켜도:

- 상태 전이를 롤백하지 않음
- 다른 observer 호출을 막지 않음
- 예외 원문을 coordinator에 저장하지 않음

UI observer는 자체 로그와 Dispatcher 수명 관리를 담당해야 합니다.

## UI 통합 원칙

각 기존 기능의 취소·완료 로직은 유지하면서 외곽에 lease를 추가합니다.

예:

```text
측정 CancellationTokenSource 생성
  → coordinator.TryBegin(DownloadMeasurement, cts.Cancel)
  → Started인 경우만 기존 측정 실행
  → 기존 finally에서 lease.Dispose()
```

다음 단계의 통합 대상:

```text
기본 다운로드 측정
반복 측정
브라우저 관찰
프록시 경로 판정
경로 근거 수집
Windows 프록시 가져오기
내부 DIRECT–프록시 비교
경로 비교 보고서 저장
통합 진단 보고서 저장
```

기능별 기존 busy Boolean과 버튼 제어는 첫 통합 단계에서 호환성을 위해 유지할 수 있습니다. 최종적으로 coordinator 스냅샷을 공통 UI 상태의 단일 근거로 전환합니다.

## 통신·데이터 경계

조정기가 직접 수행하는 작업:

- lock 기반 상태 전이
- 증가형 작업 ID 할당
- 취소 callback 최대 한 번 호출
- `TaskCompletionSource`를 이용한 idle·종료 대기
- 고정 enum과 Boolean 스냅샷 발행

직접 수행하지 않는 작업:

- DNS 또는 Windows 라우팅 API
- HTTP·HTTPS
- 프록시 연결·인증
- PAC/WPAD
- 파일 생성
- 외부 API
- AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드
- 자동 업데이트

## 자동 검증

Core SelfTest는 다음을 확인합니다.

1. 작업 시작·완료와 idle 전환
2. `None`과 정의되지 않은 enum 거부
3. 활성 작업 중 두 번째 시작의 `Busy`
4. 64개 동시 시작에서 정확히 한 lease만 성공
5. 32개 동시 취소에서 callback 정확히 한 번
6. 취소 미지원·idle·완료 lease 상태 구분
7. 취소 callback 예외 격리와 구조화 상태
8. stale lease가 새 작업을 완료하지 못함
9. shutdown이 새 작업을 차단하고 활성 작업 취소·완료를 기다림
10. 취소하지 않는 shutdown 대기
11. shutdown 해제 후 새 작업 재허용
12. idle 대기 호출자의 CancellationToken
13. 실패한 observer와 정상 observer 격리
14. 취소·완료 경쟁 후 idle 일관성
15. 기본 JSON에서 callback closure·lease completion 비노출
