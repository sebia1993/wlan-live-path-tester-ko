# 프록시 지시문 출처 선택 V3

이 기능은 특정 외부 URL에 대한 대상별 PAC/WPAD 판정과 현재 사용자 수동 프록시 설정이 동시에 존재할 때 어떤 지시문을 후속 로컬 경로 분석에 전달할지 결정합니다.

프록시 서버에 연결하거나 PAC 파일을 내려받는 기능이 아니라, Windows reader가 이미 수집한 상태와 문자열을 메모리에서 비교하는 Core 정책입니다.

## 핵심 원칙

```text
대상별 PAC/WPAD 판정을 수행함
  → 대상별 결과가 최우선
  → Proxy, DIRECT, 실패를 그대로 유지
  → 수동 프록시로 자동 대체하지 않음

대상별 판정을 수행하지 않음
  → 수동 프록시 읽기가 성공한 경우에만 수동 설정 사용

둘 다 읽지 않음
  → Unavailable
  → DIRECT 또는 프록시를 추정하지 않음
```

대상별 판정을 **시도했지만 실패한 경우**와 **아예 시도하지 않은 경우**는 서로 다릅니다. 실패를 미시도로 취급하면 PAC/WPAD 오류가 유효한 수동 프록시 뒤에 숨겨질 수 있기 때문에 반드시 별도 상태로 유지합니다.

## 선택 상태

```text
Selected
SelectedWithWarnings
Direct
Unavailable
Invalid
```

### Selected

- 사용할 프록시 후보가 1개 이상 있음
- 파싱 오류 없음
- 선택된 원문은 현재 프로세스 메모리에만 유지

### SelectedWithWarnings

- 사용할 프록시 후보가 1개 이상 있음
- 일부 세그먼트가 잘못돼 제외됨
- 유효 후보는 사용자가 명시적으로 실행할 수 있음
- 전체 PAC fallback 경로 비교에는 불완전 근거로 전달해야 함

예:

```text
PROXY valid.example:8080; UNKNOWN invalid; DIRECT
```

### Direct

- 프록시 후보가 없음
- DIRECT 지시문 1개 이상
- 파싱 오류 없음

`DIRECT`는 프록시 엔드포인트가 아니므로 후속 프록시 DNS·Windows 경로 분석을 시작하지 않습니다.

### Unavailable

- 대상별 판정을 수행하지 않음
- 수동 프록시 설정도 읽지 않음 또는 설정되지 않음

이 상태를 `DIRECT`로 추정하지 않습니다.

### Invalid

다음과 같은 모순·읽기 실패·모호한 설정입니다.

- 대상별 boolean은 DIRECT인데 문자열에 프록시 후보가 있음
- 대상별 boolean은 프록시인데 문자열은 DIRECT-only
- 대상별 PAC/WPAD 판정을 시도했지만 결과를 얻지 못함
- 수동 프록시 설정 읽기 실패
- 수동 설정이 구성됐다고 표시됐지만 사용할 수 있는 지시문이 없음
- DIRECT와 해석할 수 없는 구간이 함께 있어 실제 프록시 후보 존재 여부가 불명확함
- 정의되지 않은 reader 상태 값

`Invalid`에서는 실행 가능한 원문을 반환하지 않습니다.

## 출처 종류

```text
None
TargetSpecificAutoProxy
ManualProxyConfiguration
```

오류가 발생해도 실제로 오류가 난 출처를 유지합니다. 대상별 판정 실패를 `ManualProxyConfiguration`으로 바꾸지 않습니다.

## 고정 판정 코드

```text
TargetSpecificProxy
TargetSpecificDirect
ManualProxy
ManualDirect
TargetDecisionInvalid
ManualConfigurationInvalid
NoAvailableDirective
```

UI와 보고서는 한국어 설명을 파싱하지 않고 이 코드를 기준으로 상태를 처리할 수 있습니다.

## 대상별 판정 예시

### 프록시 판정 성공

```text
targetDecisionWasEvaluated=true
targetDecisionIsDirect=false
targetSpecificDirective="PROXY target.example:8080; DIRECT"
manualProxyConfigured=true
manualProxyDirective="PROXY manual.example:3128"
```

결과:

```text
Status=Selected
SourceKind=TargetSpecificAutoProxy
Code=TargetSpecificProxy
ProxyEndpointCount=1
DirectDirectiveCount=1
```

수동 프록시 문자열은 선택하지 않습니다.

### DIRECT 판정 성공

```text
targetDecisionWasEvaluated=true
targetDecisionIsDirect=true
targetSpecificDirective=null
```

결과:

```text
Status=Direct
Code=TargetSpecificDirect
SelectedDirectiveText="DIRECT"
```

reader가 별도 DIRECT 문자열을 제공하지 않으면 canonical `DIRECT`를 현재 메모리에 생성합니다.

### 범위가 있는 DIRECT

```text
targetDecisionIsDirect=true
targetSpecificDirective="https=DIRECT"
```

범위 정보를 유지하고 일반 `DIRECT`로 축소하지 않습니다.

### 모순된 대상별 DIRECT

```text
targetDecisionIsDirect=true
targetSpecificDirective="PROXY proxy.example:8080; DIRECT"
```

결과:

