# 로컬 경로 비교 화면

`로컬 경로 비교` 탭은 사용자가 입력한 내부 DIRECT 대상과 Windows 프록시 결과를 기반으로 현재 PC의 로컬 인터페이스 경로를 비교합니다.

실행 버튼을 누르기 전에는 DNS, 라우팅 API 또는 프록시 관련 네트워크 작업을 하지 않습니다.

## 입력

### 내부 승인 DIRECT 대상

다음 중 하나를 입력합니다.

```text
https://internal-file.example/file.bin
internal-file.example
10.0.0.10
```

이 화면은 PAC·WPAD에서 해당 대상이 DIRECT인지 자동 보증하지 않습니다. 회사에서 승인된 내부 DIRECT 측정 대상을 사용해야 합니다.

### 외부 측정 대상 URL

프록시 수동 스킴 매핑을 선택하기 위한 절대 HTTP 또는 HTTPS URL입니다.

```text
https://external-download.example/file.bin
```

이 입력은 프록시 후보 선택에만 사용하며 로컬 경로 비교 단계에서는 외부 대상에 HEAD·GET을 보내지 않습니다.

사용자 정보 또는 fragment가 있는 URL은 거부합니다.

### Windows 프록시 결과 또는 수동 서버 목록

예:

```text
PROXY proxy.example:8080; DIRECT
HTTPS secure-proxy.example:8443; DIRECT
http=proxy-http.example:8080;https=proxy-https.example:8443
DIRECT
```

입력 원문은 현재 화면의 TextBox에만 남습니다. 결과 화면과 보고서는 프록시 호스트 원문 대신 SHA-256 기반 10자리 지문을 사용합니다.

회사 밖으로 스크린샷을 공유할 때는 입력란의 프록시 호스트 원문을 직접 가려야 합니다.

## 실행 순서

```text
입력 검증
  ↓
현재 Native WLAN GUID 확인
  ↓
내부 대상의 Windows 로컬 경로 확인
  ↓
프록시 문자열 해석
  ↓
DIRECT 앞의 적용 프록시 후보만 로컬 경로 확인
  ↓
내부 DIRECT–프록시 인터페이스 비교
  ↓
안전한 화면 결과와 보고서 생성
```

다운로드 측정 또는 브라우저 관찰이 실행 중이면 로컬 경로 비교를 시작하지 않습니다.

## 수행 가능한 로컬 작업

사용자가 `로컬 경로 비교 실행`을 누른 경우에만 다음을 수행할 수 있습니다.

- 내부 대상 DNS 확인
- 프록시 후보 DNS 확인
- IP literal이면 DNS 생략
- IPv4·IPv6별 Windows 최적 로컬 인터페이스 판정
- 현재 Native WLAN GUID와 인터페이스 상관 분석

## 수행하지 않는 작업

- 프록시 TCP 연결
- HTTP CONNECT
- 프록시 인증
- 내부·외부 HEAD·GET 다운로드
- PAC URL 다운로드
- WPAD 탐색
- 프록시 서버 API·CPU·세션·정책·캐시 조회
- 외부 분석 API·AI·로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

외부 속도 측정은 기존 다운로드 측정 화면에서 사용자가 별도로 시작해야 합니다.

## `DIRECT` 순서

### DIRECT 우선

```text
DIRECT; PROXY later.example:8080
```

프록시 DNS·라우팅 조회를 수행하지 않습니다.

결과:

```text
비교 미완료
DIRECT 우선: 예
프록시 후보 분석: 0개
```

비교 대상인 프록시 엔드포인트 경로가 없기 때문입니다.

### 프록시 뒤 DIRECT fallback

```text
PROXY first.example:8080; DIRECT
```

첫 프록시 후보의 로컬 경로는 확인하지만 실제 연결 실패와 DIRECT 전환을 시험하지 않습니다.

결과에는 `DIRECT fallback: 있음` 경고가 남습니다.

## 화면 결과

화면에는 다음 안전한 값만 표시합니다.

