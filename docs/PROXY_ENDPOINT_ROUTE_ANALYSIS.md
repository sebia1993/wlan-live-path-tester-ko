# 프록시 엔드포인트 Windows 로컬 경로 분석

이 기능은 로컬에서 해석한 프록시 지시문 중 실제 비‑DIRECT 후보에 대해 Windows가 선택하는 최적 로컬 인터페이스를 확인합니다. 프록시 서버 내부 상태나 관리 화면에는 접근하지 않습니다.

## 실행 경계

프록시 문자열 해석 자체는 네트워크 요청이 없습니다. 사용자가 프록시 경로 분석을 명시적으로 실행한 경우에만 다음 순서로 처리합니다.

```text
수동 프록시 또는 PAC/WPAD 판정 문자열
  ↓ 로컬 문자열 해석
PROXY / HTTPS / SOCKS / DIRECT 후보
  ↓
DIRECT
  → DNS 없음
  → 경로 조회 없음

비-DIRECT 후보
  → 호스트가 IP이면 DNS 없음
  → DNS 이름이면 Windows DNS 주소 확인
  → 주소별 Windows 최적 로컬 인터페이스 확인
  → 현재 Native WLAN 인터페이스와 정확한 GUID 상관분석
```

HTTP 다운로드, 프록시 로그인, TCP 연결 또는 프록시 관리 API 호출은 수행하지 않습니다.

## 결과 상태

### 전체 분석 상태

| 상태 | 의미 |
|---|---|
| `Success` | 모든 확인 대상 프록시 후보의 단일 Windows 경로 확인 성공 |
| `PartialSuccess` | 일부 후보·주소 계열만 확인, 파서 일부 오류, 후보 상한 초과 또는 DIRECT fallback 존재 |
| `DirectOnly` | DIRECT만 있어 DNS·프록시 경로 조회를 하지 않음 |
| `Empty` | 입력 없음 |
| `InvalidInput` | 사용할 수 있는 프록시 또는 DIRECT 지시문 없음 |
| `Canceled` | 사용자 취소 후 완료된 후보만 유지 |
| `Failed` | 확인 대상 프록시 경로를 하나도 확정하지 못함 |

### 후보별 상태

```text
Direct
Success
PartialSuccess
MultipleInterfaces
ResolutionFailed
RouteNotFound
Canceled
Failed
```

`Success`는 Windows 최적 경로 조회가 성공했다는 뜻입니다. 해당 경로가 현재 WLAN과 같다는 뜻은 아니므로 `WlanCorrelationStatus`를 별도로 확인합니다.

## WLAN 상관 상태

```text
Matched
DifferentInterface
WlanIdentityUnavailable
RouteInterfaceUnavailable
NotEvaluated
```

예를 들어 프록시 엔드포인트까지의 Windows 최적 경로가 회사 VPN·유선 NIC를 선택하면 경로 조회 자체는 성공할 수 있지만 현재 WLAN과의 상관 상태는 `DifferentInterface`입니다.

## DIRECT 처리

`DIRECT`는 프록시 서버 주소가 아닙니다. 다음 작업을 하지 않습니다.

- DNS 조회
- 프록시 호스트 경로 조회
- 임의 외부 대상 선택
- 인터넷 연결 확인

결과에는 다음 설명만 남깁니다.

```text
DIRECT 지시문은 프록시 엔드포인트가 아니므로 DNS 또는 프록시 경로 조회를 수행하지 않았습니다.
```

따라서 PAC 결과가 `DIRECT` 하나뿐인 경우 `DirectOnly`입니다.

## fallback 순서

다음 PAC 결과를 예로 들면 순서를 그대로 유지합니다.

```text
PROXY proxy-a.example:8080;
HTTPS proxy-b.example:8443;
DIRECT
```

1. 첫 번째 프록시 후보
2. 두 번째 프록시 후보
3. DIRECT fallback

경로 분석은 PAC 실행 성공 여부나 실제 요청 시의 프록시 장애 fallback을 재현하지 않습니다. 각 후보까지 현재 PC의 로컬 경로를 독립적으로 확인하는 기능입니다.

## 후보 수 제한

한 번의 사용자 실행에서 DNS·라우팅 확인을 수행하는 비‑DIRECT 후보는 기본 8개, 최대 16개로 제한합니다.

