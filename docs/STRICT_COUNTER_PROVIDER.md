# 브라우저 관찰 저수준 Wi-Fi 카운터 고정

브라우저 다운로드 관찰은 특정 물리 Wi-Fi 인터페이스의 누적 Rx/Tx 바이트를 읽습니다. 이 문서는 관찰 실행 계약뿐 아니라 실제 카운터 공급자에서도 고정 인터페이스 ID를 강제하는 경계를 설명합니다.

## 목적

다음 환경에서는 단순히 첫 번째 `Up` 상태 Wi-Fi를 선택하면 잘못된 처리량이 표시될 수 있습니다.

- 내장 Wi-Fi와 USB Wi-Fi 동시 활성
- Microsoft Wi-Fi Direct Virtual Adapter 존재
- VPN·터널·가상 어댑터 혼재
- 관찰 중 Wi-Fi NIC 비활성화 또는 교체
- Native WLAN 연결과 `NetworkInterface` 열거 순서 불일치

잘못된 NIC의 카운터를 계속 읽는 것보다 관찰을 중단하고 원인을 명확히 표시하는 것이 안전합니다.

## 선택 경계

```text
Native WLAN 연결
  ↓ 인터페이스 GUID 또는 설명
브라우저 관찰 시작
  ↓ 고정 ID
WindowsInterfaceCounterReader
  ├─ 물리 Wireless80211 후보만 유지
  ├─ Wi-Fi Direct·가상·VPN 후보 제외
  ├─ GUID 정확 일치 우선
  ├─ GUID 불가 시 설명 완전 일치
  └─ 미일치·중복·Down이면 다른 NIC로 우회하지 않음
```

고정 ID가 제공되면 활성 물리 Wi-Fi가 하나뿐이어도 해당 ID와 다르면 선택하지 않습니다. 고정 식별정보가 전혀 없는 레거시 호출에서만 활성 물리 Wi-Fi가 정확히 하나일 경우 선택할 수 있습니다.

## 관찰 중 연속성

관찰 시작 시 카운터 공급자가 반환한 실제 인터페이스 ID를 고정합니다. 이후 모든 카운터 읽기는 같은 ID와 설명을 다시 전달합니다.

- 같은 NIC의 BSSID 변경은 로밍으로 처리하고 계속 관찰합니다.
- Native WLAN ID의 일시적인 1~2회 누락은 재확인합니다.
- 다른 ID 또는 ID 미확인이 3회 연속 발생하면 `AdapterChanged`로 종료합니다.
- 고정 ID를 로컬 카운터 공급자에서 찾지 못하면 `CounterProviderMismatch`로 종료합니다.
- 고정 NIC가 Down이거나 통계를 읽을 수 없으면 `AdapterUnavailable`로 종료합니다.
- 사용자 중지는 `CanceledByUser`로 별도 기록합니다.

## 구조화된 종료 원인

`BrowserObservationResult.TerminationReason`은 다음 값을 사용합니다.

| 값 | 의미 |
|---|---|
| `Completed` | 계획된 관찰 완료 |
| `CanceledByUser` | 사용자가 중지 |
| `AdapterChanged` | 관찰 중 Native WLAN 물리 NIC 변경 |
| `AdapterUnavailable` | 고정 NIC가 Down·소멸하거나 통계 조회 불가 |
| `CounterProviderMismatch` | 고정 ID와 대응되는 물리 Wi-Fi 카운터를 찾지 못함 또는 후보 중복 |
| `InvalidOptions` | 관찰 시간·간격 설정 오류 |
| `UnsupportedPlatform` | Windows가 아닌 환경 또는 API 미지원 |
| `NoWirelessConnection` | 연결된 Native WLAN 없음 |
| `Failed` | 위 범주로 분류하지 못한 실행 오류 |

기존 `BrowserObservationStatus`는 화면 호환성을 유지하고, 종료 원인은 자동 분석과 보고서가 문장 파싱 없이 원인을 구분하도록 보조합니다.

## 통신·데이터 경계

이 기능은 다음 로컬 정보만 사용합니다.

- Windows Native WLAN 인터페이스 상태
- `NetworkInterface` 유형·ID·운영 상태
- 선택된 물리 Wi-Fi의 누적 바이트 통계

다음 작업은 수행하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 조회
- 외부 API 호출
- 인터페이스 ID 업로드
- AI·텔레메트리·자동 업데이트

전체 인터페이스 GUID는 로컬 대응에만 사용하며 공개 보고서에는 원문으로 저장하지 않습니다.

## 실제 환경 검증

- 내장 Wi-Fi만 활성화했을 때 정상 관찰
- 내장 Wi-Fi와 USB Wi-Fi가 함께 활성화됐을 때 현재 연결 NIC 선택
- 고정 ID와 다른 NIC만 남긴 상태에서 관찰 시작 차단
- 관찰 중 고정 NIC를 비활성화했을 때 `AdapterUnavailable`
- 관찰 중 다른 Wi-Fi로 전환했을 때 `AdapterChanged`
- 같은 NIC에서 AP 로밍 시 관찰 유지
- Wi-Fi Direct와 가상 무선 어댑터가 카운터 후보에서 제외되는지 확인
- 사용자 중지가 `CanceledByUser`로 구분되는지 확인

실제 SSID, BSSID, IP, MAC, 전체 GUID, 프록시 주소와 PAC URL은 공개 Issue에 원문으로 남기지 않습니다.
