# 다중 NIC·VPN·가상 어댑터 진단

이 기능은 Windows PC에 무선·유선·VPN·Hyper-V·VMware·WSL·Wi-Fi Direct 어댑터가 동시에 존재할 때 실제 WLAN 측정 후보와 경로 해석상의 주의점을 로컬에서 확인합니다.

어댑터 목록을 읽는 동작은 다음 통신을 수행하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC 또는 WPAD 조회
- 외부 API 호출
- 텔레메트리 또는 결과 업로드

## 분류 대상

프로그램은 `NetworkInterface.GetAllNetworkInterfaces()`의 로컬 정보와 Native WLAN이 반환한 현재 연결 인터페이스 ID를 비교합니다.

| 분류 | 예시 |
|---|---|
| Physical Wi-Fi | Intel·Realtek·Qualcomm·MediaTek 내장 또는 USB 무선랜 |
| Physical Ethernet | 실제 유선 NIC |
| Wi-Fi Direct/SoftAP | Microsoft Wi-Fi Direct Virtual Adapter, Hosted Network |
| VPN/Tunnel | Wintun, WireGuard, OpenVPN, Tailscale, AnyConnect, GlobalProtect 등 |
| Virtual Switch | Hyper-V vEthernet, VMware, VirtualBox, WSL, Docker |
| Other Virtual | 기타 가상 네트워크 어댑터 |
| Loopback | Windows 또는 Npcap 루프백 |
| Unknown | 고정 규칙으로 안전하게 분류할 수 없는 인터페이스 |

USB Wi-Fi라는 이유만으로 제외하지 않습니다. Windows가 `Wireless80211`로 보고하고 Wi-Fi Direct·VPN·가상 스위치 식별 근거가 없으면 물리 Wi-Fi 후보로 취급합니다.

## Wi-Fi 후보 점수

물리 Wi-Fi 후보에 다음 근거를 더해 점수를 계산합니다.

| 근거 | 가중치 |
|---|---:|
| 물리 Wi-Fi 후보 기본값 | 100 |
| Native WLAN 현재 연결 ID와 일치 | +100 |
| `OperationalStatus.Up` | +40 |
| 기본 게이트웨이 존재 | +20 |
| 유니캐스트 주소 존재 | +10 |
| 링크 속도 값 존재 | +5 |

가장 높은 점수의 활성 Wi-Fi가 하나뿐이면 권장 후보로 표시합니다. 최고 점수가 같은 활성 물리 Wi-Fi가 둘 이상이면 임의로 첫 번째 후보를 선택하지 않고 `모호함`으로 표시합니다.

## 표시하지 않는 값

어댑터 진단 탭은 다음 값을 원문으로 표시하지 않습니다.

- IPv4·IPv6 주소
- 기본 게이트웨이 주소
- MAC 주소
- 전체 인터페이스 GUID

인터페이스 ID는 로컬 비교를 위해 SHA-256 앞 10자리 지문으로만 표시합니다. 게이트웨이와 유니캐스트 주소는 존재 여부만 `Y`로 나타냅니다.

## VPN 해석

활성 VPN·터널이 존재하면 다음 영향을 받을 수 있습니다.

- 기본 라우팅
- 외부 URL의 도달 경로
- 회사 프록시까지의 경로
- DNS 정책
- 분할 터널링
- 방화벽 및 보안 에이전트 정책

어댑터 진단은 VPN의 내부 정책이나 서버 상태를 확인하지 않습니다. VPN 어댑터가 있다는 사실만으로 장애 원인으로 단정하지 않고, 외부 측정 결과와 운영 정책을 함께 확인합니다.

## 브라우저 관찰과의 관계

브라우저 관찰은 특정 Wi-Fi 인터페이스의 전체 Rx/Tx 카운터를 사용합니다. Hyper-V·VMware·VPN 인터페이스의 카운터를 Wi-Fi 카운터로 잘못 선택하면 처리량이 크게 왜곡될 수 있습니다.

현재 진단 탭은 다음을 수행합니다.

1. 실제 물리 Wi-Fi 후보 분류
2. Native WLAN 현재 연결 ID와 로컬 인터페이스 ID 비교
3. 다중 활성 Wi-Fi 모호성 감지
4. 활성 VPN·가상 어댑터 경고
5. 향후 브라우저 관찰과 WLAN 선택 로직에서 사용할 권장 인터페이스 ID 보관

실제 관찰 엔진에 권장 ID를 강제 적용하는 작업은 별도 변경으로 진행합니다. 그 전까지는 다중 Wi-Fi가 `모호함`으로 표시되면 사용하지 않는 무선 어댑터를 비활성화하거나 실제 연결 어댑터를 확인한 뒤 측정합니다.

## 실제 환경 검증

다음 조합에서 확인하는 것이 좋습니다.

- 내장 Wi-Fi만 활성화
- 내장 Wi-Fi와 USB Wi-Fi 동시 활성화
- Wi-Fi와 유선 동시 활성화
- Wi-Fi와 회사 VPN 동시 활성화
- Hyper-V 또는 WSL vEthernet 활성화
- VMware 또는 VirtualBox 가상 NIC 활성화
- Microsoft Wi-Fi Direct Virtual Adapter 활성화
- 절전 복귀 후 Wi-Fi 재연결
- 로밍으로 BSSID가 바뀐 상태

확인 항목:

- 실제 연결 중인 Wi-Fi가 권장 후보로 표시되는지
- Wi-Fi Direct가 물리 Wi-Fi로 선택되지 않는지
- 활성 VPN·가상 인터페이스가 경고에 표시되는지
- 두 물리 Wi-Fi의 우선순위가 같을 때 `모호함`이 표시되는지
- 새로고침이 외부 통신을 만들지 않는지
- IP·MAC·게이트웨이·전체 GUID가 화면에 노출되지 않는지
