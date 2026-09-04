# v0.1.0-alpha.10 사전 릴리스 노트

## 개요

이번 사전 릴리스는 브라우저 다운로드 관찰이 실제로 같은 물리 Wi-Fi 인터페이스와 연속된 시간 구간만 사용하도록 안전 경계를 강화하고, 종료 원인·보고서·고정 판정·Portable ZIP 운영 문서를 하나의 검증 체계로 통합합니다.

## 핵심 변경

### 물리 Wi-Fi 카운터 고정

- Native WLAN에서 선택한 물리 Wi-Fi GUID를 저수준 `NetworkInterface` 카운터 공급자까지 전달
- 후속 샘플은 `RequireExactInterfaceId`만 사용
- 같은 설명의 다른 Wi-Fi, 첫 번째 활성 Wi-Fi, Wi-Fi Direct, VPN·가상 NIC로 자동 우회하지 않음
- 공급자가 다른 인터페이스를 반환하면 `CounterProviderMismatch`

### WLAN 연결 ID 연속성

- Native WLAN 연결 또는 GUID 일시 미확인 1·2회는 시작 시 고정한 카운터로 제한적으로 재확인
- identity 없는 샘플은 WLAN 메타데이터를 신뢰하지 않고 대표 처리량 통계에서 제외
- 동일 GUID가 복구되면 연속 미확인 횟수를 초기화하고 관찰 계속
- 세 번째 연속 미확인에서는 현재 카운터를 읽기 전에 `WlanIdentityUnavailable`로 중단
- 실제 다른 유효 GUID는 임계값을 기다리지 않고 즉시 `AdapterChanged`

### 샘플 시간 연속성

```text
허용 상한 = max(5초, 예상 샘플 간격 × 4)
```

- 0·음수 간격 차단
- 허용 상한 초과 카운터를 샘플 생성 전에 차단
- 비정상 구간의 바이트를 평균·최고·총량·정지·급락·시간축에서 제외
- 부분 요약 종료 시각을 마지막 유효 카운터로 고정
- 구조화 종료 원인 `TimingDiscontinuity`

### 카운터 재설정 복구

- 누적 Rx·Tx 카운터 감소를 `CounterReset`으로 기록
- 재설정 구간의 delta는 0, Mbps는 null로 처리
- 같은 고정 NIC에서 다음 정상 증가 카운터부터 계산 복구
- 전용 Warning Finding `BROWSER_OBSERVATION_COUNTER_RESET`
- 재설정 뒤 시간 단절이 발생해도 두 근거를 모두 보존

### Windows 절전·복귀

- WPF `WM_POWERBROADCAST` 사용
- Suspend에서 활성 관찰 취소
- 사용자 중지와 시스템 절전을 thread-safe 우선순위 컨텍스트로 구분
- `None < CanceledByUser < SystemSuspend`
- 절전 전 유효 샘플만 보존
- 실제 Resume가 관측되고 관찰·측정이 유휴 상태가 된 뒤 어댑터 진단을 한 번만 재실행
- Critical Resume, Resume Suspend와 Automatic Resume 지원
- AC·배터리 상태 변경만으로는 관찰을 중단하지 않음

## 구조화 종료 원인

```text
Completed
CanceledByUser
AdapterChanged
AdapterUnavailable
WlanIdentityUnavailable
CounterProviderMismatch
SystemSuspend
TimingDiscontinuity
InvalidOptions
UnsupportedPlatform
NoWirelessConnection
Failed
```

상태와 종료 원인을 분리합니다. 예를 들어 `Canceled` 상태라도 사용자 중지와 `SystemSuspend`는 서로 다른 의미와 Finding을 가집니다.

## 보고서

### 브라우저 관찰 전용 보고서

