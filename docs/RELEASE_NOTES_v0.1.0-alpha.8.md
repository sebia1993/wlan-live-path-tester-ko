# v0.1.0-alpha.8 사전 릴리스 노트

## 핵심 변경

### 저수준 물리 Wi-Fi 카운터 고정

브라우저 관찰의 모든 인터페이스 카운터 읽기를 관찰 시작 시 선택한 물리 Wi-Fi ID에 고정합니다.

- 지정 ID와 일치하는 물리 `Wireless80211` 카운터만 허용
- Wi-Fi Direct, 가상 무선, VPN·터널 후보 제외
- 지정 GUID가 없거나 중복·Down인 경우 다른 활성 Wi-Fi로 자동 우회하지 않음
- GUID가 제공된 상태에서는 같은 설명을 가진 다른 NIC로도 우회하지 않음

### 구조화 종료 원인

브라우저 관찰 결과에서 다음 종료 원인을 문장 파싱 없이 구분합니다.

```text
Completed
CanceledByUser
AdapterChanged
AdapterUnavailable
CounterProviderMismatch
SystemSuspend
InvalidOptions
UnsupportedPlatform
NoWirelessConnection
Failed
```

### 브라우저 관찰 전용 보고서

가장 최근 관찰 결과를 다음 로컬 파일로 저장합니다.

```text
WlanBrowserObservation_yyyyMMdd_HHmmss.json
WlanBrowserObservation_yyyyMMdd_HHmmss.csv
WlanBrowserObservation_yyyyMMdd_HHmmss.html
WlanBrowserObservation_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

보고서는 종료 원인, 처리량 요약, RSSI·PHY 링크 속도와 시간축 상태 플래그를 포함합니다. SSID, BSSID 원문, 인터페이스 GUID·이름, IP, MAC, 게이트웨이, DNS, URL과 파일명은 포함하지 않습니다.

### 절전·최대 절전·복귀

WPF 창의 로컬 `WM_POWERBROADCAST`를 처리합니다.

- 활성 관찰 중 Suspend를 감지하면 현재 관찰 취소
- 절전 전후의 긴 시간 간격과 누적 카운터 혼합 차단
- 사용자 중지가 아닌 `SystemSuspend`로 종료 원인 기록
- Resume 후 Wi-Fi·VPN·가상 어댑터 선택 재평가
- 일반 AC·배터리 상태 변경만으로는 관찰을 취소하지 않음

## 유지되는 통신 경계

- 외부 분석 API와 AI 없음
- 텔레메트리와 자동 오류 전송 없음
- 자동 업데이트 없음
- 결과 업로드 없음
- 프로그램 외부 요청은 사용자가 시작한 HTTP/HTTPS HEAD·GET 다운로드 측정뿐
- 브라우저 관찰, 어댑터 진단, 보고서 생성과 절전 감지는 로컬 API만 사용

## 권장 실제 검증

1. 내장 Wi-Fi 단독 관찰 후 `Completed` 확인
2. 관찰 중 사용자 중지 후 `CanceledByUser` 확인
3. 내장 Wi-Fi와 USB Wi-Fi를 함께 활성화해 현재 연결 NIC만 선택되는지 확인
4. 관찰 중 다른 물리 Wi-Fi로 전환해 `AdapterChanged` 확인
5. 고정 Wi-Fi를 비활성화·제거해 `AdapterUnavailable` 확인
6. 같은 NIC에서 BSSID 로밍 시 관찰이 유지되는지 확인
7. 관찰 중 Windows 절전 후 복귀해 `SystemSuspend` 확인
8. 복귀 후 어댑터 선택이 다시 평가되는지 확인
9. 관찰 보고서에서 실제 SSID·BSSID·전체 GUID·IP·MAC가 남지 않는지 확인
10. `SHA256SUMS.txt`와 Release 자산 해시 비교

## 현재 한계

- 상용 Authenticode 코드 서명 인증서가 없어 EXE는 서명되지 않았습니다.
- 실제 회사 PAC·WPAD·407·TLS 검사·GPO·EDR 호환성은 사용자 환경에서 확인해야 합니다.
- 일부 Modern Standby 장치나 드라이버는 전원 메시지 전달 방식이 다를 수 있습니다.
