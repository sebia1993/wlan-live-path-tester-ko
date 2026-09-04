# 내부 DIRECT와 프록시 엔드포인트 로컬 경로 비교

`경로 비교` 탭은 현재 앱 실행의 라우팅 근거 이력에서 다음 목적별 가장 최근 결과를 선택해 비교합니다.

- 내부 DIRECT 측정 대상
- 프록시 엔드포인트
- 외부 사이트 참고 경로

이 기능은 이미 수집된 메모리 이력만 사용합니다. 비교 버튼을 눌러도 DNS, HTTP, PAC/WPAD, 외부 API 또는 업로드가 새로 발생하지 않습니다.

## 비교 목적

내부 다운로드와 외부 프록시 경로가 서로 다른 로컬 인터페이스를 사용하면 단순 속도 비교가 왜곡될 수 있습니다.

```text
내부 DIRECT
PC → Wi-Fi → 사내 내부 서버

외부 프록시
PC → Wi-Fi 또는 유선/VPN → 회사 프록시 → 외부 사이트
```

예를 들어 WLAN 상태는 내장 Wi-Fi에서 읽지만 내부 서버는 유선으로, 프록시는 VPN으로 접속하면 내부·외부 속도 차이를 무선 품질 문제로 단정할 수 없습니다.

## 입력 근거

비교 엔진은 `RouteEvidenceResultHistory`의 최근 결과 최대 12건 중 목적별 최신 결과만 사용합니다.

```text
InternalDirectTarget → 가장 최근 1건
ProxyEndpoint → 가장 최근 1건
ExternalTargetReference → 가장 최근 1건
```

오래된 실패 결과보다 이후에 수집한 최신 정상 결과를 우선합니다. 앱을 종료하면 메모리 이력은 사라집니다.

## 전체 상태

| 상태 | 의미 |
|---|---|
| `Ready` | 내부·프록시 필수 근거가 있고 단일 인터페이스 비교가 가능함 |
| `Incomplete` | 내부 또는 프록시 근거가 없거나 WLAN ID 비교가 불가능함 |
| `Ambiguous` | IPv4·IPv6·복수 주소가 서로 다른 인터페이스를 선택하거나 단일 경로가 확정되지 않음 |
| `Diverged` | 내부·프록시 또는 현재 WLAN 사이의 인터페이스 차이가 확인됨 |

우선순위는 `Ambiguous` → `Diverged` → `Incomplete` → `Ready`입니다. 복수 인터페이스 상태는 단일 경로를 임의로 고르는 것보다 더 중요한 제한으로 처리합니다.

## 주요 판정 코드

### 필수 근거

- `INTERNAL_ROUTE_NOT_MEASURED`
- `PROXY_ROUTE_NOT_MEASURED`
- `INTERNAL_ROUTE_UNAVAILABLE`
- `PROXY_ROUTE_UNAVAILABLE`
- `INTERNAL_ROUTE_AMBIGUOUS`
- `PROXY_ROUTE_AMBIGUOUS`

### 현재 WLAN NIC 비교

- `INTERNAL_MATCHES_CONNECTED_WLAN`
- `PROXY_MATCHES_CONNECTED_WLAN`
- `INTERNAL_DIFFERS_FROM_CONNECTED_WLAN`
- `PROXY_DIFFERS_FROM_CONNECTED_WLAN`
- `INTERNAL_WLAN_CORRELATION_UNAVAILABLE`
- `PROXY_WLAN_CORRELATION_UNAVAILABLE`

### 내부·프록시 상호 비교

- `INTERNAL_AND_PROXY_SHARE_INTERFACE`
- `INTERNAL_AND_PROXY_USE_DIFFERENT_INTERFACES`

### VPN·가상 경로

- `INTERNAL_USES_VPN_OR_TUNNEL`
- `PROXY_USES_VPN_OR_TUNNEL`
- `INTERNAL_USES_VIRTUAL_INTERFACE`
- `PROXY_USES_VIRTUAL_INTERFACE`

