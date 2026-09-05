# 프록시 경로 분석 계획과 실행 경계 V3

프록시 출처 선택 결과를 곧바로 DNS·Windows 경로 분석기에 전달하지 않습니다. 먼저 선택 상태·원문·파싱 결과의 일관성을 다시 검사해 명시적인 실행 계획으로 변환하고, 허용된 계획에서만 사용자 실행 분석 콜백을 최대 한 번 호출합니다.

## 전체 흐름

```text
Windows reader 상태·지시문
  ↓
ProxyDirectiveSourceSelectionPolicy
  ↓
ProxyDirectiveRouteAnalysisPlanPolicy
  ↓
ProxyDirectiveRouteAnalysisExecutor
  ├─ AnalyzeProxyEndpoints → 사용자 실행 콜백 최대 1회
  ├─ DirectOnly           → 콜백 0회
  ├─ Blocked              → 콜백 0회
  └─ Unavailable          → 콜백 0회
```

계획 생성과 Executor 자체는 DNS·라우팅·HTTP를 수행하지 않습니다. 실제 콜백 구현이 기존 Windows 로컬 경로 분석기를 호출할 때만, 그리고 사용자가 명시적으로 실행했을 때만 DNS와 Windows 최적 인터페이스 조회가 발생할 수 있습니다.

## 실행 계획 상태

```text
AnalyzeProxyEndpoints
DirectOnly
Blocked
Unavailable
```

### AnalyzeProxyEndpoints

다음 조건을 모두 충족해야 합니다.

- 선택 상태가 `Selected` 또는 `SelectedWithWarnings`
- 메모리 전용 선택 지시문이 비어 있지 않음
- 파싱 결과가 존재
- 비-DIRECT 프록시 후보가 1개 이상 존재

이 상태에서만 다음 값이 true입니다.

```text
ShouldAnalyzeProxyEndpoints=true
NetworkLookupAllowed=true
```

`SelectedWithWarnings`도 유효한 프록시 후보는 분석할 수 있지만, 제외된 fallback 구간이 있으므로 계획에 `HasParseErrors=true`를 유지합니다. 이후 내부 DIRECT↔프록시 전체 비교는 완전 증거로 취급하면 안 됩니다.

### DirectOnly

다음 조건을 모두 충족합니다.

- 선택 상태 `Direct`
- 선택 지시문과 파싱 결과 존재
- 프록시 후보 없음
- DIRECT 지시문 1개 이상
- 파싱 Error 없음

결과:

```text
NetworkLookupAllowed=false
```

프록시 엔드포인트가 없으므로 프록시 호스트 DNS·Windows 경로 분석 콜백을 호출하지 않습니다. `ftp=DIRECT`와 같은 범위는 메모리에서 그대로 유지합니다.

### Blocked

다음 상황입니다.

- 출처 선택 상태가 `Invalid`
- 선택 상태와 원문·파싱 결과가 서로 모순됨
- `Selected`인데 실제 프록시 후보가 없음
- `Direct`인데 프록시 후보나 파싱 Error가 존재
- 정의되지 않은 계획 상태

실행 가능한 원문을 공개 결과에 남기지 않고 콜백을 호출하지 않습니다.

### Unavailable

대상별 판정과 수동 프록시 설정 중 사용할 출처가 없습니다.

- DIRECT 미추정
- 프록시 미추정
- 콜백 0회
- 네트워크 조회 없음

`Blocked`는 입력은 있으나 유효하지 않은 상태이고, `Unavailable`은 사용할 출처 자체가 없는 상태입니다.

## 계획 코드

```text
TargetSpecificProxySelected
ManualProxySelected
TargetSpecificDirect
ManualDirect
InvalidSourceDecision
MissingSourceDecision
InconsistentSelectionResult
```

UI·로그·보고서는 한국어 메시지를 파싱하지 않고 이 코드를 사용할 수 있습니다.

## Executor 상태