```text
WlanBrowserObservation_yyyyMMdd_HHmmss.json
WlanBrowserObservation_yyyyMMdd_HHmmss.csv
WlanBrowserObservation_yyyyMMdd_HHmmss.html
WlanBrowserObservation_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

- 종료 원인과 한국어 표시
- 기준·평균·최고 처리량
- 총 수신량과 신뢰도
- BSSID 변경·NIC 변경·WLAN 미확인·카운터 재설정·정지·급락 횟수
- 시간축 샘플과 상태 플래그

### 통합 로컬 진단 보고서

- `browserObservation.status`
- `browserObservation.terminationReason`
- JSON·CSV의 머신용 Finding 코드
- HTML의 사람이 읽는 제목·근거·해석·조치·한계

### 개인정보 경계

다음 원문은 관찰 전용·통합 보고서에서 제외합니다.

- SSID와 BSSID
- 인터페이스 이름·설명·전체 GUID
- IPv4·IPv6·MAC
- 게이트웨이와 DNS
- 내부·외부 URL과 파일명
- 이메일과 인증 정보

CSV 수식 주입 방지, HTML 인코딩과 Content Security Policy를 유지합니다.

## 고정 Finding

주요 관찰 판정:

```text
BROWSER_OBSERVATION_COMPLETED
BROWSER_OBSERVATION_CANCELED_BY_USER
BROWSER_OBSERVATION_ADAPTER_CHANGED
BROWSER_OBSERVATION_ADAPTER_UNAVAILABLE
BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE
BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH
BROWSER_OBSERVATION_SYSTEM_SUSPEND
BROWSER_OBSERVATION_TIMING_DISCONTINUITY
BROWSER_OBSERVATION_COUNTER_RESET
BROWSER_OBSERVATION_LOW_CONFIDENCE
```

지원되지 않는 종료 원문은 Finding에 반사하지 않고 `BROWSER_OBSERVATION_TERMINATION_UNKNOWN`으로 처리합니다.

## 자동 검증

### Core

- 정확한 GUID 고정
- 일시 WLAN ID 누락과 복구
- 세 번째 미확인 중단
- 실제 다른 GUID 즉시 변경
- 카운터 재설정
- 시간 연속성 경계값
- 사용자 중지·SystemSuspend 동시 요청 우선순위
- Resume 전 어댑터 재평가 차단

### 주입식 end-to-end

- 정상 완료
- 사용자 취소
- Native WLAN 물리 NIC 변경
- 고정 NIC Down·제거
- 카운터 공급자 불일치
- 동일 NIC BSSID 로밍
- WLAN ID 일시 누락 후 동일 ID 복구
- WLAN ID 3회 연속 미확인
- SystemSuspend
- TimingDiscontinuity
- CounterReset 후 정상 복구
- CounterReset 후 TimingDiscontinuity

### 보고서 행렬

모든 종료 원인이 전용·통합 JSON·CSV·HTML과 예상 Finding 코드·심각도에 포함되는지 검증합니다. enum에 새 값이 추가되고 행렬을 갱신하지 않으면 CI가 실패합니다.

### Portable ZIP

관찰·보고서 운영 문서 12개를 publish 및 실제 ZIP 엔트리에서 검증합니다.

- 문서 존재
- 파일 크기 0 초과
- 중복 엔트리 없음
- 실제 설정·로그·보고서·캡처 미포함

## 배포물

GitHub Release에는 기존 정책대로 정확히 네 파일만 유지합니다.

```text
WlanLivePathTester-win-x64-portable.zip
WlanLivePathTester-win-x64-single-file.exe
SHA256SUMS.txt
THIRD_PARTY_NOTICES.md
```

두 실행 형태 모두 .NET 런타임을 포함합니다. Python과 별도 .NET 설치가 필요하지 않습니다.

## 현재 한계

- 상용 Authenticode 코드 서명 인증서가 없어 실행 파일은 서명되지 않았습니다.
- 실제 회사 PAC·WPAD, HTTP 407 Negotiate·NTLM, TLS 검사, GPO·EDR·SmartScreen 호환성은 사용자 환경에서 확인해야 합니다.
- Modern Standby와 드라이버별 전원 메시지 순서는 실제 Windows 11 장치에서 확인해야 합니다.
- 보고서 마스킹은 보조 수단이므로 회사 밖으로 공유하기 전 사용자가 내용을 다시 검토해야 합니다.

## 권장 실제 검증

1. 내장 Wi-Fi 단독 정상 관찰
2. 내장·USB Wi-Fi 동시 활성과 정확한 GUID 선택
3. 같은 NIC의 Aruba AP 로밍
4. 물리 Wi-Fi 전환
5. USB Wi-Fi 제거·NIC 비활성화
6. WLAN 상태 1~2회 일시 누락 후 동일 ID 복구
7. WLAN 연결 3회 연속 미확인
8. 절전·최대 절전·Modern Standby
9. 무선 드라이버 재시작과 카운터 재설정
10. CPU 부하·스케줄러 장기 지연
11. 내부 DIRECT 다운로드
12. 외부 PROXY 다운로드
13. PAC·WPAD·407·TLS 검사
14. 전용·통합 보고서 개인정보 확인
15. Release 자산 SHA-256 재검증
