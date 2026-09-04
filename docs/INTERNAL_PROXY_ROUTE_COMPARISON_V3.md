# 내부 DIRECT–프록시 로컬 경로 비교

이 기능은 현재 PC에서 다음 두 구간에 선택되는 Windows 로컬 인터페이스를 비교합니다.

```text
내부 DIRECT 측정 대상까지의 로컬 경로
프록시 엔드포인트까지의 로컬 경로
```

프록시 서버 이후 외부 사이트까지의 경로는 비교하지 않습니다.

## 비교 결과

```text
Ready
Incomplete
Ambiguous
Diverged
```

| 상태 | 의미 |
|---|---|
| `Ready` | 내부와 모든 분석 프록시 후보의 단일 인터페이스 지문이 확인됐고 서로 같음 |
| `Diverged` | 양쪽의 단일 인터페이스 지문이 확인됐지만 서로 다름 |
| `Ambiguous` | 내부 또는 프록시 후보가 여러 인터페이스로 나뉘거나 같은 지문의 메타데이터가 충돌함 |
| `Incomplete` | 내부 경로, 프록시 경로 또는 fallback 후보 일부의 근거가 부족함 |

`Ready`는 비교 수행 준비와 로컬 인터페이스 일치를 뜻합니다. 내부 서비스, 프록시 또는 인터넷 성능이 정상이라는 뜻이 아닙니다.

`Diverged`도 비교 근거가 충분한 결론입니다. 단순 실패가 아니라 두 경로가 다른 로컬 인터페이스를 사용한다는 의미입니다.

## 입력 근거

### 내부 DIRECT 경로

기존 `DestinationRouteEvidence`를 사용합니다.

- IPv4·IPv6 주소별 Windows 최적 인터페이스
- 단일 선택 인터페이스
- 인터페이스 category·virtual·VPN·Up·default gateway
- 현재 Native WLAN과 상관 분석

`Success` 또는 단일 인터페이스가 확정된 `PartialSuccess`는 비교에 사용할 수 있습니다. `PartialSuccess`는 별도 경고로 남깁니다.

`MultipleInterfaces`는 `Ambiguous`입니다.

### 프록시 엔드포인트 경로

`ProxyEndpointRouteAnalysisResult`를 사용합니다.

비교 가능한 조건:

```text
Status=Success
AnalyzedEndpointCount > 0
모든 분석 후보가 route success
모든 성공 후보에 interface fingerprint 존재
모든 성공 후보의 fingerprint가 하나
범주·VPN·가상·Up·gateway 메타데이터 일치
```

일부 fallback 후보의 경로가 실패한 `PartialSuccess`는 `Incomplete`입니다. 실제 프록시 연결을 시험하지 않으므로 확인되지 않은 fallback 후보를 무시하고 같은 경로라고 결론 내리지 않습니다.

`MultipleInterfaces`는 `Ambiguous`입니다.

## `DIRECT` 외부 경로

외부 대상의 프록시 결정이 `DirectPathSelected`이면 비교할 프록시 엔드포인트가 없습니다.

```text
Status=Incomplete
ProxyDirectPathSelected=true
```

이는 오류가 아니라 이 비교의 대상인 “프록시까지의 경로”가 없다는 의미입니다. 필요하면 별도의 내부 DIRECT–외부 DIRECT 비교를 수행해야 합니다.

프록시 뒤에 `DIRECT` fallback이 있는 경우에는 프록시 로컬 인터페이스 비교가 가능하지만 다음 경고를 유지합니다.

```text
이 비교는 프록시 연결 실패와 실제 DIRECT 전환을 시험하지 않습니다.
```

## 현재 WLAN과의 관계

유효한 Native WLAN GUID가 있으면 SHA-256 기반 짧은 인터페이스 지문으로 변환해 양쪽 로컬 경로와 비교합니다.

```text
InternalInterface.MatchesExpectedWlan
ProxyInterface.MatchesExpectedWlan
```

가능한 결과:

```text
내부=true, 프록시=true
  → 두 경로가 모두 현재 연결 Wi-Fi를 사용

내부=true, 프록시=false
  → 내부는 현재 Wi-Fi, 프록시는 유선·VPN·다른 인터페이스일 가능성

내부=false, 프록시=false
  → 두 경로 모두 현재 Native WLAN과 다른 인터페이스

null
  → 현재 WLAN GUID를 확인하지 못해 판정하지 않음
```

WLAN GUID가 없어도 내부·프록시 인터페이스 지문 자체가 안전하게 확인되면 `Ready` 또는 `Diverged` 비교는 가능합니다. WLAN 일치 여부만 `null`로 남깁니다.

