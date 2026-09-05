# 내부 DIRECT–프록시 경로 비교 코디네이터

`InternalProxyRouteComparisonCoordinator`는 기존 프록시 문자열 파서, Windows 로컬 경로 판정기와 내부–프록시 비교기를 하나의 사용자 실행 흐름으로 조합합니다.

새로운 프록시 파서나 라우팅 알고리즘을 중복 구현하지 않습니다.

```text
ProxyEndpointParser
  ↓
내부 DIRECT LocalRouteEvidenceReader
  ↓
ProxyEndpointRouteAnalyzer
  ↓
InternalProxyRouteComparison
  ↓
InternalProxyRouteComparisonRunResult
```

## 실행 순서

1. DNS 제한 시간과 입력 길이·형식을 확인합니다.
2. 프록시 문자열을 현재 외부 HTTP·HTTPS URL 기준으로 먼저 해석합니다.
3. 프록시 입력이 유효하지 않으면 어떤 DNS·라우팅 조회도 시작하지 않습니다.
4. `DIRECT`가 첫 적용 경로이면 내부 대상과 프록시 후보 조회를 모두 생략합니다.
5. 취소가 이미 요청된 경우 reader를 호출하지 않습니다.
6. 내부 DIRECT 대상의 로컬 경로를 확인합니다.
7. 내부 경로가 비교에 사용할 수 없는 실패 상태면 프록시 후보 조회를 시작하지 않습니다.
8. 적용 가능한 프록시 후보의 로컬 경로를 확인합니다.
9. 내부와 프록시의 안전한 인터페이스 근거를 비교합니다.
10. 원본 근거는 같은 프로세스의 후속 보고서용으로 메모리에만 유지합니다.

## 실행 상태

```text
InvalidInput
DirectPathSelected
InternalRouteUnavailable
Completed
Canceled
Failed
```

### `InvalidInput`

다음 조건에서 반환합니다.

- 내부 DIRECT 기준 대상이 비어 있음
- 내부 대상이 2,048자를 초과하거나 제어 문자를 포함함
- 프록시 지시문이 비어 있음
- 외부 대상이 절대 HTTP·HTTPS URL이 아님
- 현재 외부 URL에 적용 가능한 프록시 또는 DIRECT 경로를 안전하게 결정하지 못함

이 상태에서는 다음 호출이 모두 0회입니다.

```text
내부 DNS·라우팅 reader
프록시 endpoint route analyzer
```

### `DirectPathSelected`

프록시 파서의 결정이 다음 중 하나인 경우입니다.

```text
Direct
DirectWithProxyAlternatives
```

`DIRECT`가 실제 적용 순서에서 먼저이므로 뒤 프록시 후보를 기본 경로로 추정하지 않습니다.

```text
InternalRouteReadPerformed=false
ProxyRouteAnalysisPerformed=false
```

비교할 프록시 엔드포인트가 없으므로 내부 대상 DNS도 불필요하게 조회하지 않습니다.

### `InternalRouteUnavailable`

내부 DIRECT 대상의 경로 상태가 다음처럼 비교에 사용할 수 없는 경우입니다.

```text
ResolutionFailed
RouteNotFound
Failed
지원되지 않는 상태
```

내부 기준 경로가 없으면 프록시 후보를 추가로 조회해도 내부–프록시 비교를 완료할 수 없으므로 프록시 분석을 시작하지 않습니다.

다음 내부 상태는 비교 엔진에 전달할 수 있습니다.

```text
Success
PartialSuccess
MultipleInterfaces
```

`PartialSuccess`와 `MultipleInterfaces`의 최종 의미는 기존 `InternalProxyRouteComparison`이 각각 경고 또는 `Ambiguous`로 판단합니다.

### `Completed`

내부 경로와 프록시 경로 분석을 모두 실행하고 기존 비교 엔진의 결과를 만들었습니다.

`Completed`는 비교 작업이 끝났다는 뜻입니다. 내부 비교 결과는 별도로 다음 상태를 가집니다.

```text
Ready
Incomplete
Ambiguous
Diverged
```

### `Canceled`

다음 시점의 취소를 같은 상태로 정리합니다.

- 첫 reader 호출 전 사전 취소
- 내부 경로 reader의 취소
- 내부 경로 완료 후 프록시 분석 전 취소
- 프록시 경로 분석 중 취소

취소 뒤 다음 단계의 DNS·라우팅 호출을 시작하지 않습니다.

### `Failed`

주입된 내부 reader 또는 프록시 분석 서비스가 일반 예외를 발생시킨 경우입니다.

결과 메시지에는 다음을 반사하지 않습니다.

- 내부 URL·호스트·IP
- 프록시 호스트와 지시문
- 예외 메시지
- 자격증명 또는 토큰

