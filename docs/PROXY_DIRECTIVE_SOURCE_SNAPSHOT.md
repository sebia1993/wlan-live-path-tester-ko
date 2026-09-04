# Windows 프록시 reader용 지시문 출처 스냅샷

`ProxyDirectiveSourceSnapshot`은 향후 Windows 프록시 설정·PAC/WPAD 판정 reader가 Core 선택 정책에 전달해야 하는 최소 계약입니다. 원문 프록시 문자열은 메모리에서만 유지하고, 읽기 시도 여부와 성공·실패 상태를 구조화합니다.

## 읽기 상태

```text
NotAttempted
Success
Failed
```

대상별 판정과 수동 설정 읽기에 각각 독립적으로 사용합니다.

### NotAttempted

해당 출처를 조회하지 않았습니다.

예:

- 자동 검색과 PAC 설정이 모두 꺼져 대상별 판정을 실행하지 않음
- 수동 설정 reader를 아직 호출하지 않음

### Success

reader 호출이 정상 완료됐습니다.

- 대상별 결과가 DIRECT인지
- 대상별 프록시 지시문
- 수동 프록시 설정 여부와 문자열

을 함께 전달합니다.

### Failed

reader를 실제로 호출했지만 결과를 얻지 못했습니다.

- PAC 다운로드 실패
- WPAD 탐색 실패
- WinHTTP 판정 오류
- 현재 사용자 수동 프록시 설정 읽기 오류

`Failed`는 `NotAttempted`와 다릅니다. 특히 대상별 PAC/WPAD 판정을 시도했지만 실패한 경우 유효한 수동 프록시가 있어도 자동 fallback하지 않습니다.

## 스냅샷 필드

```text
capturedAt
targetDecisionStatus
targetDecisionIsDirect
targetSpecificDirective
manualConfigurationStatus
manualProxyConfigured
manualProxyDirective
autoDetectEnabled
pacConfigured
redactedDisplay
```

다음 두 원문은 `[JsonIgnore]`입니다.

```text
targetSpecificDirective
manualProxyDirective
```

후속 로컬 프록시 경로 분석에 필요하므로 현재 프로세스 메모리에서만 유지합니다.

## 선택 변환

`ProxyDirectiveSourceSnapshotSelectionPolicy`가 스냅샷을 기존 출처 선택 정책으로 변환합니다.

### 대상별 판정 성공

```text
TargetDecisionStatus=Success
  → 대상별 boolean·지시문 사용
  → 수동 설정보다 우선
```

### 대상별 판정 실패

```text
TargetDecisionStatus=Failed
  → TargetDecisionInvalid
  → SelectedDirectiveText=null
  → 수동 프록시로 fallback하지 않음
```

실패를 “대상별 판정을 하지 않음”으로 바꾸면 수동 설정이 잘못 선택될 수 있으므로 반드시 구분합니다.

### 대상별 판정 미시도

```text
TargetDecisionStatus=NotAttempted
  → ManualConfigurationStatus 확인
```

### 수동 설정 성공

```text
ManualConfigurationStatus=Success
  → manualProxyConfigured와 원문을 기존 선택 정책에 전달
```

### 수동 설정 실패

```text
ManualConfigurationStatus=Failed
  → ManualConfigurationInvalid
  → DIRECT로 추정하지 않음
```

### 둘 다 미시도

```text
NoAvailableDirective
Status=Unavailable
```

## 잘못된 enum 값

손상된 직렬화·테스트 어댑터·향후 코드 오류로 정의되지 않은 읽기 상태가 전달되면 fail-closed로 처리합니다.

- 알 수 없는 대상 읽기 상태 → `TargetDecisionInvalid`
- 알 수 없는 수동 읽기 상태 → `ManualConfigurationInvalid`

유효한 다른 출처로 fallback하지 않습니다.

## 개인정보 경계

기본 JSON과 `ToString()`에는 다음만 표시합니다.

- 캡처 시각
- 대상 판정 읽기 상태
- 수동 설정 읽기 상태
- 수동 프록시 설정 여부
- 자동 검색 사용 여부
- PAC 설정 여부
- 마스킹된 요약

표시 예:

```text
대상 판정 Success · 수동 설정 Success · 수동 프록시 있음 · 자동 검색 사용 · PAC 설정
```

프록시 호스트, PAC URL, 수동 프록시 문자열과 사용자 자격 증명은 포함하지 않습니다.

## 통신 경계

스냅샷 클래스와 선택 정책은 이미 reader가 반환한 메모리 값만 처리합니다.

다음 작업은 직접 수행하지 않습니다.

- Windows 프록시 설정 조회
- PAC/WPAD 다운로드·실행
- DNS
- TCP·HTTP·HTTPS
- 프록시 인증
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·결과 업로드

실제 Windows reader는 별도 Windows 계층에서 구현하고, 사용자 실행·제한 시간·취소·메모리 해제 규칙을 가져야 합니다.

## 자동 검증

Core SelfTest는 다음을 확인합니다.

- 성공한 대상별 판정 선택
- 대상별 판정 실패가 유효한 수동 프록시로 fallback하지 않음
- 대상별 판정 미시도에서만 수동 설정 사용
- 수동 설정 읽기 실패의 Invalid 처리
- 둘 다 미시도의 Unavailable 처리
- 원문 대상별·수동 프록시 문자열의 JSON·ToString 비노출
- 정의되지 않은 읽기 상태의 fail-closed 처리