```text
Completed
DirectOnly
Blocked
Unavailable
Canceled
Failed
```

### Completed

- 계획이 `AnalyzeProxyEndpoints`
- 호출 전 취소 없음
- 콜백을 정확히 한 번 호출
- 콜백이 null이 아닌 결과 반환

분석 payload는 호출자에게 메모리로 반환하지만 기본 JSON 직렬화에서는 제외합니다.

### Canceled

다음 경우를 구분합니다.

```text
호출 전 토큰이 이미 취소됨
  → 콜백 0회

콜백이 OperationCanceledException으로 종료
  → Canceled
  → 예외 메시지 미반사
```

### Failed

다음 경우입니다.

- 콜백이 null 반환
- 콜백이 일반 예외 발생
- 분석 가능 계획인데 메모리 전용 지시문이 없음

프록시 호스트, 지시문 원문, 예외 타입·메시지와 분석 payload를 공개 결과에 복사하지 않습니다.

## 콜백 계약

```csharp
Func<string, CancellationToken, Task<TAnalysis>>
```

전달값:

- 문자열: 출처 선택 정책이 승인한 메모리 전용 지시문
- 토큰: 호출자가 제공한 동일 `CancellationToken`

호출 횟수:

| 계획 또는 상태 | 최대 호출 수 |
|---|---:|
| AnalyzeProxyEndpoints | 1 |
| DirectOnly | 0 |
| Blocked | 0 |
| Unavailable | 0 |
| 호출 전 취소 | 0 |

복수 프록시 후보의 세부 순서·DNS·Windows 인터페이스 판정은 콜백으로 연결되는 기존 분석기가 담당합니다. Executor는 전체 사용자 실행을 한 번만 시작하도록 제어합니다.

## 개인정보 경계

메모리 전용이며 기본 JSON에서 제외:

```text
ProxyDirectiveRouteAnalysisPlan.DirectiveText
ProxyDirectiveRouteAnalysisExecutionResult.Analysis
```

계획의 `ParseResult`는 안전한 구조화 진단을 위해 유지할 수 있지만, 각 `ProxyRouteDirective.Host`는 기존 `[JsonIgnore]` 경계로 제외됩니다.

안전 표시에는 다음만 포함합니다.

```text
계획·실행 상태
고정 코드
출처
선택 상태
프록시 후보 수
DIRECT 수
파싱 오류 존재 여부
완료 여부
고정 메시지
```

실행 결과는 명시적인 camelCase JSON 이름을 사용합니다.

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

## 통신 경계

계획 정책과 Executor가 직접 수행하는 작업:

- 상태·계약 검사
- CancellationToken 확인
- 승인된 콜백 최대 1회 호출
- 취소·예외를 고정 결과로 변환

직접 수행하지 않는 작업:

- Windows 프록시 설정 조회
- PAC/WPAD 다운로드 또는 실행
- DNS 조회
- Windows 라우팅 API
- TCP 연결
- HTTP·HTTPS 요청
- 프록시 인증
- 프록시 관리 API
- 외부 분석 API
- AI 또는 로컬 AI
- 텔레메트리
- 자동 업데이트
- 결과 업로드

## 자동 검증

Core SelfTest는 다음을 확인합니다.

1. 대상별 프록시의 `AnalyzeProxyEndpoints`
2. 수동 부분 파싱 프록시의 분석 허용과 `HasParseErrors`
3. 대상별·수동 DIRECT의 `DirectOnly`
4. Invalid의 `Blocked`
5. 출처 없음의 `Unavailable`
6. 승인된 콜백 정확히 1회와 동일 취소 토큰 전달
7. DIRECT·Invalid·Unavailable 콜백 0회
8. 사전 취소 콜백 0회
9. 콜백 취소·일반 예외의 원문 비반사
10. null 분석 결과의 `Failed`
11. 계획·실행 JSON 및 `ToString()`에서 지시문 원문과 분석 payload 비노출