상한을 초과하면:

- 앞의 후보부터 순서대로 확인
- 나머지 비‑DIRECT 후보는 조회하지 않음
- `WasTruncated=true`
- 전체 상태 `PartialSuccess`
- 뒤에 있는 DIRECT 지시문은 계속 결과에 유지

파서 자체는 최대 32개 세그먼트를 허용하므로 분석기 상한과 입력 방어 상한을 분리합니다.

## 사용자 취소

사용자가 취소하면 현재 후보 또는 다음 후보부터 중단합니다.

- 이미 완료한 후보 결과 유지
- `Status=Canceled`
- 이후 프록시 후보와 DIRECT는 처리하지 않음
- 취소 뒤 새로운 DNS·경로 조회 없음

Windows DNS 호출이 이미 진행 중이라면 운영체제가 취소를 반영하는 시점까지 짧은 지연이 있을 수 있습니다.

## 개인정보 경계

실제 프록시 호스트는 DNS와 Windows 경로 확인에 필요하므로 분석 실행 중 메모리에만 사용합니다.

공개 가능한 기본 분석 모델에는 다음 값만 남깁니다.

- 프록시 종류
- 적용 범위
- 포트
- 호스트 SHA-256 앞 10자 지문
- 선택 인터페이스 SHA-256 앞 10자 지문
- 인터페이스 범주
- 인터페이스 운영 상태
- WLAN 상관 상태
- 고정된 일반 설명

다음 원문은 기본 JSON 직렬화에서 제외합니다.

- 프록시 호스트
- 전체 인터페이스 GUID
- 인터페이스 이름과 설명
- 주소별 내부 라우팅 객체

`DestinationRouteEvidence` 원본은 앱 메모리에서 후속 로컬 비교에 사용할 수 있지만 `ProxyEndpointRouteEntry.RouteEvidence`에는 `JsonIgnore`를 적용합니다.

표시 예:

```text
HttpProxy
범위: https
호스트 지문: 3a40f29c11
포트: 8080
선택 인터페이스 지문: 9f21ad0c33
범주: Wireless
WLAN 상관: Matched
```

## 예외와 오류 메시지

DNS·경로 resolver가 예외를 던져도 원문 호스트나 예외 메시지를 공개 분석 결과에 그대로 복사하지 않습니다.

```text
프록시 후보의 로컬 Windows 경로 확인 중 예외가 발생했습니다.
원문 호스트와 예외 메시지는 결과에 포함하지 않았습니다.
```

주소 해석 실패·라우팅 미확정도 호스트를 포함하지 않는 고정 문구로 기록합니다.

## 통신 경계

이 기능이 사용자 실행 시 수행할 수 있는 네트워크 관련 동작은 다음뿐입니다.

- 프록시 DNS 이름의 Windows DNS 주소 확인
- Windows IP Helper API를 통한 최적 로컬 인터페이스 확인

다음 작업은 하지 않습니다.

- 프록시 서버 TCP 연결
- HTTP/HTTPS HEAD·GET
- 프록시 인증
- PAC/WPAD 파일 다운로드
- 프록시 서버 내부 API·로그·세션 조회
- 외부 분석 API 또는 AI·로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

실제 외부 속도 측정은 기존 사용자가 명시적으로 실행하는 별도 HEAD·GET 기능에서만 수행합니다.

## 자동 검증

Windows Smoke는 실제 DNS나 외부 프록시 대신 주입식 resolver를 사용해 다음을 검증합니다.

- DIRECT-only 입력에서 resolver 호출 0회
- 프록시 fallback 순서와 원문 호스트의 내부 전달 순서
- resolver에 전달되는 label의 호스트 원문 비노출
- 현재 WLAN과 같은 인터페이스의 `Matched`
- 프록시 실패와 DIRECT fallback의 `PartialSuccess`
- 비‑DIRECT 후보 상한과 DIRECT 보존
- 취소 이후 추가 후보 미조회
- 유선 경로 성공과 `DifferentInterface` 분리
- 기본 JSON에서 프록시 호스트·전체 GUID·인터페이스 이름·설명 비노출
- 잘못된 DNS 제한 시간·후보 상한 거부
