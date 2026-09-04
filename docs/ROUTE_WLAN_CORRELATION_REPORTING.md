# 라우팅–WLAN 상관분석 구조화 보고서

라우팅 근거 보고서 스키마 `1.1`은 Windows가 선택한 목적지별 인터페이스와 현재 연결된 Native WLAN 인터페이스의 비교 결과를 별도 구조화 필드로 저장합니다.

기존 보고서의 설명 문장과 경고도 유지하지만, 자동 분석에서는 문장을 해석하지 않고 다음 필드를 사용합니다.

```text
wlanCorrelationStatus
expectedWlanInterfaceFingerprint
wlanCorrelationMessage
selectedInterface.idFingerprint
selectedInterface.category
```

## 상태 값

| 값 | 의미 |
|---|---|
| `Matched` | Windows 최적 라우팅 인터페이스와 현재 연결 WLAN 인터페이스 ID가 같음 |
| `DifferentInterface` | 다른 물리 Wi-Fi, 유선, VPN·터널 또는 기타 인터페이스가 선택됨 |
| `WlanIdentityUnavailable` | 현재 Native WLAN 인터페이스 ID를 확인하지 못함 |
| `RouteInterfaceUnavailable` | 단일 라우팅 인터페이스 또는 유효한 라우팅 ID를 확인하지 못함 |
| `NotEvaluated` | 상관분석을 적용하지 않은 이전 또는 합성 결과 |

## 지문 필드

`expectedWlanInterfaceFingerprint`와 `selectedInterface.idFingerprint`는 인터페이스 GUID 원문이 아닙니다.

```text
정규화한 인터페이스 ID
  ↓
SHA-256
  ↓
앞 10자리 소문자 hex
```

보고서 생성기는 예상 WLAN 지문에 정확히 10자리 hex만 허용합니다. 길이가 다르거나 hex가 아닌 문자가 있으면 해당 필드를 `null`로 저장합니다. 따라서 호출자가 실수로 전체 GUID나 임의 문자열을 넣더라도 지문 필드가 원문 전달 통로가 되지 않습니다.

## JSON 예시

```json
{
  "status": "Success",
  "wlanCorrelationStatus": "Matched",
  "expectedWlanInterfaceFingerprint": "a1b2c3d4e5",
  "wlanCorrelationMessage": "Windows 최적 라우팅 인터페이스가 현재 연결된 Native WLAN 인터페이스와 일치합니다.",
  "selectedInterface": {
    "idFingerprint": "a1b2c3d4e5",
    "category": "Wireless",
    "nativeInterfaceType": "Wireless80211",
    "operationalState": "Up",
    "hasDefaultGateway": true,
    "isVirtual": false,
    "isVpn": false
  }
}
```

## CSV 구조

`section,key,value` 형식을 유지하며 다음 행이 추가됩니다.

```text
route.1,wlanCorrelationStatus,Matched
route.1,expectedWlanInterfaceFingerprint,a1b2c3d4e5
route.1,wlanCorrelationMessage,...
```

CSV 수식 시작 문자는 기존과 같이 비활성화합니다.

## HTML 표시

각 라우팅 결과 카드에 다음 항목을 표시합니다.

- WLAN NIC 비교 상태
- 예상 WLAN ID 지문
- 선택된 라우팅 ID 지문
- Native WLAN 인터페이스 비교 설명

HTML은 외부 JavaScript, CSS, 웹폰트, 이미지와 iframe을 사용하지 않으며 Content Security Policy와 HTML 인코딩을 유지합니다.

## 개인정보 경계

구조화 상관분석을 추가해도 다음 원문은 보고서 모델에 포함하지 않습니다.

- Native WLAN 전체 인터페이스 GUID
- 선택된 라우팅 인터페이스 전체 GUID
- 인터페이스 이름과 설명
- DNS로 해석한 IP 주소
- 게이트웨이·DNS 서버·MAC 주소
- 실제 내부·외부 URL
- 프록시 호스트와 PAC URL

`wlanCorrelationMessage`와 기존 경고·설명은 민감정보 마스킹을 거쳐 저장합니다.

## 해석 예

### 내부 DIRECT + Matched

내부 서버까지의 Windows 최적 경로가 현재 연결 WLAN NIC와 같습니다. 내부 다운로드 실측을 무선 경로 진단에 사용할 수 있는 근거가 강화됩니다.

### 내부 DIRECT + DifferentInterface / Ethernet

내부 다운로드는 유선을 선택하고 WLAN 상태는 Wi-Fi에서 읽는 불일치입니다. 이 결과를 Wi-Fi 처리량으로 해석하지 않습니다.

### 내부 DIRECT + DifferentInterface / Wireless

내장 Wi-Fi와 USB Wi-Fi가 함께 활성화되어 다른 물리 Wi-Fi가 선택됐을 수 있습니다. 사용하지 않는 무선 NIC를 비활성화하거나 목적지별 Windows 라우팅을 확인합니다.

### 프록시 엔드포인트 + DifferentInterface / Tunnel

PC에서 회사 프록시까지의 로컬 경로가 VPN 또는 보안 터널을 선택합니다. 외부 다운로드 결과에는 Wi-Fi 물리 구간 외에 VPN·프록시 경로가 포함될 수 있습니다.

### 외부 사이트 참고 경로 + Matched

회사 프록시 환경에서는 외부 사이트 주소의 직접 라우팅이 현재 WLAN과 일치해도 실제 HTTP 연결 경로를 증명하지 않습니다. 실제 로컬 연결 대상은 프록시 엔드포인트일 수 있습니다.

## 호환성

스키마 `1.0` 소비자는 새 필드를 모르는 경우 무시할 수 있습니다. 스키마 `1.1` 소비자는 상관분석 필드를 optional로 처리해야 하며, 이전 결과나 판단 불가 상태에서는 지문과 메시지가 `null`일 수 있습니다.
