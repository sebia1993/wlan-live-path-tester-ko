# 대상별 PAC/WPAD와 수동 프록시 지시문 선택

Windows 환경에는 동시에 여러 프록시 정보가 존재할 수 있습니다.

- 특정 외부 URL에 대한 PAC/WPAD 자동 프록시 판정
- 현재 사용자 수동 프록시 설정
- 프로토콜별 수동 프록시 문자열
- `DIRECT` 또는 프록시 fallback 목록

이 기능은 어떤 지시문을 프록시 엔드포인트 경로 분석에 전달할지 결정합니다. 대상별 판정과 수동 설정이 모순될 때 임의 fallback을 하지 않는 것이 핵심입니다.

## 우선순위

```text
대상별 PAC/WPAD 판정이 실제로 수행됨
  → 대상별 판정이 최우선
  → 성공·DIRECT·오류를 그대로 유지
  → 수동 프록시로 자동 대체하지 않음

대상별 판정을 수행하지 않음
  → 수동 프록시가 설정된 경우에만 수동 문자열 사용

둘 다 없음
  → Unavailable
  → DIRECT로 추정하지 않음
```

수동 프록시는 대상별 판정이 없을 때 사용하는 설정 근거입니다. PAC/WPAD가 특정 URL을 `DIRECT`로 판정했거나 오류가 발생한 상태를 수동 프록시로 덮어쓰지 않습니다.

## 선택 상태

```text
Selected
SelectedWithWarnings
Direct
Unavailable
Invalid
```

### Selected

유효한 프록시 후보가 있고 모든 구간을 안전하게 해석했습니다.

### SelectedWithWarnings

유효한 프록시 후보가 있지만 일부 구간을 해석하지 못했습니다.

예:

```text
PROXY valid.example:8080; UNKNOWN invalid; DIRECT
```

유효한 프록시와 DIRECT 순서는 유지하지만 제외된 구간이 있다는 사실을 함께 전달합니다. 후속 경로 비교는 전체 fallback 증거가 불완전하므로 `Incomplete`로 판단할 수 있습니다.

### Direct

대상별 판정 또는 수동 문자열에서 `DIRECT`만 확인했습니다.

`DIRECT`는 프록시 엔드포인트가 아니므로 후속 프록시 경로 분석에서 DNS 또는 프록시 호스트 조회를 하지 않습니다.

### Unavailable

대상별 판정을 수행하지 않았고 수동 프록시도 설정되지 않았습니다.

```text
NoAvailableDirective
```

이 상태를 `DIRECT`로 추정하지 않습니다.

### Invalid

선택한 출처의 정보가 모순되거나 안전하게 해석되지 않았습니다.

- 대상별 boolean 판정은 DIRECT인데 문자열에 프록시 후보가 있음
- 대상별 boolean 판정은 프록시인데 문자열은 DIRECT-only
- 대상별 지시문이 비어 있거나 포트·호스트 형식이 잘못됨
- 수동 프록시가 설정됐다고 표시됐지만 사용할 수 있는 지시문이 없음

`Invalid`에서는 실행 가능한 원문을 반환하지 않습니다.

## 대상별 판정은 권위 있는 입력

### 대상별 프록시

```text
targetDecisionWasEvaluated=true
targetDecisionIsDirect=false
targetSpecificDirective="PROXY target.example:8080; DIRECT"
manualProxyConfigured=true
manualProxyDirective="PROXY manual.example:3128"
```

결과:

```text
SourceKind=TargetSpecificAutoProxy
Code=TargetSpecificProxy
SelectedDirectiveText=대상별 문자열
```

수동 프록시 호스트는 선택 결과에 사용하지 않습니다.

### 대상별 DIRECT

```text
targetDecisionWasEvaluated=true
targetDecisionIsDirect=true
targetSpecificDirective=null
manualProxyConfigured=true
```

결과:

```text
Status=Direct
SourceKind=TargetSpecificAutoProxy
Code=TargetSpecificDirect
SelectedDirectiveText="DIRECT"
```

수동 프록시가 설정돼 있어도 해당 URL의 대상별 DIRECT 판정을 우선합니다.

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

프록시 후보가 섞인 문자열을 DIRECT로 축소하지 않고 수동 프록시로도 대체하지 않습니다.

### 모순된 대상별 프록시

```text
targetDecisionIsDirect=false
targetSpecificDirective="DIRECT"
```

이 역시 `Invalid`입니다. boolean 결과와 지시문이 서로 다르므로 어느 쪽도 임의로 선택하지 않습니다.

## 수동 프록시

대상별 판정을 수행하지 않은 경우에만 수동 프록시 설정을 사용합니다.

```text
http=manual-http.example:8080;
https=manual-connect.example:8080
```

프로토콜별 범위를 그대로 유지합니다.

수동 문자열이 scoped DIRECT-only인 경우에도 원문 범위를 보존합니다.

```text
ftp=DIRECT
```

이를 단순 `DIRECT`로 축소하지 않으므로 후속 분석에서 적용 범위를 확인할 수 있습니다.

## fail-closed 원칙

다음 동작은 하지 않습니다.

- 대상별 PAC/WPAD 오류를 수동 프록시로 자동 대체
- 대상별 프록시 오류를 `DIRECT`로 추정
- 설정 없음 상태를 `DIRECT`로 추정
- boolean 판정과 문자열이 모순될 때 편리한 쪽을 선택
- 잘못된 세그먼트를 임의 host:port로 보정

자동 fallback은 실제 네트워크 경로를 잘못 설명할 수 있기 때문입니다.

## 개인정보 경계

`SelectedDirectiveText`는 후속 로컬 경로 분석에 필요하므로 현재 프로세스 메모리에서만 유지합니다.

- 기본 JSON 직렬화에서 제외
- `ToString()`과 DebuggerDisplay에 원문 미포함
- 표시에는 상태·출처·고정 코드·프록시 후보 수·DIRECT 수만 사용
- 파싱 결과의 실제 호스트도 기본 JSON에서 제외
- 선택되지 않은 수동 또는 대상별 문자열을 결과 객체에 복사하지 않음

표시 예:

```text
Selected · TargetSpecificAutoProxy · TargetSpecificProxy · 프록시 후보 1개 · DIRECT 1개
```

## 통신 경계

선택 정책은 이미 수집된 boolean·문자열과 로컬 파서만 사용합니다.

다음 작업은 수행하지 않습니다.

- PAC/WPAD 다운로드 또는 실행
- Windows 프록시 설정 재조회
- DNS 조회
- TCP 연결
- HTTP/HTTPS 요청
- 프록시 인증
- 프록시 관리 API
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

실제 대상별 PAC/WPAD 판정 수집과 프록시 엔드포인트 DNS·경로 조회는 별도의 사용자 실행 단계입니다.

## 자동 검증

Core SelfTest는 다음을 확인합니다.

- 대상별 프록시가 유효한 수동 프록시보다 우선
- 대상별 DIRECT가 수동 프록시를 무시
- DIRECT boolean과 프록시 문자열 모순 차단
- 프록시 boolean과 DIRECT-only 문자열 모순 차단
- 잘못된 대상별 판정이 수동 프록시로 fallback하지 않음
- 대상별 판정이 없을 때만 수동 프록시 선택
- `ftp=DIRECT` 범위 보존
- 부분 파싱의 `SelectedWithWarnings`
- 출처 없음 상태의 `Unavailable`과 DIRECT 미추정
- `ToString()`과 기본 JSON에서 선택·미선택 프록시 원문 비노출
