# 통합 로컬 진단 보고서의 경로 비교 선택 확장

`LocalDiagnosticReportRouteComparisonWriter`는 기존 `LocalDiagnosticReport` 모델과 `LocalReportWriter`를 수정하지 않고 내부 DIRECT–프록시 로컬 경로 비교를 선택적으로 추가합니다.

기존 positional record 생성자, deconstruction, JSON·CSV·HTML 호출 코드를 깨지 않는 호환 계층입니다.

## 호환 동작

경로 비교 결과가 없는 경우:

```text
RenderJson(report, null) == LocalReportWriter.RenderJson(report)
RenderCsv(report, null)  == LocalReportWriter.RenderCsv(report)
RenderHtml(report, null) == LocalReportWriter.RenderHtml(report)
```

기존 출력 문자열을 바이트 단위로 그대로 반환합니다.

경로 비교 결과가 있는 경우에만 선택 섹션을 추가합니다.

## JSON

기존 최상위 JSON 객체에 다음 속성을 추가합니다.

```json
{
  "internalProxyRouteComparison": {
    "schemaVersion": "1.0",
    "completedAt": "...",
    "runStatus": "Completed",
    "proxySourceKind": "AutoProxyResult",
    "proxyDecision": "ProxyWithDirectFallback",
    "targetScheme": "https",
    "internalRouteStatus": "Success",
    "proxyRouteStatus": "Success",
    "comparisonStatus": "Diverged",
    "sameLocalInterface": false,
    "internalInterface": {
      "interfaceFingerprint": "0123456789",
      "category": "Wireless"
    },
    "proxyInterface": {
      "interfaceFingerprint": "abcdef0123",
      "category": "Tunnel"
    },
    "finding": {
      "code": "INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED"
    }
  }
}
```

실제 스냅샷에는 후보 개수, 단계 수행 여부, DIRECT·fallback, 부분 근거, VPN·터널·가상 NIC 상태도 포함됩니다.

## 최상위 Finding

안전 스냅샷의 고정 Finding을 기존 최상위 `findings` 배열에도 추가합니다.

다음 조건으로 중복을 방지합니다.

```text
기존 findings 배열에서 같은 code가 있으면 추가하지 않음
없으면 정확히 한 개 추가
```

선택 섹션 내부에도 해당 실행과 결합된 Finding이 유지됩니다. 자동 처리는 최상위 `findings` 배열을 사용하고, 선택 섹션은 경로 비교 자체의 완전한 스냅샷을 제공합니다.

## CSV

기존 CSV 문자열 뒤에 다음 section을 추가합니다.

```text
internalProxyRouteComparison
internalProxyRouteComparison.internalInterface
internalProxyRouteComparison.proxyInterface
internalProxyRouteComparison.finding
```

예:

```csv
"internalProxyRouteComparison","runStatus","Completed"
"internalProxyRouteComparison","comparisonStatus","Diverged"
"internalProxyRouteComparison","parsedProxyEndpointCount","1"
"internalProxyRouteComparison.internalInterface","interfaceFingerprint","0123456789"
"internalProxyRouteComparison.proxyInterface","interfaceFingerprint","abcdef0123"
"internalProxyRouteComparison.finding","code","INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED"
```

기존 보고서의 `Findings`에 같은 코드가 있으면 별도 경로 Finding 행을 추가하지 않습니다.

CSV header는 기존 `section,key,value` 한 개만 유지합니다.

모든 추가 값에도 기존 `SensitiveDataRedactor.ProtectCsvFormula`를 적용합니다.

## HTML

기존 통합 HTML의 마지막 `</main>` 직전에 다음 섹션을 삽입합니다.

```text
내부 DIRECT ↔ 프록시 로컬 경로 비교
실행·비교 상태
프록시 출처·결정
내부·프록시 경로 상태
후보 개수
DIRECT·fallback
내부·프록시 안전 인터페이스
고정 Finding
데이터 처리 선언
```

`</main>`이 없는 이전 HTML 형식은 `</body>` 직전에 삽입합니다. 두 태그가 모두 없으면 손상된 기존 HTML로 보고 실패합니다.

