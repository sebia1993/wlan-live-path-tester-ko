# Windows 프록시 출처 reader와 안전 실행 코디네이터

이 계층은 기존 Windows 수동 프록시 설정 reader와 대상별 WinHTTP·PAC/WPAD 판정 reader를 Core의 fail-closed 출처 선택 파이프라인에 연결합니다.

Windows API를 새로 구현하지 않고 기존 reader 결과를 다음 두 인터페이스로 감싸는 구조입니다.

```text
IWindowsManualProxyConfigurationSource
IWindowsTargetProxyDecisionSource
```

## 전체 흐름

```text
사용자가 선택한 HTTP·HTTPS 대상 URL
  ↓
Windows 수동 프록시 설정 reader
  ↓
자동 검색 또는 PAC가 설정된 경우에만
대상별 WinHTTP·PAC/WPAD 판정 reader
  ↓
ProxyDirectiveSourceSnapshot
  ↓
출처 선택 정책
  ↓
실행 계획 검증
  ↓
승인된 경우에만 프록시 엔드포인트 분석 콜백
```

`WindowsProxyDirectiveSourceExecutionCoordinator`가 이 흐름을 하나의 호출로 제공합니다.

## 수동 설정 reader 계약

```csharp
Task<WindowsManualProxyConfigurationReadResult> ReadAsync(
    CancellationToken cancellationToken)
```

결과:

```text
Status
ManualProxyConfigured
ManualProxyDirective
AutoDetectEnabled
PacConfigured
PacUrl
```

다음 두 원문은 메모리 전용이며 기본 JSON에서 제외합니다.

```text
ManualProxyDirective
PacUrl
```

수동 설정 reader가 예외를 발생시키거나 `Success`가 아닌 상태를 반환하면 다음과 같이 정규화합니다.

```text
ManualConfigurationStatus=Failed
ManualProxyConfigured=false
원문 설정=null
```

이 경우 대상별 판정 reader와 분석 콜백을 호출하지 않습니다.

## 대상별 판정 reader 계약

```csharp
Task<WindowsTargetProxyDecisionReadResult> ReadAsync(
    Uri targetUri,
    WindowsManualProxyConfigurationReadResult manualConfiguration,
    CancellationToken cancellationToken)
```

결과:

```text
Status
IsDirect
DirectiveText
```

`DirectiveText`는 메모리 전용이며 기본 JSON에서 제외합니다.

대상별 reader는 수동 reader가 확인한 다음 값을 사용할 수 있습니다.

- 자동 검색 사용 여부
- PAC 설정 여부와 메모리 전용 PAC URL
- 수동 프록시 설정 여부와 메모리 전용 문자열

이를 통해 기존 WinHTTP 구현을 중복 P/Invoke 없이 어댑터로 연결할 수 있습니다.

## 대상별 판정 호출 조건

다음 중 하나가 true일 때만 대상별 reader를 호출합니다.

```text
AutoDetectEnabled
PacConfigured
```

둘 다 false이면:

```text
TargetDecisionStatus=NotAttempted
```

수동 설정 결과만 출처 선택 정책으로 전달합니다.

## 대상별 판정 실패

대상별 reader가 예외를 발생시키거나 성공하지 못하면:

```text
TargetDecisionStatus=Failed
```

수동 프록시가 유효하더라도 자동 fallback하지 않습니다.

```text
Blocked
analyzer callback=0회
```

판정을 실제로 시도했지만 실패한 상태를 “미시도”로 바꾸지 않기 위한 경계입니다.

## 대상별 DIRECT

대상별 reader가 다음 결과를 반환하면:

```text
Status=Success
IsDirect=true
DirectiveText=null 또는 DIRECT-only
```

출처 선택 정책은 대상별 DIRECT를 우선합니다.

```text
DirectOnly
analyzer callback=0회
```

수동 프록시가 별도로 설정돼 있어도 해당 대상에 프록시 엔드포인트를 추정하지 않습니다.

## 실행 코디네이터

```csharp
Task<WindowsProxyDirectiveSourceExecutionResult<TAnalysis>>
    ReadAndExecuteAsync<TAnalysis>(
        Uri targetUri,
        Func<string, CancellationToken, Task<TAnalysis>> analyzer,
        CancellationToken cancellationToken = default)
```