### 외부 참고값

- `EXTERNAL_REFERENCE_IS_NOT_PROXY_PATH`

## 판단 예

### Ready + 같은 지문

내부 DIRECT와 프록시 엔드포인트가 같은 인터페이스 지문을 선택하고 둘 다 현재 Native WLAN과 일치합니다.

이 경우 PC의 로컬 출구 NIC는 같다고 볼 수 있습니다. 그래도 외부 경로에는 프록시 이후 회선·캐시·외부 사이트 구간이 추가되므로 속도가 같아야 한다는 뜻은 아닙니다.

### Diverged + 내부 Wi-Fi / 프록시 VPN

내부 서버는 현재 Wi-Fi를 사용하지만 프록시까지의 Windows 최적 경로는 VPN·터널을 선택합니다.

외부 측정 결과에는 다음 구간이 포함될 수 있습니다.

```text
Wi-Fi 물리 구간
VPN 또는 보안 터널
회사 프록시
인터넷 회선
외부 CDN 또는 사이트
```

### Diverged + 내부 유선 / 프록시 Wi-Fi

내부 다운로드는 유선으로 나가고 외부 프록시 경로만 Wi-Fi를 사용할 수 있습니다. 내부 결과를 무선 기준값으로 사용하지 않습니다.

### Incomplete + 외부 참고만 존재

외부 사이트 주소에 대한 직접 경로는 확인했지만 프록시 엔드포인트를 확인하지 않은 상태입니다. 회사 프록시 환경에서는 실제 외부 HTTP 연결 경로 비교가 불완전합니다.

### Ambiguous

IPv4와 IPv6 또는 복수 DNS 응답이 서로 다른 인터페이스를 선택합니다. 실제 애플리케이션이 어떤 주소를 사용할지 정해지기 전에는 단일 경로로 확정하지 않습니다.

## 개인정보 경계

비교 모델에는 다음 원문이 들어가지 않습니다.

- 목적지 IP 주소
- 게이트웨이·DNS 서버 주소
- MAC 주소
- 인터페이스 이름과 설명
- 전체 인터페이스 GUID
- 내부·외부 URL과 프록시 호스트

비교 Point에는 다음 값만 유지합니다.

- 목적
- 수집 시각
- 라우팅 상태
- WLAN 상관 상태
- 인터페이스 SHA-256 앞 10자리 지문
- 인터페이스 범주
- VPN·가상 여부
- 경고 개수

## 권장 사용 순서

1. `라우팅 근거` 탭에서 현재 내부 URL을 내부 DIRECT 목적으로 확인합니다.
2. 운영 정책상 확인 가능한 프록시 호스트 또는 `host:port`를 프록시 엔드포인트 목적으로 확인합니다.
3. 필요하면 외부 사이트 참고 경로를 별도로 확인합니다.
4. `경로 비교` 탭에서 현재 이력을 비교합니다.
5. `Ready`, `Incomplete`, `Ambiguous`, `Diverged`와 판정 근거를 확인합니다.
6. 내부·외부 다운로드와 반복 측정 결과를 같은 인터페이스 조건에서 비교합니다.
7. 필요한 경우 `라우팅 보고서`를 생성해 구조화 결과를 저장합니다.

## 판단 한계

- 비교 결과는 Windows 로컬 인터페이스 근거이며 실제 TCP 연결 성공을 보장하지 않습니다.
- 프록시 이후 서버·회선·캐시·정책 상태는 포함하지 않습니다.
- VPN split tunnel, Windows Filtering Platform, 투명 프록시와 보안 에이전트 정책은 이 비교만으로 완전히 확인할 수 없습니다.
- 프록시 엔드포인트를 알 수 없거나 정책상 입력할 수 없으면 외부 로컬 경로 비교는 `Incomplete`로 남는 것이 정상입니다.
