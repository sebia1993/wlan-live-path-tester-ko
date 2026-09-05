# 프록시 엔드포인트 지시문 로컬 해석

이 기능은 Windows 수동 프록시 문자열이나 PAC/WPAD 판정 결과에서 실제 프록시 후보와 `DIRECT` fallback 순서를 추출합니다. 회사 프록시 서버에 로그인하거나 관리 API를 호출하지 않고 현재 PC에 이미 전달된 문자열만 로컬에서 해석합니다.

## 지원 입력

### PAC·WPAD 결과 형식

```text
PROXY proxy-a.example:8080; HTTPS proxy-b.example:8443; SOCKS5 [2001:db8::5]:1080; DIRECT
```

지원 키워드:

```text
PROXY
HTTP
HTTPS
SOCKS
SOCKS4
SOCKS5
DIRECT
```

세미콜론 순서를 그대로 유지하므로 프록시 fallback과 최종 `DIRECT`를 구분할 수 있습니다.

### Windows 프로토콜별 수동 프록시

```text
http=proxy-http.example:8080;https=proxy-connect.example:8080;ftp=DIRECT;socks=proxy-socks.example:1080
```

지원 범위:

```text
http
https
ftp
proxy
all
socks
socks4
socks5
```

Windows의 `https=host:port`는 HTTPS 목적지에 적용되는 프록시 범위입니다. 프록시 서버 자체가 TLS 프록시라는 의미로 단정하지 않으므로 `HttpProxy` 종류와 `scope=https`를 함께 기록합니다.

### 절대 프록시 URI

```text
http://proxy.example:8080
https://secure-proxy.example:8443
socks5://proxy.example:1080
```

포트를 생략한 URI에는 다음 기본 포트를 적용합니다.

| 스킴 | 기본 포트 |
|---|---:|
| `http` | 80 |
| `https` | 443 |
| `socks`, `socks4`, `socks5` | 1080 |

### 단일 엔드포인트

```text
proxy.example:8080
[2001:db8::10]:8080
```

단일 `host:port`는 범위 전체에 적용되는 일반 HTTP 프록시 후보로 해석합니다.

## 호스트 정규화

- DNS 이름은 소문자로 변환
- 마지막 점 제거
- 국제화 도메인은 IDNA ASCII로 변환
- IPv4·IPv6는 운영체제 표준 문자열로 정규화
- IPv6와 포트 조합은 반드시 `[IPv6]:port` 형식 사용
- 포트는 1~65535만 허용

예:

```text
Example.COM.:8080
  → example.com:8080

münich.example:8080
  → xn--mnich-kva.example:8080

[2001:0db8:0:0:0:0:0:1]:8080
  → [2001:db8::1]:8080
```

## 안전하지 않은 입력

다음 입력은 사용할 수 있는 엔드포인트로 만들지 않습니다.

- 사용자 이름이나 암호가 포함된 URI
- 경로, query 또는 fragment
- `@`가 포함된 엔드포인트
- 0·65536 이상·문자가 포함된 포트
- 대괄호 없이 포트가 붙은 IPv6
- 지원하지 않는 URI 스킴
- 줄바꿈·탭·NUL 등 제어 문자
- 4096자를 초과하는 입력
- 32개를 초과하는 세미콜론 구간

예:

```text
http://user:password@proxy.example:8080
https://proxy.example:8443/private
PROXY proxy.example:0
PROXY 2001:db8::1:8080
ftp://proxy.example:21
```

오류 메시지는 원문 호스트나 자격증명 문자열을 반사하지 않고 고정 코드와 일반 설명만 제공합니다.

## 부분 성공

다음처럼 일부 구간만 잘못된 경우 유효한 지시문은 순서를 유지합니다.

```text
PROXY valid-a.example:8080; UNKNOWN invalid; DIRECT; PROXY valid-b.example:3128
```

결과:

```text
Status=PartialSuccess
유효 지시문=3
오류 구간=1
```

후속 UI와 경로 분석은 제외된 구간이 있다는 경고를 반드시 표시해야 합니다. 해석되지 않은 구간을 임의 프록시나 `DIRECT`로 추정하지 않습니다.

## 중복 처리

종류, 범위, 정규화 호스트와 포트가 같은 지시문은 한 번만 유지합니다.

```text
PROXY Example.COM.:8080
proxy example.com:8080
```

위 두 항목은 하나의 프록시 후보와 `DUPLICATE_DIRECTIVE` 경고로 정리됩니다. `http=`와 `https=`처럼 범위가 다른 동일 호스트는 서로 다른 정책이므로 각각 유지합니다.

## 개인정보 경계

경로 확인에는 실제 호스트가 필요하므로 `ProxyRouteDirective.Host`는 메모리에서만 유지합니다. 다음 보호를 적용합니다.

- 기본 JSON 직렬화에서 `Host` 제외
- `ToString()`은 원문 호스트 대신 SHA-256 앞 10자의 지문 사용
- 디버거 표시도 마스킹된 문자열 사용
- 결과·경고 메시지에 원문 세그먼트를 포함하지 않음
- 공개 보고서에는 호스트 지문, 종류, 범위와 포트만 사용

표시 예:

```text
HttpProxy · 범위 all · 호스트 지문 3a40f29c11 · 포트 8080
```

짧은 지문은 비밀번호나 인증 토큰이 아니며 동일 후보 비교를 위한 비가역 표시값입니다. 외부에 공유할 때도 회사 정책을 따릅니다.

## 통신 경계

`ProxyRouteDirectiveParser`는 문자열 처리와 SHA-256만 수행합니다.

다음 작업은 하지 않습니다.

- DNS 조회
- TCP 연결
- HTTP/HTTPS 요청
- PAC/WPAD 다운로드 또는 실행
- 프록시 인증
- 프록시 서버 관리 API
- 외부 분석 API 또는 AI·로컬 AI
- 텔레메트리·결과 업로드

실제 DNS와 Windows 최적 경로 확인은 사용자가 경로 분석을 명시적으로 실행하는 별도 단계에서만 수행합니다.

## 자동 검증

Core SelfTest는 다음을 확인합니다.

- PAC fallback 순서
- Windows 프로토콜별 매핑
- HTTP·HTTPS·SOCKS URI 기본 포트
- IPv6 대괄호와 canonical 형식
- IDN ASCII 정규화
- 일부 오류가 있는 부분 성공
- 정규화 후 중복 제거
- 자격증명·경로·포트·IPv6 형식 거부
- 제어 문자·길이·구간 수 제한
- `ToString()`과 기본 JSON에서 원문 호스트 비노출
- 빈 입력의 `Empty` 처리
