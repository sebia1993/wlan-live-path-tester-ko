# v0.1.0-alpha.11 사전 릴리스 노트

## 개요

이번 사전 릴리스는 외부망 속도 측정이 회사 프록시를 경유하는 환경에서, 현재 PC가 프록시 엔드포인트까지 어떤 Windows 로컬 인터페이스를 선택하는지 확인하고 내부 DIRECT 대상 경로와 비교하는 기능을 추가합니다.

프록시 서버 내부에는 접근하지 않습니다. 프록시 CPU·세션·큐·정책·캐시·클러스터 상태와 프록시 이후 외부 사이트 경로는 분석하지 않습니다.

## 핵심 변경

### 프록시 엔드포인트 문자열 파서

지원 입력:

```text
PROXY proxy-a.example:8080; DIRECT
HTTPS proxy-b.example:8443; DIRECT
SOCKS5 [2001:db8::1]:1080
http=proxy-http.example:8080;https=proxy-https.example:8443
all=common-proxy.example:3128
```

지원 지시문:

```text
PROXY
HTTP
HTTPS
SOCKS
SOCKS4
SOCKS5
DIRECT
```

주요 정책:

- 프록시와 `DIRECT`의 입력 순서 보존
- 프록시가 먼저면 `ProxyWithDirectFallback`
- `DIRECT`가 먼저면 `DirectWithProxyAlternatives`
- 대상 URL이 있으면 정확한 대상 스킴 또는 `all`·`*` 후보만 선택
- HTTPS 대상에 `http=` 후보를 임의 fallback하지 않음
- DNS·IDN·IPv4·IPv6 지원
- 명시 URI에만 안전한 기본 포트 적용
- 자격 증명·경로·query·fragment·잘못된 포트 거부
- 거부 경고에 프록시 원문을 다시 출력하지 않음
- 16 KiB 입력, 64 토큰, 32 후보 제한

파서는 문자열만 처리하며 DNS·TCP·HTTP·PAC 다운로드·WPAD 탐색을 수행하지 않습니다.

### 프록시 엔드포인트 로컬 경로 분석

사용자가 명시적으로 실행한 경우에만 다음을 확인합니다.

```text
현재 PC
  → 운영체제 DNS
  → Windows 최적 로컬 인터페이스
  → 프록시 엔드포인트 주소
```

- IP literal이면 DNS 생략
- IPv4·IPv6별 Windows 최적 인터페이스 판정
- 현재 Native WLAN GUID와 상관 분석
- `DIRECT`가 첫 경로이면 프록시 DNS·route 조회 0회
- 프록시 뒤 `DIRECT`이면 DIRECT 앞 후보만 순서대로 분석
- DIRECT 뒤 프록시는 분석하지 않음

구조화 상태:

```text
InvalidInput
DirectPathSelected
NoApplicableEndpoint
Success
PartialSuccess
MultipleInterfaces
Canceled
Failed
```

결과에는 프록시 호스트 원문 대신 host fingerprint, 인터페이스 전체 GUID·이름 대신 interface fingerprint·category·VPN·가상·Up·default gateway 여부만 남깁니다.

### 내부 DIRECT–프록시 로컬 경로 비교

내부 승인 DIRECT 대상과 적용 프록시 후보의 Windows 로컬 인터페이스를 비교합니다.

```text
Ready
Incomplete
Ambiguous
Diverged
```

- `Ready`: 양쪽 단일 인터페이스 근거가 충분하고 같은 지문
- `Diverged`: 양쪽 근거가 충분하지만 다른 지문
- `Ambiguous`: 여러 인터페이스 또는 충돌하는 메타데이터
- `Incomplete`: 내부·프록시 근거 부족, 외부 DIRECT, 프록시 fallback 일부 실패

내부 일부 주소군만 성공하더라도 단일 인터페이스가 확정되면 비교할 수 있지만 부분 근거 경고를 남깁니다. 프록시 fallback 후보 일부가 실패하면 확인되지 않은 후보가 있으므로 `Incomplete`로 유지합니다.

