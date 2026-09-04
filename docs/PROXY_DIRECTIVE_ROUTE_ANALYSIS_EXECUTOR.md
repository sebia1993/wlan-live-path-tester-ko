# 프록시 경로 분석 콜백 실행 경계

`ProxyDirectiveRouteAnalysisExecutor`는 출처 선택과 실행 계획 검증을 통과한 경우에만 실제 프록시 엔드포인트 분석 콜백을 호출합니다. 이 계층 자체는 DNS·라우팅·HTTP를 수행하지 않습니다.

## 실행 상태

```text
Completed
DirectOnly
Blocked
Unavailable
Canceled
Failed
```

### Completed

- 계획 상태 `AnalyzeProxyEndpoints`
- 취소가 요청되지 않음
- 분석 콜백을 정확히 한 번 호출
- 콜백이 null이 아닌 결과를 반환

메모리 내 분석 결과는 호출자에게 반환하지만 기본 JSON 직렬화에서는 제외합니다.

### DirectOnly

계획이 `DirectOnly`이면 콜백을 호출하지 않습니다.

```text
callback count = 0
Network lookup = 없음
```

### Blocked

출처 판정이 `Invalid`이거나 선택 상태·원문·파싱 결과가 서로 모순되면 콜백을 호출하지 않습니다.

### Unavailable

사용 가능한 대상별·수동 프록시 출처가 없으면 콜백을 호출하지 않습니다. DIRECT로 추정하지 않습니다.

### Canceled

다음 두 경우를 구분합니다.

- 콜백 호출 전 토큰이 이미 취소됨: 콜백 0회
- 콜백 내부에서 `OperationCanceledException`: 취소 결과로 변환

취소 예외 메시지와 지시문 원문은 결과에 복사하지 않습니다.

### Failed

- 콜백이 null 결과 반환
- 콜백이 일반 예외 발생

예외 타입·메시지·프록시 호스트·토큰은 공개 결과에 반사하지 않습니다.

## 콜백 계약

```csharp
Func<string, CancellationToken, Task<TAnalysis>>
```

전달값:

- 문자열: 출처 선택 정책이 승인한 메모리 전용 지시문
- 토큰: 호출자가 제공한 동일 CancellationToken

호출 횟수:

- 승인된 프록시 계획: 최대 1회
- DIRECT·Invalid·Unavailable·사전 취소: 0회

콜백이 여러 프록시 후보를 순서대로 처리하는 책임은 실제 Windows 분석기 구현에 있습니다. Executor는 전체 분석 작업을 한 번만 시작하도록 제어합니다.

## 직렬화 경계

실행 결과의 안전 필드는 명시적인 camelCase JSON 이름을 사용합니다.

```text
status
planStatus
planCode
sourceKind
selectionStatus
proxyEndpointCount
directDirectiveCount
hasParseErrors
message
redactedDisplay
hasCompletedAnalysis
```

다음 값은 `[JsonIgnore]`입니다.

- 선택된 지시문 원문
- `TAnalysis` 분석 payload

파싱 결과의 각 프록시 호스트도 기본 JSON에서는 SHA-256 앞 10자의 지문만 노출됩니다.

## 부분 파싱

`SelectedWithWarnings`에 유효한 프록시 후보가 있으면 콜백 실행은 허용합니다.

```text
Status=Completed
HasParseErrors=true
```

다만 후속 내부 DIRECT↔프록시 전체 비교는 제외된 fallback 구간이 있으므로 완전 증거로 보지 않아야 합니다.

## 통신 경계

Executor가 직접 수행하는 작업:

- 실행 계획 생성
- 상태 switch
- CancellationToken 확인
- 콜백 최대 1회 호출
- 취소·예외를 안전한 결과로 변환

직접 수행하지 않는 작업:

- DNS
- Windows 라우팅 API
- TCP
- HTTP/HTTPS
- PAC/WPAD 다운로드
- 프록시 인증
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·결과 업로드

실제 네트워크 동작은 승인된 계획에서 사용자가 실행한 콜백 구현에만 존재합니다.

## 자동 검증

Core SelfTest는 다음을 확인합니다.

- 대상별 프록시 콜백 정확히 1회
- 선택된 원문과 같은 CancellationToken 전달
- 부분 파싱 프록시 실행과 `HasParseErrors`
- DIRECT·Invalid·Unavailable 콜백 0회
- 사전 취소 콜백 0회
- 콜백 취소·일반 예외의 원문 비반사
- null 분석 결과의 Failed 처리
- 기본 JSON·ToString에서 지시문 원문과 분석 payload 비노출
