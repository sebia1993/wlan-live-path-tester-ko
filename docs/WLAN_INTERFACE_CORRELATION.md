# Native WLAN과 로컬 NIC 대응

이 기능은 Windows Native WLAN API가 반환한 현재 연결 인터페이스와 `System.Net.NetworkInformation.NetworkInterface` 목록의 Wi-Fi NIC가 같은 장치인지 확인합니다.

내장 Wi-Fi와 USB Wi-Fi가 동시에 활성화돼 있거나 가상 무선 어댑터가 존재할 때 다음 오판을 줄이는 것이 목적입니다.

- WLAN 상태는 내장 NIC에서 읽었는데 처리량 카운터는 USB NIC로 해석하는 경우
- 연결된 물리 Wi-Fi 대신 Wi-Fi Direct 가상 어댑터를 선택하는 경우
- 인터페이스 이름이 다르다는 이유로 같은 장치를 서로 다른 NIC로 판단하는 경우
- 유선·VPN 경로를 사용하는 다운로드를 Wi-Fi 자체 성능으로 잘못 해석하는 경우

## 통신 경계

대응 확인은 다음 로컬 정보만 사용합니다.

- `WlanEnumInterfaces`가 반환하는 WLAN 인터페이스 GUID·설명·연결 상태
- `NetworkInterface.Id`와 로컬 Wi-Fi 어댑터 유형·상태
- 기본 게이트웨이 유무와 VPN·가상 분류

다음 작업은 수행하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 조회
- 외부 API 호출
- 공인 IP·ISP·GeoIP 조회
- GUID 또는 인터페이스 정보 업로드

## 대응 순서

```text
1. 연결된 Native WLAN 인터페이스 확인
2. 별도 WLAN identity 목록에서 설명이 정확히 일치하는 연결 항목의 GUID 보완
3. 로컬 NetworkInterface 중 Wireless80211 항목만 후보로 제한
4. 유효한 GUID 완전 일치 확인
5. GUID 일치가 없을 때만 설명 완전 일치 사용
6. 후보가 여러 개면 임의 선택하지 않고 중복 후보로 종료
```

인터페이스 ID는 유효한 GUID일 때만 정확 일치 기준으로 사용합니다. 임의 문자열이나 부분 일치는 허용하지 않습니다.

설명 보조 일치는 연속 공백과 탭·줄바꿈만 정규화하며, 포함 검색이나 유사도 검색은 사용하지 않습니다. 같은 설명을 가진 후보가 여러 개면 대응을 확정하지 않습니다.

## 화면 결과

`WLAN NIC 대응` 탭에는 다음만 표시합니다.

- Native WLAN 조회 성공 여부
- WLAN identity 목록 조회 성공 여부와 항목 개수
- 로컬 활성 Wi-Fi 개수
- 대응 방식: GUID 정확 일치, 설명 완전 일치, 중복 후보, 일치 없음
- 대응된 로컬 NIC 이름
- 대응된 NIC의 Up 상태
- 기본 게이트웨이 유무
- VPN·가상 어댑터 분류
- 유선·VPN·다중 게이트웨이로 인한 경로 혼재 가능성

인터페이스 GUID 원문은 화면에 표시하지 않습니다.

## 해석

### GUID 정확 일치

Native WLAN과 로컬 NetworkInterface가 같은 Windows 인터페이스 ID를 사용합니다. 현재 제공 가능한 가장 강한 대응 근거입니다.

### 설명 완전 일치

Native WLAN GUID를 직접 보완하지 못했지만 무선 NIC 설명이 정확히 한 개의 로컬 후보와 일치합니다. GUID 일치보다 약한 보조 근거입니다.

### 중복 후보

같은 GUID 또는 설명을 가진 Wi-Fi 후보가 여러 개입니다. 프로그램은 임의로 하나를 선택하지 않습니다.

### 일치 없음

Native WLAN 결과와 로컬 Wi-Fi 목록 사이에 정확한 대응을 만들지 못했습니다. 이 상태에서는 브라우저 관찰 카운터와 Native WLAN 상태가 같은 NIC에서 왔다고 단정하지 않습니다.

## 경고 조건

대응에 성공해도 다음 조건은 별도로 경고합니다.

- 대응된 NIC가 Up 상태가 아님
- 대응된 NIC가 가상 어댑터로 분류됨
- 대응된 NIC가 VPN·터널 후보로 분류됨
- 대응된 Wi-Fi에 기본 게이트웨이가 없음
- 활성 기본 게이트웨이가 여러 개임
- 물리 유선과 무선의 기본 경로가 동시에 활성화됨
- VPN 또는 터널 인터페이스가 활성화됨

## 개인정보 경계

인터페이스 GUID는 로컬 메모리에서 정확 대응에만 사용합니다. 인터페이스 환경 구조화 보고서는 다음 값을 포함하지 않습니다.

- GUID와 `NetworkInterface.Id`
- 인터페이스 이름과 설명
- IP·게이트웨이·DNS·DHCP 주소
- MAC 주소
- SSID와 BSSID

보고서에는 대응 여부를 추가할 경우에도 상태와 위험 플래그만 기록해야 하며 GUID나 NIC 이름을 기록하지 않습니다.

## 판단 한계

- NIC 대응 성공은 특정 HTTP 요청이 반드시 그 NIC로 나갔다는 의미가 아닙니다.
- Windows 라우팅은 목적지 prefix, interface metric, route metric, VPN split tunnel과 정책의 영향을 받습니다.
- 외부 프록시 측정에서 실제 로컬 연결 목적지는 외부 사이트가 아니라 회사 프록시일 수 있습니다.
- WLAN identity 보완은 연결된 Native WLAN 설명이 정확히 한 identity 항목과 일치할 때만 수행합니다.
- 드라이버가 WLAN API와 NetworkInterface에 서로 다른 설명을 제공하면 설명 보조 일치가 실패할 수 있습니다.

실제 목적지 경로는 다음 명령과 프록시 경로 판정을 함께 확인합니다.

```powershell
route print
Get-NetRoute -AddressFamily IPv4 |
  Sort-Object DestinationPrefix, RouteMetric
Get-NetIPInterface |
  Sort-Object InterfaceMetric
```