## 프록시 입력 우선 검증

내부 대상보다 프록시 문자열을 먼저 해석합니다.

예를 들어 HTTPS 외부 대상에 다음 수동 매핑만 있는 경우:

```text
http=proxy-http.example:8080
```

HTTPS에 적용되는 경로가 없으므로 `InvalidInput`이며 내부 DNS도 조회하지 않습니다.

이 순서는 잘못된 프록시 입력 때문에 불필요한 사내 DNS 요청이 발생하는 것을 막습니다.

## `DIRECT` 우선 경계

```text
DIRECT; PROXY later.example:8080
```

결과:

```text
ProxyDecision=DirectWithProxyAlternatives
Status=DirectPathSelected
내부 reader 호출=0
프록시 analyzer 호출=0
```

`DIRECT` 뒤 후보를 조회하거나 프록시 기본 경로로 표시하지 않습니다.

반대로 다음 입력은 프록시 후보가 먼저입니다.

```text
PROXY first.example:8080; DIRECT
```

내부 기준 경로를 확인한 뒤 `DIRECT` 앞의 실제 프록시 후보만 기존 분석기가 처리합니다.

## 개인정보 경계

공개 가능한 `InternalProxyRouteComparisonRunResult`에는 다음만 저장합니다.

- 실행 상태
- 프록시 출처 종류와 결정
- 대상 스킴
- 내부·프록시 구조화 상태
- 기존 안전 비교 결과
- 프록시 후보 개수
- DIRECT·fallback 여부
- 현재 WLAN GUID의 유효성 여부
- 각 단계 실행 여부
- 고정 메시지와 한계

다음 원본 객체는 `[JsonIgnore]`입니다.

```text
InternalRouteEvidence
ProxyRouteAnalysis
```

원본 내부 경로에는 인터페이스 GUID·이름·설명과 주소별 근거가 있을 수 있으므로 기본 JSON에 포함하지 않습니다.

프록시 파서 결과도 실행 결과에 저장하지 않습니다. 파서 후보의 실제 호스트는 DNS 판정에만 사용되고 공개 결과에는 복사되지 않습니다.

## 정확한 WLAN 상태

`ExpectedWlanIdentityAvailable`은 제공된 값이 실제 GUID로 해석되는지만 나타냅니다.

- 전체 GUID는 결과에 저장하지 않음
- 중괄호와 대소문자 차이는 허용
- 잘못된 값은 `false`
- WLAN GUID가 없어도 기존 비교 엔진이 안전한 지문 기준으로 가능한 범위만 판정

## 통신 경계

코디네이터가 직접 만드는 외부 HTTP·HTTPS 요청은 없습니다.

사용자가 실행하고 프록시 입력이 유효한 경우에만 기존 reader가 다음을 수행할 수 있습니다.

- 내부 호스트의 운영체제 DNS 조회
- 프록시 호스트의 운영체제 DNS 조회
- Windows 최적 로컬 인터페이스 판정

수행하지 않는 작업:

- HTTP `HEAD`·`GET`
- 프록시 TCP 연결
- `CONNECT`
- 프록시 인증
- PAC·WPAD 다운로드
- 프록시 서버 API 또는 내부 상태 조회
- AI·로컬 AI·외부 분석 API
- 텔레메트리·자동 오류 전송
- 결과 업로드

## 주입식 자동 검증

Windows Smoke는 실제 DNS나 프록시 없이 다음을 확인합니다.

1. HTTPS에 적용되지 않는 프록시 입력에서 reader 호출 0회
2. `DIRECT` 우선에서 내부·프록시 호출 0회
3. 내부 경로를 먼저 읽고 프록시 분석을 두 번째로 실행
4. 같은 인터페이스 근거의 `Ready`
5. 프록시 후보 파싱·분석·성공 개수 유지
6. 프록시 뒤 `DIRECT` fallback 보존
7. 내부 경로 실패 후 프록시 호출 0회
8. 사전 취소에서 모든 reader 호출 0회
9. reader 예외의 입력·예외 원문 비반사
10. 기본 JSON에서 원본 경로 객체·URL·호스트·전체 GUID·인터페이스 이름 비노출
11. 원본 근거가 같은 프로세스 메모리에서는 후속 보고서용으로 유지됨
12. 잘못된 DNS 제한 시간에서 reader 호출 0회

## 후속 연결

이 결과는 다음 계층의 공통 입력으로 사용할 수 있습니다.

- WPF 사용자 실행 화면
- 전용 JSON·CSV·HTML 보고서
- 통합 `LocalDiagnosticReport` optional 섹션
- 비교 Finding pipeline

후속 UI는 이 코디네이터 하나만 호출하고 내부 reader·파서·프록시 분석기를 개별적으로 다시 조합하지 않아야 합니다.
