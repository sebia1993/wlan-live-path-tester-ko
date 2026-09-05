# 내부 DIRECT–프록시 경로 비교 실행 보고서

`InternalProxyRouteComparisonRunReportWriter`는 코디네이터가 완료한 실행 결과를 안전 스냅샷으로 다시 매핑한 뒤 JSON·CSV·단일 HTML과 SHA-256으로 현재 PC에 저장합니다.

Writer는 원본 내부 대상, 프록시 문자열 또는 원본 경로 객체를 입력 모델로 직접 직렬화하지 않습니다.

## 생성 파일

```text
WlanInternalProxyRouteComparison_yyyyMMdd_HHmmss.json
WlanInternalProxyRouteComparison_yyyyMMdd_HHmmss.csv
WlanInternalProxyRouteComparison_yyyyMMdd_HHmmss.html
WlanInternalProxyRouteComparison_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

같은 초에 다시 생성하면 다음처럼 suffix를 붙입니다.

```text
..._1.json
..._2.json
```

기존 파일을 덮어쓰지 않습니다.

## 생성 과정

```text
InternalProxyRouteComparisonRunResult
  ↓
InternalProxyRouteComparisonRunSnapshotMapper
  ↓ 검증된 안전 필드만 재매핑
InternalProxyRouteComparisonRunSnapshot
  ↓
InternalProxyRouteComparisonRunReportDocument
  ├─ JSON
  ├─ CSV
  ├─ HTML
  └─ SHA256SUMS
```

원본 실행 객체를 `JsonSerializer`에 직접 전달하지 않는 것이 핵심입니다.

## JSON 구조

```text
schemaVersion
generatedAt
applicationName
applicationVersion
sensitiveValuesIncluded
dataHandlingStatement
routeComparison
  schemaVersion
  completedAt
  runStatus
  proxySourceKind
  proxyDecision
  targetScheme
  internalRouteStatus
  proxyRouteStatus
  comparisonStatus
  sameLocalInterface
  internalInterface
  proxyInterface
  parsedProxyEndpointCount
  analyzedProxyEndpointCount
  successfulProxyEndpointCount
  proxyDistinctInterfaceCount
  directPresent
  directFallback
  expectedWlanIdentityAvailable
  internalRouteReadPerformed
  proxyRouteAnalysisPerformed
  internalEvidencePartial
  proxyEvidencePartial
  anyVirtualInterface
  anyVpnOrTunnelInterface
  finding
limitations[]
```

원본 `InternalRouteEvidence`와 `ProxyRouteAnalysis` 속성은 JSON에 존재하지 않습니다.

## 인터페이스 안전 필드

```text
interfaceFingerprint
category
isVirtual
isVpn
isUp
hasDefaultGateway
matchesExpectedWlan
```

인터페이스 지문은 다음 조건을 만족할 때만 저장됩니다.

```text
길이 10
소문자 16진수
```

전체 Windows GUID, 이름, 설명 또는 잘못된 범주는 인터페이스 스냅샷으로 인정하지 않습니다.

## Finding

실행·비교 상태에 따라 고정 Finding을 포함합니다.

예:

```text
INTERNAL_PROXY_ROUTE_COMPARISON_READY
INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED
INTERNAL_PROXY_ROUTE_COMPARISON_AMBIGUOUS
INTERNAL_PROXY_ROUTE_COMPARISON_INCOMPLETE
INTERNAL_PROXY_ROUTE_COMPARISON_DIRECT_PRIMARY
INTERNAL_PROXY_ROUTE_COMPARISON_INVALID_INPUT
```

Finding에는 구조화 enum·개수·Boolean만 사용하며 실행 객체의 자유형 메시지와 원본 경로 설명을 반사하지 않습니다.

## CSV

CSV는 다음 고정 header를 사용합니다.

```text
section,key,value
```

주요 section:

```text
metadata
run
internalInterface
proxyInterface
finding
limitation
```

모든 값은 큰따옴표로 감싸고 내부 큰따옴표를 이중화합니다.

다음 문자로 시작하는 값에는 apostrophe를 붙여 스프레드시트 수식 실행을 방지합니다.

```text
=
+
-
@
tab
carriage return
```

예:

```text
=1+1
  → '=1+1
