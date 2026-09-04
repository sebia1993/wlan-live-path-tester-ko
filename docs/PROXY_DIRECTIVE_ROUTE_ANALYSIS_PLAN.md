# 프록시 지시문 경로 분석 실행 계획

프록시 출처 선택 결과를 곧바로 DNS·Windows 경로 분석기에 전달하지 않습니다. `ProxyDirectiveRouteAnalysisPlanPolicy`가 선택 상태와 파싱 결과의 내부 계약을 다시 검사하고, 네트워크 조회 가능 여부를 명시적인 실행 계획으로 변환합니다.

## 계획 상태

```text
AnalyzeProxyEndpoints
DirectOnly
Blocked
Unavailable
```

### AnalyzeProxyEndpoints

다음 조건을 모두 충족해야 합니다.

- 출처 선택 상태가 `Selected` 또는 `SelectedWithWarnings`
- 메모리에 선택된 지시문 원문이 있음
- 파싱 결과가 있음
- 비-DIRECT 프록시 후보가 1개 이상 있음

이 상태에서만 `NetworkLookupAllowed=true`입니다.

실제 DNS·Windows 최적 경로 분석은 사용자가 명시적으로 실행한 뒤에만 시작합니다. 계획 생성 자체는 네트워크 요청을 수행하지 않습니다.

### DirectOnly

다음 조건을 모두 충족합니다.

- 선택 상태 `Direct`
- 선택 지시문 원문과 파싱 결과가 있음
- 프록시 후보 없음
- DIRECT 지시문 1개 이상
- 파싱 Error 없음

결과:

```text
NetworkLookupAllowed=false
```

프록시 엔드포인트가 없으므로 DNS나 프록시 호스트 경로를 조회하지 않습니다. `ftp=DIRECT` 같은 수동 범위 정보는 메모리에서 유지합니다.

### Blocked

다음 경우입니다.

- 출처 선택 상태 `Invalid`
- 선택 상태·원문·파싱 결과가 서로 모순
- `Selected`인데 프록시 후보가 없음
- `Direct`인데 프록시 후보 또는 파싱 오류가 있음

`DirectiveText`를 계획에 남기지 않고 네트워크 조회를 차단합니다.

### Unavailable

대상별 PAC/WPAD 판정을 수행하지 않았고 수동 프록시 설정도 없는 상태입니다.

- DIRECT로 추정하지 않음
- 프록시 후보를 추정하지 않음
- 네트워크 조회하지 않음

`Blocked`는 입력이 존재하지만 유효하지 않은 상태이고, `Unavailable`은 사용할 출처 자체가 없는 상태입니다.

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

고정 코드를 사용해 UI·로그·보고서가 한국어 메시지를 파싱하지 않고 상태를 구분할 수 있습니다.

## 부분 파싱

`SelectedWithWarnings`에 유효한 프록시 후보가 있으면 `AnalyzeProxyEndpoints` 계획을 만들 수 있습니다.

다만 계획에 다음을 유지합니다.

```text
HasParseErrors=true
```

후속 프록시 경로 분석은 유효 후보만 확인할 수 있지만, 내부 DIRECT↔프록시 전체 비교는 PAC fallback 일부가 제외됐으므로 `Incomplete`로 처리해야 합니다.

## 이중 방어

출처 선택 정책이 정상 결과를 만들었더라도 실행 계획에서 다음을 다시 확인합니다.

- 원문 존재
- 파싱 결과 존재
- 프록시 후보 또는 DIRECT의 상태 일치
- 파싱 오류와 DIRECT-only 상태의 일관성

이는 향후 직렬화·코드 변경·잘못된 어댑터 구현으로 내부 계약이 깨진 경우 DNS 분석이 시작되는 것을 방지합니다.

## 개인정보 경계

`DirectiveText`는 후속 분석에 필요한 메모리 전용 값이며 기본 JSON에서 제외합니다.

`ToString()`과 DebuggerDisplay에는 다음만 표시합니다.

- 계획 상태
- 고정 코드
- 출처
- 프록시 후보 수
- DIRECT 수
- 파싱 오류 존재 여부

원문 호스트·PAC 문자열·수동 프록시 문자열은 표시하지 않습니다.

## 통신 경계

실행 계획 정책은 이미 생성된 선택 결과만 검사합니다.

다음 작업을 수행하지 않습니다.

- PAC/WPAD 다운로드 또는 실행
- Windows 프록시 설정 조회
- DNS 조회
- Windows 라우팅 API
- TCP·HTTP·HTTPS
- 프록시 인증
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·결과 업로드

## 자동 검증

Core SelfTest는 다음을 확인합니다.

- 대상별 프록시의 `AnalyzeProxyEndpoints`
- 부분 파싱 프록시의 분석 허용과 `HasParseErrors`
- 대상별·수동 DIRECT의 `DirectOnly`와 네트워크 조회 금지
- `Invalid`의 `Blocked`
- 출처 없음의 `Unavailable`
- 실행 계획 기본 JSON·표시에서 원문 지시문과 호스트 비노출
