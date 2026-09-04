# 유효 프록시 엔드포인트 문자열 파서

이 Core 계층은 Windows 수동 프록시 설정 또는 PAC/WPAD 평가 결과에서 프록시 후보와 `DIRECT` fallback을 엄격하게 분리합니다.

이 파서 자체는 문자열만 처리하며 네트워크 요청을 수행하지 않습니다.

## 지원 형식

### 단일 프록시

```text
proxy.example:8080
```

### 스킴별 수동 프록시

```text
http=proxy-http.example:8080;https=proxy-https.example:8443
```

대상 URL이 HTTPS이면 `https=` 항목을 우선합니다. `https=` 항목이 없고 `http=` 항목만 있으면 공통 HTTP 프록시 후보로 사용할 수 있습니다.

### 복수 프록시와 DIRECT fallback

```text
proxy-a.example:8080;proxy-b.example:8080;DIRECT
```

### PAC 스타일 transport prefix

```text
PROXY proxy-a.example:8080;HTTPS proxy-b.example:443;SOCKS5 socks.example:1080;DIRECT
```

### IPv6

```text
HTTPS [2001:db8::10]:8443
```

포트를 포함하는 IPv6는 대괄호 형식을 사용해야 합니다.

## 구조화 결과

```text
Decision
  ├─ Direct
  ├─ Proxy
  ├─ ProxyWithDirectFallback
  └─ Unresolved

Endpoints[]
  ├─ Sequence
  ├─ Transport: Http / Https / Socks / Unknown
  ├─ Host
  ├─ Port
  └─ HostFingerprint

Warnings[]
Errors[]
```

`Host`는 후속 DNS·라우팅 분석을 위한 로컬 메모리 값입니다. 화면·JSON·CSV·HTML에 원문을 전달할 때는 별도 마스킹 또는 안전 모델을 사용해야 합니다.

`HostFingerprint`는 정규화한 호스트 문자열의 SHA-256 앞 10자리입니다. 동일 후보 비교를 위한 지문이며 원문 프록시 호스트를 표시하는 값이 아닙니다.

## 제한

- 전체 문자열 최대 8192자
- 프록시 후보 최대 8개
- 포트 1~65535
- HTTP·HTTPS·SOCKS transport만 명시 지원
- DNS 호스트 또는 IPv4·IPv6만 허용
- 후보 중복 제거
- IDN 호스트를 ASCII로 정규화

## 거부하는 값

다음 항목은 프록시 엔드포인트로 사용하지 않습니다.

```text
http://user:password@proxy.example:8080
http://proxy.example:8080/path
proxy.example:8080?query=1
proxy.example:8080#fragment
proxy.example:70000
ftp://proxy.example:21
```

사용자 이름·비밀번호, 경로, 쿼리, fragment와 범위를 벗어난 포트는 오류로 처리합니다. 잘못된 후보를 정상 후보처럼 부분 적용하지 않습니다.

## DIRECT 처리

프록시 후보 없이 `DIRECT`만 있으면 `Direct`입니다.

프록시 후보 뒤에 `DIRECT`가 있으면 `ProxyWithDirectFallback`입니다. 이 상태는 첫 프록시 장애 시 직접 연결을 허용할 가능성이 있으므로 외부 측정의 회사 정책과 일치하는지 별도로 확인해야 합니다.

## 다음 연결 단계

파서는 PAC/WPAD를 직접 실행하지 않습니다. 후속 Windows 계층은 다음 순서를 사용합니다.

```text
사용자가 외부 URL 경로 확인 실행
  ↓
기존 WinHTTP 프록시 경로 판정
  ↓
원문 결정 문자열을 Core 파서에 전달
  ↓
각 프록시 후보를 로컬 라우팅 분석
  ↓
화면·보고서에는 후보 번호·transport·port·호스트 지문·인터페이스 범주만 표시
```

PAC URL, 프록시 호스트와 해석 IP 원문은 공개 보고서에 포함하지 않습니다.

## 통신·데이터 경계

이 파서의 테스트와 실행에는 다음 통신이 없습니다.

- DNS
- HTTP/HTTPS
- PAC/WPAD 다운로드
- 외부 API
- 텔레메트리·업로드

실제 PAC/WPAD 평가는 별도 Windows 계층에서 사용자가 명시적으로 실행한 경우에만 수행해야 합니다.
