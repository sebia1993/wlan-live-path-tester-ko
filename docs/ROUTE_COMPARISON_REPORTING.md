# 내부·프록시 경로 비교 구조화 보고서

`경로 비교 보고서` 탭은 현재 앱 메모리에 저장된 라우팅 이력에서 목적별 최신 결과를 다시 비교하고 다음 파일을 생성합니다.

```text
WlanRouteComparison_yyyyMMdd_HHmmss.json
WlanRouteComparison_yyyyMMdd_HHmmss.csv
WlanRouteComparison_yyyyMMdd_HHmmss.html
WlanRouteComparison_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

## 통신 경계

보고서 생성은 `RouteEvidenceResultHistory`에 이미 있는 로컬 메모리 이력만 사용합니다.

다음 작업은 다시 수행하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC 또는 WPAD 평가
- 프록시 연결
- 외부 API 호출
- 텔레메트리·파일 업로드

따라서 과거 라우팅 결과를 보고서로 저장하는 과정에서 새로운 네트워크 변수가 추가되지 않습니다.

## 보고서 구조

### 전체 비교

```text
status
message
internalDirect
proxyEndpoint
externalReference
findings[]
limitations[]
```

전체 상태는 다음 중 하나입니다.

- `Ready`
- `Incomplete`
- `Ambiguous`
- `Diverged`

### 경로 Point

내부 DIRECT, 프록시 엔드포인트와 외부 사이트 참고 결과는 동일한 구조를 사용합니다.

```text
purpose
capturedAt
routeStatus
wlanCorrelationStatus
interfaceFingerprint
interfaceCategory
isVpn
isVirtual
warningCount
```

원문 인터페이스 ID는 저장하지 않습니다. `interfaceFingerprint`는 정확히 10자리 hex 지문만 허용하며, 전체 GUID 또는 형식이 다른 값은 `null`로 저장합니다.

### Finding

각 판정은 다음 필드를 가집니다.

```text
code
severity
Title
evidence
interpretation
nextStep
```

Finding은 AI가 아닌 고정 규칙으로 생성됩니다. 같은 입력 이력에는 같은 상태와 판정 코드가 생성됩니다.

## JSON

JSON은 자동 분석이나 별도 로컬 프로그램에서 사용하기 위한 형식입니다. 숫자·Boolean·날짜·상태를 문자열 문장에서 분리해 저장합니다.

## CSV

CSV는 `section,key,value` 구조를 사용합니다.

```text
comparison
comparison.internalDirect
comparison.proxyEndpoint
comparison.externalReference
finding.1
finding.2
limitation
```

값이 `=`, `+`, `-`, `@` 등 스프레드시트 수식 시작 문자로 시작하면 실행되지 않도록 비활성화합니다.

## HTML

HTML은 다음 내용을 제공합니다.

- 전체 비교 상태 배지
- 내부·프록시·외부 참고 경로 카드
- 라우팅 상태와 WLAN 상관 상태
- 인터페이스 범주·지문·VPN·가상 여부
- Finding별 근거·해석·다음 확인
- 판단 한계

다음 외부 요소는 포함하지 않습니다.

- JavaScript
- 외부 CSS
- 웹폰트
- 외부 이미지
- iframe

Content Security Policy와 HTML 인코딩을 적용합니다.

## SHA-256

`_SHA256SUMS.txt`에는 JSON·CSV·HTML 세 파일의 SHA-256을 기록합니다. 전달·보관 후 다음 명령으로 무결성을 확인할 수 있습니다.

```powershell
Get-FileHash .\WlanRouteComparison_*.json -Algorithm SHA256
Get-FileHash .\WlanRouteComparison_*.csv -Algorithm SHA256
Get-FileHash .\WlanRouteComparison_*.html -Algorithm SHA256
```

## 포함하지 않는 값

비교 보고서 모델에는 다음 원문이 없습니다.

- 목적지 IPv4·IPv6 주소
- 게이트웨이와 DNS 서버 주소
- MAC 주소
- 인터페이스 이름·설명
- 전체 인터페이스 GUID
- 내부·외부 URL
- 프록시 호스트와 PAC URL

설명·경고·Finding 문자열도 기존 민감정보 마스킹을 거쳐 저장합니다.

## 불완전 보고서

프록시 엔드포인트 근거가 없거나 Native WLAN ID를 확인하지 못해도 보고서 생성은 허용합니다. 이 경우 `Incomplete` 상태와 필요한 다음 확인을 구조화해 남깁니다.

외부 사이트 참고 경로만 있고 프록시 엔드포인트 경로가 없는 경우에는 외부 사이트 직접 경로를 실제 프록시 경로로 대신하지 않습니다.

## 권장 사용 순서

1. 내부 URL을 `내부 DIRECT 측정 대상`으로 라우팅 확인합니다.
2. 운영 정책상 확인 가능한 프록시 호스트를 `프록시 엔드포인트`로 확인합니다.
3. `경로 비교` 탭에서 현재 상태와 Finding을 확인합니다.
4. `경로 비교 보고서`에서 파일을 생성합니다.
5. SHA-256을 확인하고 필요한 범위에서만 공유합니다.
6. 회사 밖으로 전달하기 전에는 마스킹 결과를 직접 다시 검토합니다.
