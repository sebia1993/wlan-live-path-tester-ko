# 내부 DIRECT·프록시 경로 비교 전용 보고서

`경로 보고서` 탭은 가장 최근 내부 DIRECT·프록시 경로 비교 결과를 로컬 파일로 저장합니다. 보고서 생성 시 DNS, Windows 라우팅 API 또는 프록시 연결을 다시 실행하지 않고 앱 메모리에 이미 있는 비교 결과만 사용합니다.

## 생성 파일

```text
WlanRouteComparison_yyyyMMdd_HHmmss.json
WlanRouteComparison_yyyyMMdd_HHmmss.csv
WlanRouteComparison_yyyyMMdd_HHmmss.html
WlanRouteComparison_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

- JSON: 프로그램과 후속 로컬 분석을 위한 구조화 데이터
- CSV: `section,key,value` 형식의 비교·후보·Finding 데이터
- HTML: 외부 리소스 없는 사람이 읽는 단일 보고서
- SHA-256: JSON·CSV·HTML 세 파일의 무결성 목록

같은 초에 여러 번 저장하면 `_1`, `_2` suffix를 사용해 기존 파일을 덮어쓰지 않습니다.

## 보고서 구조

### 메타데이터

```text
schemaVersion
generatedAt
applicationName
applicationVersion
sensitiveValuesIncluded
dataHandlingStatement
```

### 비교 결과

```text
status
relation
code
internalRouteStatus
proxyAnalysisStatus
internalInterfaceFingerprint
internalInterfaceCategory
proxyInterfaceFingerprints[]
proxyInterfaceCategories[]
proxyEndpointCount
successfulProxyRouteCount
directDirectiveCount
proxyAnalysisWasTruncated
exactIdentityComparisonPerformed
hasCompleteComparableEvidence
message
interpretation
limitation
nextStep
```

### 프록시 후보

각 후보에는 다음 안전 필드만 저장합니다.

```text
sequence
kind
sourceSyntax
scope
port
hostFingerprint
status
selectedInterfaceFingerprint
selectedInterfaceCategory
selectedInterfaceOperationalState
wlanCorrelationStatus
networkLookupPerformed
```

`DIRECT` 항목은 `networkLookupPerformed=false`입니다.

### 파싱 경고

```text
segmentIndex
severity
code
```

Issue의 임의 메시지는 보고서에 복사하지 않습니다. 원문 프록시 문자열·호스트·자격 증명을 오류 문장에 반사하는 것을 막기 위한 경계입니다.

### Finding

```text
code
severity
title
evidence
interpretation
limitation
nextStep
```

`Ready`, `Diverged`, `Ambiguous`, `Incomplete`에 대응하는 고정 비교 Finding을 저장합니다.

## 안전 모델 재매핑

Writer는 `ProxyEndpointRouteAnalysisResult`를 그대로 JSON 직렬화하지 않습니다. 다음 순서로 새 보고서 모델을 만듭니다.

1. 강한 enum 값을 알려진 문자열로 정규화
2. 내부·프록시 상태 문자열이 지원 enum인지 확인
3. 인터페이스 범주와 운영 상태를 지원 enum으로 제한
4. scope를 `all`, `http`, `https`, `ftp`, `socks`, `socks4`, `socks5`로 제한
5. 포트를 1~65535로 제한
6. 호스트·인터페이스 지문을 소문자 10자리 16진수로 제한
7. 파싱 Issue code를 대문자·숫자·underscore로 제한
8. 원본 `RouteEvidence`, 후보 `Message`, `RedactedDisplay`, Issue `Message` 미복사
9. 비교·Finding 서술문의 URL·IP·MAC·이메일·Windows 사용자 경로·GUID·DNS 이름 추가 마스킹
10. 서술문 길이를 최대 2048자로 제한

따라서 내부 메모리 객체가 손상되거나 임의 값이 주입돼도 공개 보고서에는 검증된 값만 남습니다.

## 개인정보 경계

보고서에 포함하지 않는 원문:

- 내부 DIRECT URL·호스트·IP
- 프록시 호스트와 PAC 원문
- 프록시 URI의 사용자 이름·암호
- 전체 물리·가상 인터페이스 GUID
- 인터페이스 이름과 설명
- SSID와 BSSID
- IPv4·IPv6·MAC
- 기본 게이트웨이와 DNS
- 원본 `DestinationRouteEvidence`
- 원본 예외 메시지

표시 가능한 식별값은 SHA-256 앞 10자의 호스트·인터페이스 지문입니다. 짧은 지문은 정확 NIC 판정에 사용하지 않고 보고서 간 같은 후보 비교에만 사용합니다.

## CSV 안전성

CSV 값이 다음 문자로 시작하면 apostrophe를 붙여 스프레드시트 수식 실행을 방지합니다.

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

모든 값은 큰따옴표로 감싸고 내부 큰따옴표를 이중화합니다.

## HTML 안전성

- 모든 동적 값 HTML 인코딩
- Content Security Policy
- 외부 JavaScript 없음
- 외부 CSS 없음
- 웹폰트 없음
- 외부 이미지 없음
- iframe 없음
- form action 없음
- 화면·인쇄용 반응형 레이아웃

HTML은 머신용 Finding 코드와 사람이 읽는 제목·근거·해석·조치·한계를 함께 제공합니다.

## SHA-256

`_SHA256SUMS.txt`는 다음 세 파일의 SHA-256을 기록합니다.

```text
JSON
CSV
HTML
```

PowerShell 확인 예:

```powershell
Get-FileHash .\WlanRouteComparison_*.json -Algorithm SHA256
Get-FileHash .\WlanRouteComparison_*.csv -Algorithm SHA256
Get-FileHash .\WlanRouteComparison_*.html -Algorithm SHA256
```

## UI 사용 순서

1. `경로 비교` 탭에서 내부 DIRECT 대상과 프록시 지시문을 입력합니다.
2. 비교를 완료하고 상태·관계·Finding을 확인합니다.
3. `경로 보고서` 탭을 엽니다.
4. `경로 비교 보고서 생성`을 누릅니다.
5. JSON·CSV·HTML·SHA-256 네 파일을 확인합니다.
6. `최신 HTML 열기`로 로컬 보고서를 검토합니다.
7. 회사 밖으로 공유하기 전 실제 원문이 남지 않았는지 다시 확인합니다.

측정·브라우저 관찰 또는 경로 비교가 진행 중이면 보고서 생성을 시작하지 않습니다.

## 통신 경계

보고서 생성이 사용하는 것은 다음뿐입니다.

- 메모리 내 비교 결과
- 메모리 내 안전 프록시 경로 요약
- 메모리 내 고정 Finding
- 로컬 파일 시스템
- 보고서 생성 시각

다음 작업은 수행하지 않습니다.

- DNS 조회
- Windows 라우팅 API
- TCP 연결
- HTTP/HTTPS 요청
- 프록시 인증
- PAC/WPAD 다운로드
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

## 자동 검증

ReportSmoke는 다음을 확인합니다.

- JSON 구조화 상태·후보 수
- 원본 `RouteEvidence` 속성 미포함
- CSV 비교 상태·Finding 코드
- CSV 수식 비활성화
- HTML doctype·CSP·Finding 표시
- HTML 태그 인코딩
- script·iframe·외부 stylesheet 부재
- 내부 URL·프록시 호스트·이메일·IP·전체 GUID·인터페이스 설명 비노출
- 같은 초 두 번 생성 시 8개 독립 파일
- JSON·CSV·HTML 해시 재계산과 SHA256SUMS 일치
