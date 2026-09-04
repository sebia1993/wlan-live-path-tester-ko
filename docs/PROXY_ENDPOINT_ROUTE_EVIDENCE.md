# 프록시 엔드포인트 로컬 경로 근거

이 기능은 프록시 문자열에서 선택된 엔드포인트가 현재 PC에서 어느 Windows 로컬 인터페이스로 라우팅되는지 확인합니다.

분석 범위는 다음과 같습니다.

```text
현재 Windows PC
  → 운영체제 DNS 확인
  → Windows 최적 로컬 인터페이스 선택
  → 프록시 엔드포인트 주소
```

프록시 TCP 연결, 인증 또는 다운로드를 실행하지 않습니다.

분석하지 않는 범위:

```text
프록시 서버 내부 CPU·메모리·세션·큐
프록시 정책·인증·캐시·클러스터 상태
프록시에서 외부 사이트까지의 경로
회사 인터넷 회선 전체 상태
실제 프록시 연결 성공 여부
```

## 실행 조건

이 분석은 사용자가 로컬 경로 확인을 명시적으로 실행했을 때만 수행합니다. `ProxyEndpointParser` 자체는 네트워크 요청을 하지 않습니다.

실제 Windows 구현은 각 프록시 후보에 대해 기존 `LocalRouteEvidenceReader`를 사용합니다.

```text
ProxyEndpointParser
  ↓ 선택된 후보와 route 순서
ProxyEndpointRouteAnalyzer
  ↓ 사용자가 실행한 경우에만
LocalRouteEvidenceReader
  ├─ IP literal이면 DNS 생략
  ├─ DNS 호스트이면 운영체제 DNS
  ├─ IPv4·IPv6별 Windows 최적 인터페이스
  └─ 현재 Native WLAN GUID와 상관 분석
```

## `DIRECT` 순서 경계

### `DIRECT`가 첫 적용 경로

```text
DIRECT; PROXY later.example:8080
```

결과:

```text
Status=DirectPathSelected
AnalyzedEndpointCount=0
SkippedAfterDirectCount=1
```

`DIRECT`가 먼저이므로 뒤 프록시 후보에 대한 DNS·라우팅 조회를 수행하지 않습니다. 불필요한 내부 DNS 조회를 만들지 않고 실제 route 순서를 존중합니다.

### 프록시 뒤 `DIRECT` fallback

```text
PROXY first.example:8080; HTTPS second.example:8443; DIRECT
```

`DIRECT` 앞의 프록시 후보만 순서대로 분석합니다.

```text
AnalyzedEndpointCount=2
DirectFallback=true
```

로컬 인터페이스 판정은 프록시에 실제로 연결하지 않으므로 두 프록시가 실패해 `DIRECT`로 전환됐는지는 확정하지 않습니다.

### `DIRECT` 뒤 프록시

```text
PROXY first.example:8080; DIRECT; PROXY after-direct.example:8081
```

`DIRECT` 뒤의 프록시는 현재 route 순서에서 분석 대상에서 제외합니다.

```text
ApplicableEndpointCount=2
AnalyzedEndpointCount=1
SkippedAfterDirectCount=1
```

## 결과 상태

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

| 상태 | 의미 |
|---|---|
| `InvalidInput` | 프록시 문자열 또는 대상 URL 오류로 분석 미실행 |
| `DirectPathSelected` | `DIRECT`가 첫 적용 경로여서 프록시 DNS·라우팅 미실행 |
| `NoApplicableEndpoint` | 현재 대상 스킴에 적용되는 프록시 후보 없음 |
| `Success` | 분석한 모든 프록시 후보의 로컬 경로 확인 |
| `PartialSuccess` | 일부 후보의 로컬 경로만 확인 |
| `MultipleInterfaces` | 모든 후보 경로는 확인됐지만 서로 다른 로컬 인터페이스가 선택됨 |
| `Canceled` | 사용자 요청으로 중단하고 완료된 후보만 유지 |
| `Failed` | 분석한 후보의 로컬 경로를 확인하지 못함 |

## 엔드포인트 결과

보고서·UI에 전달하는 `ProxyEndpointRouteEvidenceItem`은 다음 값만 포함합니다.