```text
Status=Invalid
Code=TargetDecisionInvalid
SelectedDirectiveText=null
```

수동 프록시로 fallback하지 않습니다.

### 모순된 대상별 프록시

```text
targetDecisionIsDirect=false
targetSpecificDirective="DIRECT"
```

이 역시 `Invalid`입니다. boolean과 지시문 중 편리한 쪽을 임의 선택하지 않습니다.

## 수동 설정 예시

대상별 판정을 수행하지 않았을 때만 수동 프록시를 사용할 수 있습니다.

```text
http=manual-http.example:8080;
https=manual-connect.example:8080
```

결과:

```text
Status=Selected
SourceKind=ManualProxyConfiguration
Code=ManualProxy
ProxyEndpointCount=2
```

다음 scoped DIRECT는 범위를 유지합니다.

```text
ftp=DIRECT
```

결과:

```text
Status=Direct
Code=ManualDirect
SelectedDirectiveText="ftp=DIRECT"
```

다음 설정은 DIRECT-only로 축소하지 않습니다.

```text
DIRECT; UNKNOWN possibly-proxy.example:8080
```

결과:

```text
Status=Invalid
Code=ManualConfigurationInvalid
```

해석되지 않은 구간이 실제 프록시 후보일 가능성을 숨기지 않기 위한 fail-closed 처리입니다.

## reader 스냅샷

`ProxyDirectiveSourceSnapshot`은 Windows reader가 Core 정책에 전달하는 최소 계약입니다.

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
```

각 reader 상태는 다음 중 하나입니다.

```text
NotAttempted
Success
Failed
```

### 대상별 판정 Failed

```text
TargetDecisionStatus=Failed
ManualConfigurationStatus=Success
ManualProxyConfigured=true
```

수동 설정이 유효해도 결과는 다음과 같습니다.

```text
Status=Invalid
SourceKind=TargetSpecificAutoProxy
Code=TargetDecisionInvalid
```

### 대상별 판정 NotAttempted

```text
TargetDecisionStatus=NotAttempted
ManualConfigurationStatus=Success
```

이 경우에만 수동 설정 선택으로 이동합니다.

### 수동 설정 Failed

```text
TargetDecisionStatus=NotAttempted
ManualConfigurationStatus=Failed
```

결과:

```text
Status=Invalid
Code=ManualConfigurationInvalid
```

설정 읽기 실패를 “프록시 없음”이나 DIRECT로 바꾸지 않습니다.

## 개인정보 경계

다음 값은 후속 로컬 분석에 필요하므로 현재 프로세스 메모리에서만 유지합니다.

```text
SelectedDirectiveText
TargetSpecificDirective
ManualProxyDirective
ProxyRouteDirective.Host
```

모두 기본 JSON 직렬화 또는 안전 표시에서 제외됩니다.

`ToString()`과 DebuggerDisplay에는 다음만 나타납니다.

```text
상태
출처
고정 코드
프록시 후보 수
DIRECT 수
파싱 오류 존재 여부
자동 검색·PAC 설정 플래그
```

표시 예:

```text
Selected · TargetSpecificAutoProxy · TargetSpecificProxy · 프록시 후보 1개 · DIRECT 1개 · 파싱 오류 없음
```

```text
대상 판정 Success · 수동 설정 Success · 수동 프록시 있음 · 자동 검색 사용 · PAC 설정
```

프록시 호스트, PAC URL, 수동 프록시 문자열과 자격 증명은 표시하지 않습니다.

## 통신 경계

이 정책과 스냅샷은 다음만 수행합니다.

- boolean·enum 상태 검사
- 로컬 문자열 파싱
- 후보·DIRECT 개수 집계
- 안전한 고정 메시지 생성

다음 작업은 수행하지 않습니다.

- Windows 프록시 설정 조회
- PAC/WPAD 다운로드 또는 실행
- DNS 조회
- Windows 라우팅 API
- TCP·HTTP·HTTPS 요청
- 프록시 인증
- 프록시 관리 API
- 외부 분석 API
- AI 또는 로컬 AI
- 텔레메트리
- 자동 업데이트
- 결과 업로드

실제 reader와 DNS·Windows 경로 분석은 별도의 Windows 계층에서 사용자가 명시적으로 실행한 경우에만 동작합니다.

## 자동 검증

Core SelfTest는 다음을 확인합니다.

1. 대상별 프록시가 유효한 수동 프록시보다 우선
2. 대상별 DIRECT가 수동 프록시를 무시
3. DIRECT boolean과 프록시 문자열 모순 차단
4. 프록시 boolean과 DIRECT-only 문자열 모순 차단
5. 잘못된 대상별 판정의 수동 fallback 금지
6. 대상별 판정 미시도에서만 수동 설정 사용
7. `ftp=DIRECT` 범위 보존
8. DIRECT와 해석 불가 구간 혼재 차단
9. 일부 유효 프록시의 `SelectedWithWarnings`
10. 대상별 판정 Failed와 NotAttempted 구분
11. 수동 설정 읽기 Failed의 Invalid 처리
12. 출처 없음의 Unavailable 처리
13. 정의되지 않은 reader 상태의 fail-closed 처리
14. 스냅샷·선택 결과 JSON과 `ToString()`의 원문 비노출