상태:

```text
Completed
DirectOnly
Blocked
Unavailable
Canceled
Failed
```

### Completed

- reader 스냅샷 생성 성공
- 대상별·수동 출처 선택 완료
- 실행 계획 `AnalyzeProxyEndpoints`
- 분석 콜백 최대 1회 완료

### DirectOnly

프록시 엔드포인트가 없어 분석 콜백을 호출하지 않습니다.

### Blocked

reader 실패, 대상별 판정 모순 또는 실행 계획 오류로 분석을 차단합니다.

### Unavailable

대상별 판정을 수행하지 않았고 사용할 수 있는 수동 프록시 설정도 없습니다.

### Canceled

- 호출 전에 토큰이 이미 취소됨
- 수동 설정 reader 또는 대상별 reader가 취소됨
- 분석 콜백이 취소됨

사전 취소에서는 두 reader와 분석 콜백을 모두 호출하지 않습니다.

### Failed

지원하지 않는 대상 URL, reader 외부 계약 오류 또는 분석 실패입니다. 예외 원문은 결과에 포함하지 않습니다.

## 대상 URL 검증

대상별 프록시 판정에는 절대 HTTP 또는 HTTPS URL만 허용합니다.

```text
https://download.example/file.bin  허용
http://download.example/file.bin   허용
ftp://download.example/file.bin    거부
/file.bin                           거부
```

지원하지 않는 URL은 reader 호출 전에 차단합니다.

## 개인정보 경계

다음은 현재 프로세스 메모리에서만 유지합니다.

- 수동 프록시 원문
- 대상별 PAC/WPAD 지시문 원문
- PAC URL
- 사용자가 선택한 외부 대상 URL
- 분석 콜백 payload

기본 JSON에서 제외하는 객체:

```text
ProxyDirectiveSourceSnapshot
ProxyDirectiveRouteAnalysisExecutionResult<TAnalysis>
TAnalysis
```

공개 결과에는 비식별 `ProxyDirectiveDecisionAudit`만 포함합니다.

감사 데이터 예:

```text
대상 판정 읽기 상태
수동 설정 읽기 상태
선택 출처와 고정 코드
실행 계획과 고정 코드
프록시 후보 수
DIRECT 수
파싱 오류·경고 수
네트워크 조회 허용 여부
완료·취소·차단 단계
```

예외 메시지, 호스트, PAC URL, 전체 지시문과 분석 payload는 포함하지 않습니다.

## 통신 경계

이 어댑터와 코디네이터 자체가 수행하는 작업:

- 기존 reader 호출 순서 제어
- reader 결과 정규화
- 스냅샷 생성
- Core 출처 선택·실행 계획·Executor 호출
- 승인된 분석 콜백 최대 1회 호출

직접 구현하거나 추가하지 않는 작업:

- WinHTTP P/Invoke
- PAC/WPAD 다운로드·실행
- DNS
- Windows 라우팅 API
- TCP·HTTP·HTTPS
- 프록시 인증
- 프록시 관리 API
- 외부 분석 API·AI 또는 로컬 AI
- 텔레메트리·자동 업데이트·결과 업로드

실제 네트워크 동작은 기존 Windows reader 또는 사용자가 명시적으로 실행한 승인된 분석 콜백에만 존재합니다.

## 자동 검증

WindowsSmoke는 실제 외부 DNS·프록시·PAC를 사용하지 않고 주입식 source로 다음을 확인합니다.

1. 자동 검색·PAC가 꺼져 있으면 대상별 reader 0회, 수동 프록시 분석 1회
2. 자동 검색 또는 PAC가 켜져 있으면 대상별 reader 1회
3. 대상별 지시문이 수동 프록시보다 우선
4. 대상별 판정 실패 후 수동 fallback과 분석 콜백 0회
5. 대상별 DIRECT에서 분석 콜백 0회
6. 수동 설정 읽기 실패 후 대상 reader·분석 콜백 0회
7. 사전 취소에서 모든 reader·콜백 0회
8. 대상별 reader 취소 후 분석 콜백 0회
9. FTP 대상이 reader 호출 전에 차단됨
10. 코디네이터·수동 결과·대상 결과 기본 JSON에 프록시 원문·PAC URL·분석 payload 비노출