```text
Sequence
EndpointLabel
HostFingerprint
AppliesToScheme
Transport
Port
RouteStatus
WlanCorrelationStatus
SelectedInterfaceFingerprint
SelectedInterfaceCategory
SelectedInterfaceIsVirtual
SelectedInterfaceIsVpn
SelectedInterfaceIsUp
SelectedInterfaceHasDefaultGateway
ResolvedAddressCount
SuccessfulAddressCount
FailedAddressCount
Message
Warnings
```

프록시 호스트 원문과 인터페이스 GUID·이름·설명은 포함하지 않습니다.

## WLAN 상관 분석

Windows 최적 인터페이스와 관찰 시점의 Native WLAN GUID를 비교합니다.

```text
Matched
DifferentInterface
WlanIdentityUnavailable
RouteInterfaceUnavailable
NotEvaluated
```

두 fallback 프록시가 서로 다른 인터페이스를 사용하면 전체 상태를 `MultipleInterfaces`로 표시합니다. 이는 장애 확정이 아니라 Windows 라우팅 정책·VPN·유선·무선 우선순위를 확인해야 한다는 근거입니다.

## 개인정보 경계

파서는 실제 로컬 경로 판정을 위해 정규화된 프록시 호스트를 메모리에 유지합니다. 분석 결과 모델로 매핑할 때 다음 값을 제거합니다.

- 프록시 DNS 호스트 원문
- 로컬 인터페이스 GUID
- 인터페이스 표시 이름과 설명
- IP 주소
- MAC 주소
- URL
- 이메일
- Windows 사용자 경로

출력에는 다음 비식별 값만 유지합니다.

```text
프록시 host fingerprint
로컬 interface fingerprint
포트
인터페이스 범주
VPN·가상·Up·기본 게이트웨이 여부
```

라우팅 reader의 메시지나 경고에 원문 값이 들어 있어도 정확값 치환 후 기존 `SensitiveDataRedactor`를 적용합니다.

## DNS·통신 경계

### 사용자가 실행할 때 수행할 수 있는 작업

- 운영체제 DNS로 프록시 호스트 주소 확인
- IP literal이면 DNS 생략
- Windows 최적 로컬 라우팅 인터페이스 판정
- 현재 WLAN GUID와 로컬 결과 상관 분석

### 수행하지 않는 작업

- 프록시 TCP 연결
- HTTP `CONNECT`
- 프록시 인증
- HEAD·GET 다운로드
- PAC URL 다운로드
- WPAD 탐색
- 프록시 서버 API
- 프록시 내부 상태 조회
- 외부 분석 API·AI·로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

즉, 프록시까지의 **로컬 경로 선택**만 확인하며 프록시가 실제로 정상 응답한다는 뜻은 아닙니다.

## 자동 검증

WindowsSmoke는 실제 DNS·프록시 없이 주입식 route reader로 다음을 확인합니다.

1. `DIRECT` 우선이면 reader 호출 0회
2. `DIRECT` 앞 프록시만 입력 순서대로 분석
3. `DIRECT` 뒤 프록시 제외
4. 동일 WLAN 인터페이스 상관 `Matched`
5. 서로 다른 무선·유선 인터페이스의 `MultipleInterfaces`
6. 일부 성공과 일부 경로 없음의 `PartialSuccess`
7. reader 취소와 이전 결과 보존
8. 파서 오류·적용 후보 없음에서 reader 호출 0회
9. 프록시 호스트·인터페이스 GUID·이름·설명·이메일·IP·URL 비노출
10. 잘못된 DNS 제한 시간에서 reader 호출 0회

## 실제 환경 검증

1. 회사 외부 URL에 대해 Windows가 반환한 실제 프록시 결과를 수집합니다.
2. 프록시 후보 순서와 `DIRECT` 위치를 확인합니다.
3. 로컬 경로 분석을 실행합니다.
4. 각 후보가 현재 Wi-Fi, 유선, VPN 또는 터널 중 어느 경로인지 확인합니다.
5. IPv4·IPv6 결과가 다른 인터페이스를 선택하는지 확인합니다.
6. 내장·USB Wi-Fi가 함께 있을 때 현재 Native WLAN과 일치하는지 확인합니다.
7. VPN 연결·해제 전후 결과를 비교합니다.
8. 실제 프록시 호스트와 인터페이스 GUID가 보고서에 남지 않는지 확인합니다.

이 결과만으로 프록시 장애를 확정하지 않고 프록시 운영팀 지표·인증 오류·HTTP 상태·복수 외부 대상 측정과 함께 판단합니다.