기존 통합 Finding 목록에 같은 경로 Finding이 이미 있으면 선택 섹션에서 Finding 상세를 다시 출력하지 않고 중복 생략 사실만 표시합니다.

동적 값은 모두 HTML 인코딩하며 기존 문서의 Content Security Policy와 외부 리소스 차단을 그대로 유지합니다.

## 안전 스냅샷만 사용

어댑터는 `InternalProxyRouteComparisonRunResult`의 원본 경로 필드나 자유형 메시지를 직접 복사하지 않습니다.

```text
RunResult
  → InternalProxyRouteComparisonRunSnapshotMapper
  → 통합 JSON·CSV·HTML
```

포함하는 값:

- 검증된 실행·프록시·라우팅·비교 상태
- HTTP·HTTPS 스킴
- 후보 수와 Boolean
- 10자리 소문자 16진수 인터페이스 지문
- 알려진 인터페이스 범주
- 고정 Finding과 고정 데이터 처리 선언

포함하지 않는 값:

- 내부 URL·호스트·IP
- 외부 URL
- 프록시 호스트·지시문·PAC 원문
- 전체 Windows 인터페이스 GUID
- 인터페이스 이름과 설명
- IP·MAC·게이트웨이·DNS
- SSID·BSSID
- 원본 내부·프록시 경로 객체
- 실행·비교 자유형 Message·Warnings·Limitation
- 예외 메시지

## 파일 저장

독립 저장도 지원합니다.

```text
WlanUnifiedDiagnostic_yyyyMMdd_HHmmss.json
WlanUnifiedDiagnostic_yyyyMMdd_HHmmss.csv
WlanUnifiedDiagnostic_yyyyMMdd_HHmmss.html
WlanUnifiedDiagnostic_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

같은 시각에 다시 생성하면 suffix를 추가해 기존 파일을 덮어쓰지 않습니다.

JSON·CSV·HTML은 임시 파일에 쓴 뒤 원자적으로 이동합니다. 세 파일의 SHA-256을 계산해 checksum 파일에 기록합니다.

## 기존 보고서 스키마와의 관계

이 구현은 `LocalDiagnosticReport` record 정의를 수정하지 않습니다.

장점:

- 기존 named·positional 생성자 호환
- 기존 테스트 fixture 호환
- 경로 비교가 없는 호출의 출력 완전 유지
- 이전 소비자는 알 수 없는 최상위 JSON 속성을 무시할 수 있음
- 새로운 소비자는 `internalProxyRouteComparison`만 선택적으로 읽을 수 있음

향후 기본 모델에 정식 optional 속성을 추가하려면 별도 schema version과 역직렬화 호환성 검증을 거쳐야 합니다.

## 통신 경계

통합 어댑터와 파일 Writer는 다음만 사용합니다.

- 기존 로컬 보고서 렌더러
- 안전 경로 비교 스냅샷
- 메모리 JSON DOM
- 로컬 파일 시스템
- SHA-256

수행하지 않는 작업:

- DNS 조회
- Windows 라우팅 API
- TCP·HTTP·HTTPS
- 프록시 인증
- PAC·WPAD
- 프록시 서버 API
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

## 자동 검증

ReportSmoke는 다음을 확인합니다.

1. 경로 비교 null이면 기존 JSON·CSV·HTML과 정확히 동일
2. JSON 최상위 선택 섹션의 `Completed / Diverged`
3. 검증된 내부·프록시 10자리 지문
4. 최상위 Finding 코드가 정확히 한 개
5. 원본 경로 속성 미포함
6. CSV header 한 개와 선택 section
7. HTML main 내부 삽입
8. 기존 Finding이 있으면 JSON·CSV 중복 방지
9. HTML 중복 생략 표시
10. 내부 URL·프록시 호스트·이메일·IP·전체 GUID·인터페이스 이름 비노출
11. 같은 시각 두 번 저장 시 독립 파일 8개
12. JSON·CSV·HTML SHA-256 재계산과 checksum 일치
