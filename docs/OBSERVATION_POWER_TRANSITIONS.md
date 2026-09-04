# 브라우저 관찰의 절전·복귀 처리

브라우저 다운로드 관찰은 선택한 물리 Wi-Fi 인터페이스의 누적 Rx/Tx 카운터 차이를 시간 순서대로 계산합니다. Windows가 관찰 중 절전 또는 최대 절전 상태로 전환되면 전원 전환 전후의 카운터·시간 간격·WLAN 연결 상태를 한 세션으로 결합하면 잘못된 처리량이 계산될 수 있습니다.

이 기능은 시스템 전원 전환을 로컬 Windows 메시지로 감지하고, 절전 전환 시 활성 관찰을 중단하며, 복귀 후 어댑터 선택을 다시 평가합니다.

## 로컬 Windows 메시지

WPF 창의 `WM_POWERBROADCAST`를 사용합니다.

| 값 | 처리 |
|---|---|
| `PBT_APMSUSPEND` | 활성 브라우저 관찰 취소, `SystemSuspend` 종료 원인 준비 |
| `PBT_APMRESUMESUSPEND` | Wi-Fi·VPN·가상 어댑터 재평가 |
| `PBT_APMRESUMEAUTOMATIC` | 자동 복귀 후 어댑터 재평가 |
| `PBT_APMPOWERSTATUSCHANGE` | 배터리·AC 상태 정보만 변경; 관찰 취소하지 않음 |

이 메시지를 처리하기 위해 외부 서비스나 API를 호출하지 않습니다.

## 절전 전환 처리

```text
활성 브라우저 관찰
  ↓ PBT_APMSUSPEND
절전 중단 플래그 기록
  ↓
현재 관찰 CancellationToken 취소
  ↓
수집된 샘플까지만 보존
  ↓
BrowserObservationTerminationReason.SystemSuspend
```

사용자가 중지 버튼을 누른 결과와 구분하기 위해 앱이 최종 관찰 결과의 종료 원인을 `SystemSuspend`로 설정합니다. 관찰 결과 메시지와 화면에도 시스템 절전 때문에 중단됐다는 설명을 추가합니다.

## 복귀 처리

복귀 이벤트가 오면 다음 로컬 상태를 다시 확인합니다.

- 활성 물리 Wi-Fi 후보
- Native WLAN 현재 연결 인터페이스
- 내장·USB Wi-Fi의 선택 점수와 모호성
- 활성 VPN·터널
- Hyper-V·VMware·WSL·Wi-Fi Direct 등 가상 어댑터

측정 또는 관찰이 아직 정리 중이면 즉시 선택을 바꾸지 않습니다. 작업이 유휴 상태가 된 뒤 어댑터 진단을 다시 실행합니다.

## 구조화 종료 원인

절전으로 중단된 관찰은 다음 값을 사용합니다.

```text
status: Canceled 또는 PartialSuccess
terminationReason: SystemSuspend
```

실제 샘플이 하나도 없으면 취소 결과만 남을 수 있습니다. 일부 샘플이 있으면 관찰 전용 보고서에 중단 전 샘플과 `SystemSuspend`가 함께 기록됩니다.

## 화면 동작

관찰 결과에 다음 취지의 문장이 추가됩니다.

```text
종료 원인: 시스템 절전 전환 (SystemSuspend)
```

복귀 후 어댑터 진단이 완료되기 전에는 이전 선택 결과를 그대로 신뢰하지 않습니다. 내장·USB Wi-Fi 또는 VPN 상태가 달라졌으면 새 선택 결과와 경고를 확인한 뒤 다음 관찰을 시작합니다.

## 통신·데이터 경계

이 기능이 사용하는 정보는 다음뿐입니다.

- WPF 창의 로컬 `WM_POWERBROADCAST`
- 현재 관찰 CancellationToken
- 로컬 Native WLAN 상태
- 로컬 NetworkInterface 상태

다음 작업은 수행하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 조회
- 외부 API 호출
- 전원·인터페이스 상태 업로드
- AI·텔레메트리·자동 업데이트

## 판단 한계

- 일부 장치·드라이버·가상화 환경에서는 특정 전원 메시지가 늦게 도착하거나 누락될 수 있습니다.
- 강제 전원 차단이나 시스템 크래시는 정상적인 Suspend 이벤트를 제공하지 않을 수 있습니다.
- 복귀 직후 WLAN AutoConfig와 무선 드라이버가 완전히 준비되기 전에는 어댑터 선택이 일시적으로 실패할 수 있습니다.
- Windows Modern Standby 장치에서는 짧은 저전력 전환이 일반적인 절전과 다르게 보고될 수 있습니다.
- 이 기능은 관찰 데이터 혼합 방지용이며 절전 원인이나 배터리·펌웨어 상태를 진단하지 않습니다.

## 실제 환경 검증

1. 브라우저 관찰을 시작하고 5초 이상 샘플을 수집합니다.
2. Windows 절전을 실행합니다.
3. 시스템을 복귀시킵니다.
4. 관찰이 계속 실행되지 않고 종료됐는지 확인합니다.
5. 화면과 관찰 전용 보고서에 `SystemSuspend`가 있는지 확인합니다.
6. 복귀 후 어댑터 진단이 다시 평가되는지 확인합니다.
7. 내장 Wi-Fi·USB Wi-Fi·VPN 상태를 변경한 채 복귀했을 때 이전 선택을 그대로 사용하지 않는지 확인합니다.
8. AC 어댑터 연결·분리만으로 관찰이 중단되지 않는지 확인합니다.
9. 같은 NIC에서 BSSID만 바뀌는 로밍이 `SystemSuspend`로 오인되지 않는지 확인합니다.

실제 SSID, BSSID, 인터페이스 GUID, IP, MAC, 프록시 주소와 PAC URL은 공개 Issue에 원문으로 남기지 않습니다.
