# 프록시 출처·실행 의사결정 감사 스냅샷

`ProxyDirectiveDecisionAudit`은 원문 PAC·프록시 주소를 저장하지 않고도 다음 질문에 답할 수 있도록 만든 비식별 상태 모델입니다.

- 대상별 PAC/WPAD 판정을 실제로 시도했는가?
- 수동 프록시 설정 읽기는 성공했는가?
- 어느 출처가 선택됐는가?
- 프록시 후보와 DIRECT는 몇 개인가?
- 왜 DNS·Windows 경로 조회가 허용되거나 차단됐는가?
- 분석이 계획·완료·취소·실패 중 어느 단계인가?

## 생성 흐름

```text
ProxyDirectiveSourceSnapshot
  ↓ 출처 선택 정책
ProxyDirectiveSourceSelectionResult
  ↓ 실행 계획 정책
ProxyDirectiveRouteAnalysisPlan
  ↓
ProxyDirectiveDecisionAudit
```

선택적으로 실제 실행 상태를 전달하면 계획 이후 단계도 기록합니다.

```csharp
ProxyDirectiveDecisionAuditFactory.Create(
    snapshot,
    ProxyDirectiveRouteAnalysisExecutionStatus.Completed);
```

## 감사 단계

```text
Planned
Completed
DirectOnly
Blocked
Unavailable
Canceled
Failed
```

### Planned

유효한 프록시 후보가 있고 사용자 실행 시에만 DNS·Windows 로컬 경로 분석을 시작할 수 있습니다.

### Completed

승인된 출처와 계획을 사용한 분석 실행이 완료됐습니다.

### DirectOnly

대상별 또는 수동 지시문에서 DIRECT-only가 확인됐습니다.

```text
NetworkLookupAllowed=false
```

프록시 엔드포인트가 없으므로 DNS·프록시 경로 조회를 수행하지 않습니다.

### Blocked

다음과 같은 fail-closed 상태입니다.

- 대상별 PAC/WPAD 판정을 시도했지만 실패
- 대상별 boolean과 지시문이 모순
- 수동 설정 읽기 실패
- 수동 DIRECT와 해석 불가 구간이 혼재
- 선택 결과와 실행 계획 내부 계약 불일치

유효한 다른 출처가 있어도 자동 fallback하지 않습니다.

### Unavailable

대상별 판정을 수행하지 않았고 사용할 수 있는 수동 프록시 설정도 없습니다. DIRECT로 추정하지 않습니다.

### Canceled

사용자 취소로 분석을 완료하지 않았습니다.

### Failed

분석 콜백 오류 또는 결과 없음으로 완료되지 않았습니다. 예외 원문과 프록시 지시문은 감사 스냅샷에 포함하지 않습니다.

## 필드

```text
capturedAt
targetDecisionReadStatus
manualConfigurationReadStatus
autoDetectEnabled
pacConfigured
manualProxyConfigured
selectionStatus
sourceKind
selectionCode
planStatus
planCode
phase
networkLookupAllowed
proxyEndpointCount
directDirectiveCount
parseErrorCount
parseWarningCount
hasDirectFallback
message
redactedDisplay
```

원문 문자열을 저장하는 필드는 없습니다.

## 읽기 상태 정규화

지원 상태:

```text
NotAttempted
Success
Failed
```

정의되지 않은 값은 감사 모델에서 `Failed`로 정규화합니다. 유효한 수동 프록시가 있더라도 알 수 없는 대상 판정 상태를 fallback 근거로 사용하지 않습니다.

## 상태와 네트워크 조회의 구분

실제 분석이 `Canceled` 또는 `Failed`여도 원래 실행 계획이 프록시 분석을 허용했다면 다음 값은 유지됩니다.

```text
NetworkLookupAllowed=true
```

이는 조회가 성공했다는 뜻이 아니라, 출처·계획 정책상 조회를 시작할 자격이 있었다는 뜻입니다.

반대로 다음 상태는 항상 조회를 허용하지 않습니다.

```text
DirectOnly
Blocked
Unavailable
```

## 부분 파싱

유효 프록시 후보와 해석 불가 구간이 함께 있으면 다음과 같이 기록합니다.

```text
Phase=Planned
SelectionStatus=SelectedWithWarnings
ParseErrorCount=1
ProxyEndpointCount=1
DirectDirectiveCount=1
```

오류 세그먼트 원문은 저장하지 않고 개수만 남깁니다. 후속 내부 DIRECT↔프록시 전체 비교는 일부 fallback이 제외됐으므로 완전 증거로 보지 않아야 합니다.

## 개인정보 경계

포함하지 않는 값:

- 대상 URL
- PAC URL과 PAC 원문
- 수동 프록시 원문
- 프록시 DNS 호스트와 IP
- 사용자 이름·암호
- 전체 인터페이스 GUID
- 인터페이스 이름·설명
- 분석 payload
- 예외 메시지

안전하게 포함하는 값:

- 강한 enum 상태와 고정 코드
- 프록시·DIRECT 개수
- 파싱 오류·경고 개수
- 설정 여부 boolean
- 네트워크 조회 허용 여부
- 고정된 설명

`RedactedDisplay` 예:

```text
Planned · TargetSpecificAutoProxy · TargetSpecificProxy · TargetSpecificProxySelected · 프록시 후보 1개 · DIRECT 1개 · 네트워크 조회 허용
```

## 통신 경계

감사 스냅샷 생성은 이미 메모리에 있는 상태 모델만 사용합니다.

수행하지 않는 작업:

- Windows 프록시 설정 조회
- PAC/WPAD 다운로드·실행
- DNS
- Windows 라우팅 API
- TCP·HTTP·HTTPS
- 프록시 인증
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·결과 업로드

## 자동 검증

Core SelfTest는 다음을 확인합니다.

1. 대상별 프록시의 `Planned`와 조회 허용
2. 대상별 판정 실패 + 유효 수동 프록시의 `Blocked`
3. 대상별 DIRECT의 `DirectOnly`와 조회 차단
4. 완료·취소·실패 실행 단계
5. 수동 부분 파싱의 오류 개수와 후보 수
6. 정의되지 않은 읽기 상태의 `Failed` 정규화와 차단
7. JSON·표시·메시지에서 대상별·수동 원문 비노출