## VPN·터널·가상 인터페이스

결과에는 다음 집계가 포함됩니다.

```text
AnyVirtualInterface
AnyVpnOrTunnelInterface
```

VPN·터널 또는 가상 인터페이스가 포함되면 Windows 인터페이스 메트릭, 정적 경로, 보안 에이전트와 split tunneling 정책을 함께 확인해야 합니다.

다음 사례는 `Diverged`의 대표 예입니다.

```text
내부 DIRECT → 현재 물리 Wi-Fi
프록시 endpoint → 회사 VPN tunnel
```

이 결과만으로 VPN 장애 또는 프록시 장애를 확정하지 않습니다.

## 모호한 메타데이터

같은 interface fingerprint가 반복되지만 다음 값이 후보 사이에서 다르면 `Ambiguous`입니다.

- 인터페이스 category
- virtual 여부
- VPN 여부
- operational Up 여부
- default gateway 여부

이는 결과 모델 조립, Windows 상태 전환 또는 수집 시점 차이로 인한 충돌일 수 있으므로 첫 값을 임의 선택하지 않습니다.

## 개인정보 경계

`InternalProxyRouteComparisonResult`에는 다음 원문을 저장하지 않습니다.

- 내부 대상 주소·호스트·URL
- 프록시 호스트
- 인터페이스 전체 GUID
- 인터페이스 표시 이름과 설명
- IP·MAC·게이트웨이·DNS
- SSID·BSSID
- 이메일·사용자 경로

유지하는 값:

```text
interface fingerprint
category
virtual·VPN·Up·default gateway 여부
현재 WLAN 지문과 일치 여부
상태·집계·고정 메시지·고정 한계
```

입력 객체의 자유형 메시지와 경고를 결과에 복사하지 않고, 비교 엔진이 생성한 고정 문장만 사용합니다.

## 통신 경계

비교 함수는 이미 수집된 로컬 근거 객체만 읽는 순수 Core 로직입니다.

수행하지 않는 작업:

- DNS 조회
- 라우팅 API 재호출
- TCP 연결
- 프록시 인증
- HTTP·HTTPS 요청
- PAC·WPAD
- 프록시 서버 API
- AI·로컬 AI·외부 분석 API
- 텔레메트리·자동 오류 전송
- 결과 업로드

DNS와 Windows 경로 판정은 사용자가 앞 단계의 경로 분석을 실행한 경우에만 수행됩니다.

## 자동 검증

SelfTest는 다음을 확인합니다.

1. 같은 현재 Wi-Fi 인터페이스의 `Ready`
2. 내부 Wi-Fi와 프록시 VPN tunnel의 `Diverged`
3. 프록시 후보가 무선·유선으로 나뉜 `Ambiguous`
4. 내부 IPv4·IPv6가 여러 인터페이스인 `Ambiguous`
5. 외부 DIRECT 또는 프록시 부분 성공의 `Incomplete`
6. 내부 `PartialSuccess`지만 단일 인터페이스가 확정된 비교
7. 같은 지문에 상충하는 category의 `Ambiguous`
8. 현재 WLAN GUID가 없어도 지문 비교 유지
9. 결과 JSON에 원문 GUID·이름·설명이 남지 않음

## 실제 환경 검증

1. 내부 승인 파일 서버 URL을 DIRECT 대상으로 설정합니다.
2. 외부 승인 URL에 대해 실제 Windows 프록시 결정을 수집합니다.
3. 내부 대상과 적용 프록시 후보의 로컬 경로를 사용자가 실행해 확인합니다.
4. 비교 결과와 양쪽 interface fingerprint·category를 확인합니다.
5. 회사 VPN 연결 전후 결과를 비교합니다.
6. 유선 연결·해제 전후 결과를 비교합니다.
7. 내장·USB Wi-Fi가 함께 있을 때 현재 Native WLAN 일치 여부를 확인합니다.
8. 프록시 fallback 후보들이 다른 로컬 인터페이스로 나뉘는지 확인합니다.
9. IPv4·IPv6가 서로 다른 인터페이스를 선택하는지 확인합니다.
10. 결과와 보고서에 실제 내부 URL·프록시 호스트·GUID·IP가 남지 않는지 확인합니다.

비교 결과는 Windows 로컬 경로의 근거이며 실제 서비스 품질 판정에는 내부·외부 처리량, HTTP 상태, 프록시 인증, WLAN RSSI·PHY·로밍과 장비 로그를 함께 사용합니다.
