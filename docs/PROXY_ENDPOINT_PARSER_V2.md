# 프록시 엔드포인트 문자열 해석

이 기능은 Windows 수동 프록시 서버 목록과 PAC·WPAD·WinHTTP 자동 프록시 결과에 들어 있는 프록시 후보를 결정론적으로 해석합니다.

목적은 현재 PC가 실제로 연결 대상으로 선택할 수 있는 프록시 호스트를 추출해 이후 로컬 DNS·라우팅 인터페이스 분석에 전달하는 것입니다. 프록시 서버에 로그인하거나 서버 내부 CPU·세션·정책·캐시 상태를 조회하지 않습니다.

## 지원 입력

### 자동 프록시 결과

```text
PROXY proxy-a.example:8080; HTTPS proxy-b.example:8443; DIRECT
SOCKS5 [2001:db8::1]:1080; DIRECT
PROXY proxy-a.example:8080 DIRECT
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

프록시와 `DIRECT`의 입력 순서를 보존합니다. 프록시 후보가 `DIRECT`보다 먼저면 `ProxyWithDirectFallback`, `DIRECT`가 먼저면 `DirectWithProxyAlternatives`로 구분합니다.

`DIRECT`가 먼저인 목록을 프록시 우선 경로로 추정하지 않습니다.

### 수동 프록시 서버 목록

```text
http=proxy-http.example:8080;https=proxy-https.example:8443
all=common-proxy.example:3128
*=common-proxy.example:3128
```

대상 URL이 주어지면 해당 스킴과 정확히 일치하는 매핑 또는 `all`·`*` 매핑만 선택합니다.

예를 들어 다음 설정에서 HTTPS 대상에는 적용 가능한 프록시가 없습니다.

```text
http=proxy-http.example:8080
```

```text
대상: https://download.example/file.bin
결과: Unknown
```

`http=` 값을 HTTPS 프록시로 임의 추정하지 않습니다. 회사별 정책과 Windows 구성에 따라 의미가 달라질 수 있는 교차 스킴 fallback을 프로그램이 만들어내지 않기 위한 경계입니다.

대상 URL을 전달하지 않으면 모든 수동 매핑을 보존하고 각 후보의 `AppliesToScheme`에 적용 스킴을 기록합니다.

### 명시적 프록시 URI

```text
http://proxy.example:8080
https://secure-proxy.example:8443
socks://socks-proxy.example:1080
socks4://socks4-proxy.example:1080
socks5://socks5-proxy.example:1080
proxy://proxy.example:8080
```

명시적 URI에 포트가 없을 때만 다음 기본 포트를 사용합니다.

| URI 스킴 | 기본 포트 |
|---|---:|
| `http`, `proxy` | 80 |
| `https` | 443 |
| `socks`, `socks4`, `socks5` | 1080 |

스킴 없이 `proxy.example`만 주어진 경우에는 전송 유형과 포트를 추정하지 않습니다.

```text
Transport=Unspecified
Port=null
```

## 지원 호스트 형식

- DNS 호스트 이름
- 국제화 도메인 이름(IDN)
- IPv4
- 포트 없는 IPv6 literal
- 포트가 있는 대괄호 IPv6

예:

```text
proxy.example:8080
192.0.2.10:8080
2001:db8::2
[2001:db8::1]:1080
bücher.example:8080
```

IDN은 ASCII punycode로 정규화합니다. 대소문자와 DNS 마지막 점을 정규화한 뒤 중복을 제거합니다.

대괄호가 없는 유효 IPv6 문자열은 포트가 없는 IPv6 literal로 해석합니다. IPv6에 포트를 지정하려면 반드시 다음 형식을 사용합니다.

```text
[IPv6-address]:port
```

## 거부 입력

다음 값은 프록시 후보로 사용하지 않습니다.

- 사용자 이름·비밀번호가 포함된 URI 또는 authority
- 경로가 포함된 프록시 URI
- query 또는 fragment
- 1~65535 범위를 벗어난 포트
- 공백·제어 문자가 포함된 엔드포인트
- 잘못된 IPv6 대괄호·suffix
- wildcard 호스트
- 지원하지 않는 프록시 URI 스킴
- 상대 대상 URI
- HTTP·HTTPS가 아닌 대상 URL

예:

```text
http://user:password@proxy.example:8080
http://proxy.example/private/path
https://proxy.example:443?token=value
proxy.example:0
proxy.example:65536
[2001:db8::1]8080
ftp://proxy.example
```

거부 경고에는 원문 엔드포인트를 다시 출력하지 않습니다. 항목 순번과 고정된 실패 이유만 기록해 자격 증명이나 내부 호스트가 UI·로그·보고서로 반사되지 않게 합니다.

## 결과 모델

```text
ProxyEndpointParseResult
├─ InputPresent
├─ SourceKind
├─ Decision
├─ TargetScheme
├─ Endpoints[]
├─ DirectSequences[]
├─ DirectFallback
├─ ParsedEndpointCount
├─ IgnoredEndpointCount
├─ DuplicateEndpointCount
├─ DuplicateDirectCount
├─ RejectedTokenCount
├─ TruncatedTokenCount
├─ Warnings[]
└─ Errors[]
```

`SourceKind`:

```text
Unknown
ManualServerList
AutoProxyResult
Mixed
```

`Decision`:

```text
Unknown
Direct
Proxy
ProxyWithDirectFallback
DirectWithProxyAlternatives
```

## 순서 보존

각 프록시 후보와 첫 `DIRECT`의 `Sequence`를 유지합니다.

```text
PROXY first.example:8080; DIRECT; PROXY later.example:8081
```

이 입력은 첫 프록시가 우선이고 `DIRECT`가 그 뒤에 있으므로 `ProxyWithDirectFallback`입니다. `DIRECT` 뒤의 후보를 임의로 앞으로 이동하지 않습니다.

다음 입력은 `DIRECT`가 먼저입니다.

```text
DIRECT; PROXY later.example:8080
```

결과는 `DirectWithProxyAlternatives`이며 프록시 기본 경로로 표시하지 않습니다.

## 중복 제거

다음 값이 같으면 첫 후보만 유지합니다.

- 적용 대상 스킴
- 프록시 전송 유형
- 정규화 호스트
- 포트

대상 URL이 주어진 경우 `all=`과 정확한 대상 스킴이 같은 실제 엔드포인트를 가리키면 첫 후보로 통합합니다.

중복 `DIRECT`도 첫 순서만 유지하고 별도 집계를 남깁니다.

## 안전 한도

| 항목 | 한도 |
|---|---:|
| 입력 문자열 | 16 KiB |
| 해석 토큰 | 64개 |
| 선택 프록시 후보 | 32개 |

한도를 넘는 값은 사용하지 않고 집계와 경고를 남깁니다. 후보 한도 경고는 반복 생성하지 않습니다.

## 개인정보 경계

`ProxyEndpointCandidate.Host`는 이후 사용자가 명시적으로 실행한 로컬 DNS·라우팅 판정을 위해 메모리에만 유지하는 내부 값입니다. 보고서·화면에는 `SafeLabel`을 사용합니다.

```text
프록시 후보 1 · https 대상 · HTTPS proxy · host#1a2b3c4d5e · port 8443
```

`HostFingerprint`는 정규화 호스트의 SHA-256 앞 10자리 소문자 16진수입니다.

- 같은 호스트의 대소문자 차이는 같은 지문
- DNS 마지막 점 차이는 같은 지문
- 원문 호스트를 복원하기 위한 암호화가 아님
- 공개 보고서에서 후보를 구분하기 위한 짧은 비식별 라벨

화면·JSON·CSV·HTML에서 프록시 호스트 원문 대신 지문과 포트만 사용하는 것이 원칙입니다.

## 네트워크 통신 경계

`ProxyEndpointParser.Parse`는 문자열만 처리하는 순수 로컬 함수입니다.

수행하지 않는 작업:

- DNS 조회
- TCP 연결
- 프록시 인증
- HTTP·HTTPS 요청
- PAC URL 다운로드
- WPAD 탐색
- 프록시 서버 API 호출
- 프록시 내부 상태 조회
- AI·로컬 AI·외부 분석 API
- 텔레메트리·자동 오류 전송
- 결과 업로드

PAC·WPAD 또는 Windows 프록시 API가 이미 반환한 문자열을 입력으로 받을 뿐입니다.

## 이후 라우팅 분석

후속 기능은 사용자가 명시적으로 `로컬 경로 확인`을 실행했을 때만 각 선택 후보의 호스트를 운영체제 DNS로 확인하고 Windows 최적 로컬 인터페이스를 판정합니다.

분석 범위:

```text
PC → 프록시 엔드포인트까지의 로컬 경로
```

분석하지 않는 범위:

```text
프록시 서버 내부 처리
프록시 → 외부 사이트 구간
회사 인터넷 회선 전체
프록시 클러스터 상태
정책·인증·캐시 서버 내부 상태
```

따라서 프록시까지의 로컬 인터페이스가 Wi-Fi인지 VPN·터널인지 확인할 수 있지만, 그 결과만으로 프록시 서버 장애를 확정하지 않습니다.

## 자동 검증

SelfTest는 다음을 확인합니다.

- 빈 입력과 `DIRECT`
- 자동 `PROXY`·`HTTPS`·`SOCKS5`와 route 순서
- 세미콜론·공백 구분
- 수동 `http=`·`https=` 정확 스킴 선택
- 교차 스킴 fallback 금지
- `all=`·`*=` 공통 후보
- 명시 URI와 기본 포트
- DNS·IDN·IPv4·IPv6
- 자격 증명·경로·query·잘못된 포트 거부
- 거부 경고의 원문 비반사
- 프록시·DIRECT 중복 제거
- `DIRECT` 우선 순서 보존
- 혼합 입력
- 16 KiB·64 토큰·32 후보 한도
- 안전 라벨의 호스트 원문 비노출
- 상대 URI·FTP 대상 거부