유효한 Native WLAN GUID가 없더라도 내부·프록시 인터페이스 지문 자체는 비교하며 WLAN 일치 여부만 `null`로 남깁니다.

### 로컬 경로 비교 보고서

```text
WlanInternalProxyRoute_yyyyMMdd_HHmmss.json
WlanInternalProxyRoute_yyyyMMdd_HHmmss.csv
WlanInternalProxyRoute_yyyyMMdd_HHmmss.html
WlanInternalProxyRoute_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

주요 Finding:

```text
INTERNAL_PROXY_LOCAL_ROUTE_ALIGNED
INTERNAL_PROXY_LOCAL_ROUTE_DIVERGED
INTERNAL_PROXY_LOCAL_ROUTE_AMBIGUOUS
INTERNAL_PROXY_LOCAL_ROUTE_INCOMPLETE
LOCAL_ROUTE_VPN_OR_TUNNEL_PRESENT
LOCAL_ROUTE_VIRTUAL_INTERFACE_PRESENT
```

보고서에는 내부 URL·프록시 호스트·인터페이스 전체 GUID·이름·설명·IP·MAC·SSID·BSSID를 포함하지 않습니다.

허용 인터페이스 지문은 10자리 소문자 16진수로 제한합니다. 전체 GUID나 잘못된 지문은 보고서에서 제거합니다.

CSV 수식 주입 방지, HTML 인코딩·Content Security Policy, 외부 JavaScript·CSS·iframe 미사용 정책을 적용합니다.

### WPF `로컬 경로 비교` 화면

입력:

- 내부 승인 DIRECT 대상 URL·호스트·IP
- 프록시 스킴을 선택할 외부 HTTP(S) URL
- Windows 자동 프록시 결과 또는 수동 서버 목록

버튼:

- 로컬 경로 비교 실행
- 경로 확인 중지
- 비교 보고서 생성
- 보고서 폴더 열기
- 최신 HTML 열기

다운로드 측정 또는 브라우저 관찰이 실행 중이면 경로 비교를 시작하지 않습니다.

결과 화면에는 프록시 원문 대신 지문, 인터페이스 전체 GUID·이름 대신 지문·category, 현재 WLAN 일치·VPN·가상 여부와 고정 Finding만 표시합니다.

사용자가 입력한 프록시 원문은 TextBox에 보이므로 회사 밖으로 스크린샷을 공유할 때 직접 가려야 합니다.

## 자동 검증

### Core SelfTest

- 자동 PROXY·HTTPS·SOCKS·DIRECT 순서
- 수동 `http=`·`https=` exact target scheme
- 교차 스킴 fallback 금지
- IDN·IPv4·IPv6
- 자격 증명·경로·query·잘못된 포트 거부
- 중복 프록시·DIRECT 제거
- DIRECT 우선 순서
- 입력·토큰·후보 안전 한도
- 안전 라벨의 프록시 호스트 비노출
- Ready·Diverged·Ambiguous·Incomplete 비교
- 내부 부분 성공과 프록시 부분 성공의 서로 다른 처리
- 같은 지문의 메타데이터 충돌
- WLAN GUID 미확인 상태의 안전한 지문 비교

### WindowsSmoke

- DIRECT 우선에서 프록시 route reader 호출 0회
- DIRECT 앞 후보만 순서대로 분석
- 동일 WLAN interface correlation
- 무선·유선·VPN·가상 NIC 경로
- MultipleInterfaces·PartialSuccess·Canceled
- 파서 오류·적용 후보 없음·잘못된 DNS timeout에서 reader 호출 없음
- 내부 Wi-Fi와 프록시 VPN tunnel의 전체 사용자 흐름
- 전용 JSON·CSV·HTML에서 원문 프록시·GUID·이름 비노출

### ReportSmoke

- 네 비교 상태와 Finding 코드·심각도
- VPN·가상 인터페이스 보조 Finding
- JSON 구조화 결과
- CSV 머신용 코드와 수식 비활성화
- HTML 사람용 제목·해석과 CSP
- 전체 GUID·이메일·IP·URL 비노출
- 잘못된 지문 제거
- JSON·CSV·HTML·SHA-256 생성과 실제 해시 재계산

### Portable ZIP

브라우저 관찰, 프록시 파서, 경로 분석·비교, 전용 보고서와 사용자 화면 운영 문서를 publish와 실제 ZIP 엔트리에서 검증합니다.

- 필수 문서 존재
- 파일 크기 0 초과
- case-sensitive 엔트리 정확히 한 개
- 실제 설정·로그·보고서·캡처 미포함

## 통신·데이터 경계

### 문자열 파서와 비교·보고서

수행하지 않음:

- DNS
- TCP
- HTTP·HTTPS
- PAC 다운로드·WPAD 탐색
- 프록시 인증
- 외부 API·AI·로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

### 사용자가 로컬 경로 비교를 실행한 경우

수행 가능:

- 내부 대상과 프록시 호스트의 운영체제 DNS
- IP literal의 DNS 생략
- Windows 최적 로컬 인터페이스 판정
- Native WLAN GUID 상관 분석

수행하지 않음:

- 프록시 TCP 연결
- HTTP CONNECT
- 프록시 인증
- 내부·외부 HEAD·GET 다운로드
- 프록시 서버 내부 API·상태 조회
- 프록시 이후 외부 경로 분석

외부 속도 측정은 기존 다운로드 측정 화면에서 사용자가 별도로 시작한 HEAD·GET만 사용합니다.

## 배포물

GitHub Release에는 기존 정책대로 정확히 네 파일만 유지합니다.

```text
WlanLivePathTester-win-x64-portable.zip
WlanLivePathTester-win-x64-single-file.exe
SHA256SUMS.txt
THIRD_PARTY_NOTICES.md
```

두 실행 형태 모두 .NET 런타임을 포함하므로 Python 또는 별도 .NET 설치가 필요하지 않습니다.

## 현재 한계

- Windows/PAC/WPAD의 프록시 결과를 `로컬 경로 비교` 입력란에 자동으로 가져오는 연결은 아직 추가하지 않았습니다.
- 사용자는 기존 프록시 판정 화면 또는 회사 설정에서 결과 문자열을 확인해 붙여 넣어야 합니다.
- 실제 회사 PAC·WPAD, HTTP 407 Negotiate·NTLM, TLS 검사, GPO·EDR·SmartScreen은 사용자 환경에서 검증해야 합니다.
- 프록시 경로 분석은 서버에 연결하지 않으므로 실제 프록시 가용성이나 DIRECT fallback 발생을 확인하지 않습니다.
- 상용 Authenticode 인증서가 없어 실행 파일은 아직 코드 서명되지 않았습니다.

## 권장 실제 검증

1. 내부 승인 DIRECT 대상과 외부 승인 URL 준비
2. Windows/PAC/WPAD의 실제 프록시 결과 확인
3. DIRECT 우선·프록시 우선·프록시 뒤 DIRECT fallback 각각 입력
4. 내장 Wi-Fi 단독 경로 비교
5. 내장·USB Wi-Fi 동시 활성
6. 유선 연결·해제 전후 비교
7. 회사 VPN 연결·해제 전후 비교
8. Hyper-V·WSL·VMware·보안 에이전트 가상 NIC 환경
9. IPv4·IPv6 프록시 DNS 결과
10. 복수 프록시 후보가 서로 다른 로컬 인터페이스를 선택하는지 확인
11. 비교 보고서 JSON·CSV·HTML·SHA-256 생성
12. 실제 내부 URL·프록시 호스트·GUID·IP가 보고서에 없는지 확인
13. 기존 내부 DIRECT·외부 PROXY 다운로드 측정과 결과 비교
14. PAC·WPAD·407·TLS 검사 실환경 확인
15. 공개 Release 자산 SHA-256 재검증