```

## HTML

HTML 보고서는 다음 보호를 적용합니다.

- HTML5 doctype
- 모든 동적 값 HTML 인코딩
- Content Security Policy
- JavaScript 없음
- 외부 CSS 없음
- 웹폰트 없음
- 외부 이미지 없음
- iframe 없음
- form action 없음
- 화면·인쇄용 반응형 레이아웃

표시 영역:

```text
데이터 처리 선언
실행·비교 상태 badge
프록시 출처와 결정
내부·프록시 상태
파싱·분석·성공 후보 수
DIRECT·fallback
내부·프록시 단계 수행 여부
VPN·터널·가상 NIC 여부
내부 인터페이스 안전 지문·범주
프록시 인터페이스 안전 지문·범주
Finding 제목·근거·해석·다음 확인·한계
전체 판단 한계
```

## 애플리케이션 버전 보호

애플리케이션 버전은 다음 과정을 거칩니다.

1. URL·이메일·IP·MAC·Windows 사용자 경로 마스킹
2. 제어 문자 제거
3. 앞뒤 공백 제거
4. 최대 128자

HTML 태그 형태의 문자열은 HTML 출력에서 인코딩되고, 수식 시작 문자열은 CSV에서 비활성화됩니다.

## 개인정보 경계

보고서에 포함하지 않는 원문:

- 내부 DIRECT URL·호스트·IP
- 프록시 호스트와 지시문
- PAC·WPAD 원문
- 전체 Windows 인터페이스 GUID
- 인터페이스 이름과 설명
- IPv4·IPv6·MAC
- 기본 게이트웨이와 DNS
- SSID와 BSSID
- 예외 메시지
- 원본 `DestinationRouteEvidence`
- 원본 `ProxyEndpointRouteAnalysisResult`
- 실행·비교 객체의 자유형 Message·Warnings·Limitation

허용하는 식별값:

```text
검증된 10자리 호스트·인터페이스 지문
```

현재 보고서 스냅샷은 인터페이스 지문만 포함하며 실제 프록시 호스트 지문 목록은 포함하지 않습니다.

## SHA-256

`_SHA256SUMS.txt`에는 다음 세 파일의 SHA-256을 기록합니다.

```text
JSON
CSV
HTML
```

Writer는 각 파일을 원자적으로 저장한 뒤 해시를 계산합니다.

PowerShell 확인 예:

```powershell
Get-FileHash .\WlanInternalProxyRouteComparison_*.json -Algorithm SHA256
Get-FileHash .\WlanInternalProxyRouteComparison_*.csv -Algorithm SHA256
Get-FileHash .\WlanInternalProxyRouteComparison_*.html -Algorithm SHA256
```

## 통신 경계

보고서 생성에 사용하는 것은 다음뿐입니다.

- 메모리의 구조화 실행 결과
- 안전 스냅샷 mapper
- 로컬 파일 시스템
- 현재 시각
- SHA-256

수행하지 않는 작업:

- DNS 조회
- Windows 라우팅 API
- TCP 연결
- HTTP `HEAD`·`GET`
- 프록시 인증
- PAC·WPAD 다운로드
- 프록시 서버 API
- 외부 분석 API·AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

## 자동 검증

ReportSmoke는 다음을 확인합니다.

1. 구조화 `Completed / Diverged` 상태와 Finding
2. 검증된 내부·프록시 지문
3. JSON에 원본 경로 속성 없음
4. CSV header, 상태와 Finding 코드
5. CSV 수식 시작 버전 문자열 비활성화
6. HTML doctype·CSP·상태·Finding
7. HTML 태그 인코딩
8. script·iframe·외부 stylesheet 부재
9. 내부 URL·프록시 호스트·이메일·IP·전체 GUID 비노출
10. 같은 초 두 번 생성 시 독립 파일 8개
11. JSON·CSV·HTML 실제 SHA-256 재계산
12. `SHA256SUMS.txt`와 해시 일치

## 후속 UI 연결

WPF 경로 비교 화면은 다음 순서로 사용해야 합니다.

1. 코디네이터 실행 완료
2. 안전 실행 상태와 Finding 표시
3. 사용자가 `경로 비교 보고서 생성`을 명시적으로 누름
4. 이 Writer로 로컬 파일 생성
5. HTML 또는 폴더 열기

자동 저장이나 외부 업로드를 수행하지 않습니다.
