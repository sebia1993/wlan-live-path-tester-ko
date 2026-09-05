# 프록시 지시문 출처–실행 단일 파이프라인

`ProxyDirectiveSourceExecutionPipeline`은 Windows 프록시 reader가 만든 `ProxyDirectiveSourceSnapshot`에서 실제 경로 분석 콜백 실행까지의 정책을 한 경로로 고정합니다.

호출자가 다음 단계를 임의로 건너뛰지 않게 하는 것이 목적입니다.

```text
ProxyDirectiveSourceSnapshot
  ↓
ProxyDirectiveSourceSnapshotSelectionPolicy
  ↓
ProxyDirectiveRouteAnalysisPlanPolicy
  ↓
ProxyDirectiveRouteAnalysisExecutor
  ↓ 승인된 경우에만
사용자 실행 프록시 엔드포인트 분석 콜백
```

## 공개 API

```csharp
Task<ProxyDirectiveRouteAnalysisExecutionResult<TAnalysis>> ExecuteAsync<TAnalysis>(
    ProxyDirectiveSourceSnapshot snapshot,
    Func<string, CancellationToken, Task<TAnalysis>> analyzer,
    CancellationToken cancellationToken = default)
```

입력:

- Windows reader가 수집한 출처 스냅샷
- 선택된 지시문을 받아 실제 로컬 분석을 수행하는 콜백
- 사용자 취소 토큰

## 실행 규칙

### 대상별 프록시 판정 성공

```text
TargetDecisionStatus=Success
TargetDecisionIsDirect=false
유효 프록시 후보 있음
```

- 대상별 지시문이 수동 프록시보다 우선
- 승인된 원문을 분석 콜백에 전달
- 콜백 최대 1회

### 대상별 PAC/WPAD 판정 실패

```text
TargetDecisionStatus=Failed
수동 프록시가 유효함
```

결과:

```text
Blocked
callback count=0
```

판정을 실제로 시도하고 실패한 상태를 `NotAttempted`로 바꾸지 않습니다. 유효한 수동 프록시가 있어도 자동 fallback하지 않습니다.

### 대상별 DIRECT

```text
TargetDecisionStatus=Success
TargetDecisionIsDirect=true
```

결과:

```text
DirectOnly
callback count=0
```

수동 프록시가 설정돼 있어도 해당 URL의 대상별 DIRECT 판정을 덮어쓰지 않습니다.

### 대상별 판정 미시도와 수동 프록시 성공

```text
TargetDecisionStatus=NotAttempted
ManualConfigurationStatus=Success
ManualProxyConfigured=true
```

- 수동 프록시를 선택
- 분석 콜백 최대 1회

### 수동 설정 읽기 실패

```text
TargetDecisionStatus=NotAttempted
ManualConfigurationStatus=Failed
```

결과:

```text
Blocked
callback count=0
```

읽기 실패를 설정 없음이나 DIRECT로 추정하지 않습니다.

### 사전 취소

분석 가능한 프록시 계획이더라도 `CancellationToken`이 이미 취소됐으면 콜백을 호출하지 않습니다.

```text
Canceled
callback count=0
```

## 원문 전달 범위

선택된 지시문 원문은 승인된 프록시 계획에서만 콜백에 전달합니다.

전달하지 않는 경우:

- `DirectOnly`
- `Blocked`
- `Unavailable`
- 사전 취소
- 출처 상태가 서로 모순됨
- 선택 상태·파싱 결과·원문 계약 불일치

선택되지 않은 수동 또는 대상별 지시문은 결과 객체에 복사하지 않습니다.

## 개인정보 경계

다음 값은 현재 프로세스 메모리에서만 사용합니다.

- 대상별 프록시 원문
- 수동 프록시 원문
- 분석 콜백의 실제 결과 payload

기본 JSON과 `ToString()`에는 포함하지 않습니다.

안전하게 남는 값:

```text
실행 상태
계획 상태와 고정 코드
선택 출처
프록시 후보 수
DIRECT 수
파싱 오류 존재 여부
고정 메시지
```

프록시 호스트는 파싱 결과에서도 SHA-256 앞 10자의 지문만 기본 직렬화됩니다.

## 통신 경계

단일 파이프라인이 직접 수행하는 작업:

- 출처 상태 선택
- 실행 계획 검증
- 취소 확인
- 승인된 콜백 최대 1회 호출

직접 수행하지 않는 작업:

- Windows 프록시 설정 조회
- PAC/WPAD 다운로드 또는 실행
- DNS
- Windows 라우팅 API
- TCP·HTTP·HTTPS
- 프록시 인증
- 프록시 관리 API
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·결과 업로드

실제 DNS와 Windows 최적 인터페이스 확인은 승인된 계획에서 사용자가 실행한 콜백 구현에만 존재합니다.

## 자동 검증

Core SelfTest는 다음 end-to-end 경계를 확인합니다.

1. 대상별 프록시 성공에서 콜백 정확히 1회
2. 수동 프록시가 함께 있어도 대상별 원문만 전달
3. 대상별 판정 실패 + 유효 수동 프록시에서 콜백 0회
4. 대상별 DIRECT에서 콜백 0회
5. 대상별 판정 미시도 + 수동 프록시 성공에서 콜백 1회
6. 수동 설정 읽기 실패에서 콜백 0회
7. 사전 취소에서 콜백 0회
8. 실행 결과 JSON·표시에 두 출처 원문과 분석 payload 비노출
