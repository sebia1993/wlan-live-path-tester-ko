# v0.1.0-alpha.9 사전 릴리스 노트

## 핵심 변경

### 브라우저 관찰 샘플 시간 연속성 보호

브라우저 관찰의 연속 카운터 타임스탬프를 검사해, 절전 메시지가 누락되거나 드라이버·운영체제 스케줄링이 장시간 멈춘 경우 잘못된 처리량 계산을 중단합니다.

```text
허용 상한 = max(5초, 예상 샘플 간격 × 4)
```

- 0 또는 음수 샘플 간격 거부
- 허용 상한을 초과한 현재 스냅샷을 처리량 샘플에 추가하기 전에 중단
- 비정상 구간의 바이트 델타를 평균·최고·총 수신량에서 제외
- 구조화 종료 원인 `TimingDiscontinuity`
- 일반 사용자 중지 및 명시적 시스템 절전과 분리

### 유지되는 관찰 안전 경계

- 선택된 물리 Wi-Fi ID를 저수준 `NetworkInterface` 카운터 공급자까지 고정
- 지정 ID 미발견·중복·Down 상태에서 다른 활성 Wi-Fi로 우회하지 않음
- 관찰 중 물리 Wi-Fi ID 변경 시 `AdapterChanged`
- 고정 NIC 통계 조회 불가 시 `AdapterUnavailable`
- 카운터 공급자와 고정 ID 불일치 시 `CounterProviderMismatch`
- Windows 절전 전환 시 `SystemSuspend`
- 정상 완료는 `Completed`, 사용자 중지는 `CanceledByUser`

### 브라우저 관찰 전용 보고서

JSON·CSV·외부 리소스 없는 단일 HTML에 다음을 기록합니다.

- 관찰 상태와 구조화 종료 원인
- 기준 수신량과 조정 평균·최고 처리량
- 총 수신량과 신뢰도
- 정지·급락·BSSID 변경·NIC 변경·카운터 재설정 횟수
- 시간축 Rx·Tx·RSSI·PHY 링크 속도 및 상태 플래그

SSID, BSSID 원문, 인터페이스 ID·이름·설명, IP, MAC, 게이트웨이, DNS, 다운로드 URL과 파일명은 포함하지 않습니다.

## 통신·데이터 경계

- AI·로컬 AI·외부 분석 API 없음
- 텔레메트리·자동 오류 전송·자동 업데이트 없음
- 결과 업로드 없음
- 관찰 안전 검사는 로컬 Native WLAN·NetworkInterface·타임스탬프만 사용
- 외부 측정은 사용자가 시작한 HTTP/HTTPS HEAD·GET만 사용

## 권장 실제 검증

1. 1초 간격 정상 관찰 후 `Completed` 확인
2. 사용자 중지 후 `CanceledByUser` 확인
3. 같은 NIC에서 AP 로밍 후 관찰 유지 확인
4. 다른 물리 Wi-Fi로 전환해 `AdapterChanged` 확인
5. 고정 Wi-Fi 비활성화·제거 후 `AdapterUnavailable` 확인
6. 관찰 중 Windows 절전 후 `SystemSuspend` 확인
7. 전원 메시지가 전달되지 않는 합성 5초 초과 지연에서 `TimingDiscontinuity` 확인
8. 긴 간격의 바이트 델타가 결과 통계에 포함되지 않는지 확인
9. 관찰 전용 보고서에서 민감한 네트워크 식별정보가 남지 않는지 확인
10. Release의 `SHA256SUMS.txt`와 실제 자산 해시 비교

## 현재 한계

- 실제 회사 PAC·WPAD·Negotiate/NTLM 407·TLS 검사·GPO·EDR 호환성은 사용자 환경에서 확인해야 합니다.
- 일부 Modern Standby·드라이버 환경은 전원 메시지와 인터페이스 복귀 시점이 다를 수 있습니다.
- 상용 Authenticode 인증서가 없어 실행 파일은 아직 코드 서명되지 않았습니다.