- `Ready`, `Diverged`, `Ambiguous`, `Incomplete`의 한국어 상태
- 내부·프록시 인터페이스 10자리 지문
- 인터페이스 category
- 현재 Native WLAN과 일치 여부
- VPN·가상 여부
- 프록시 후보·성공 후보·서로 다른 인터페이스 수
- DIRECT 우선·fallback 여부
- 고정 Finding 제목과 해석
- 고정 주의사항과 한계

프록시 호스트, 인터페이스 전체 GUID·이름·설명, IP·MAC·SSID·BSSID와 URL은 결과 화면에 다시 출력하지 않습니다.

예외가 발생해도 사용자 입력값이나 예외 메시지를 그대로 반사하지 않고 예외 타입만 표시합니다.

## 중지

`경로 확인 중지`를 누르면 현재 DNS·라우팅 분석 CancellationToken을 취소합니다.

완료되지 않은 비교 결과는 `_lastInternalProxyRouteComparison`에 저장하지 않으며 보고서 생성 버튼도 활성화하지 않습니다.

## 비교 보고서

완료된 결과가 있으면 다음 네 파일을 생성할 수 있습니다.

```text
WlanInternalProxyRoute_yyyyMMdd_HHmmss.json
WlanInternalProxyRoute_yyyyMMdd_HHmmss.csv
WlanInternalProxyRoute_yyyyMMdd_HHmmss.html
WlanInternalProxyRoute_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

화면에서 다음 작업을 할 수 있습니다.

- 비교 보고서 생성
- 보고서 폴더 열기
- 최신 HTML 열기

보고서 생성은 메모리에 있는 완료된 비교 결과만 사용하며 DNS·라우팅·프록시 요청을 다시 수행하지 않습니다.

## 개인정보 경계

보고서 Writer는 다음 보호를 다시 적용합니다.

- 10자리 소문자 16진수 인터페이스 지문만 허용
- 전체 GUID 제거
- URL·이메일·IP·MAC·사용자 경로 마스킹
- CSV 수식 비활성화
- HTML 인코딩과 CSP
- 외부 script·stylesheet·iframe 없음

입력 TextBox 자체는 사용자가 입력한 원문을 보여 주므로 화면 캡처 공유 시 별도 주의가 필요합니다.

## 자동 검증

주입식 WindowsSmoke는 실제 DNS나 프록시 없이 다음 전체 사용자 흐름을 검증합니다.

### 프록시 터널 경로

```text
내부 DIRECT → 현재 Wi-Fi
프록시 endpoint → VPN tunnel
```

기대 결과:

```text
Diverged
INTERNAL_PROXY_LOCAL_ROUTE_DIVERGED
LOCAL_ROUTE_VPN_OR_TUNNEL_PRESENT
LOCAL_ROUTE_VIRTUAL_INTERFACE_PRESENT
```

전용 JSON·CSV·HTML에 프록시 호스트·GUID·인터페이스 이름이 남지 않아야 합니다.

### DIRECT 우선

```text
DIRECT; PROXY later.example:8080
```

기대 결과:

- proxy route reader 호출 0회
- `DirectPathSelected`
- 비교 `Incomplete`
- 정보성 `INTERNAL_PROXY_LOCAL_ROUTE_INCOMPLETE`

## 실제 환경 검증

1. 회사에서 승인한 내부 DIRECT 대상과 외부 속도 측정 URL을 입력합니다.
2. Windows/PAC/WPAD가 반환한 프록시 결과를 붙여 넣습니다.
3. 실행 전에 입력란의 원문이 올바른지 확인합니다.
4. 로컬 경로 비교를 실행합니다.
5. 내부와 프록시의 인터페이스 지문·category·현재 WLAN 일치 여부를 확인합니다.
6. VPN 연결 전후 결과를 비교합니다.
7. 유선 연결 전후 결과를 비교합니다.
8. 내장·USB Wi-Fi 동시 환경에서 결과를 비교합니다.
9. 보고서를 생성해 JSON·CSV·HTML을 확인합니다.
10. 실제 프록시 호스트·내부 URL·GUID·IP가 보고서에 남지 않는지 직접 검토합니다.

이 결과만으로 프록시 장애나 WLAN 장애를 확정하지 않습니다. 내부·외부 처리량, HTTP 상태, 프록시 인증, RSSI·PHY·로밍과 장비 로그를 함께 판단합니다.
