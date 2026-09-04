# v0.1.0-alpha.10 사전 릴리스 노트

## 개요

이번 버전은 브라우저 다운로드 관찰이 **같은 물리 Wi-Fi 인터페이스**, **연속된 카운터 시간 구간**, **확인 가능한 Native WLAN identity**만 사용하도록 안전 경계를 강화합니다. 구조화 종료 원인, 전용·통합 보고서, 개인정보 마스킹, Windows 절전 처리와 Portable ZIP 운영 문서 검증도 하나의 배포 계약으로 통합했습니다.

## 주요 변경

### 1. 물리 Wi-Fi 카운터를 GUID로 고정

- Native WLAN에서 선택한 물리 Wi-Fi GUID를 저수준 `NetworkInterface` 카운터 공급자까지 전달합니다.
- 후속 카운터 읽기는 `RequireExactInterfaceId`만 사용합니다.
- 같은 설명의 다른 Wi-Fi, 첫 번째 활성 Wi-Fi, Wi-Fi Direct, VPN·터널 또는 가상 NIC로 자동 우회하지 않습니다.
- 공급자가 다른 인터페이스를 반환하면 해당 샘플을 사용하지 않고 `CounterProviderMismatch`로 종료합니다.

### 2. WLAN 연결 ID 연속성

- Native WLAN 연결 또는 GUID를 1·2회 일시적으로 확인하지 못하면 시작 시 고정한 카운터만 제한적으로 계속 읽습니다.
- identity가 없는 샘플은 시간축 증거로 보존하지만 RSSI·BSSID·PHY를 신뢰하지 않고 대표 처리량 통계에서 제외합니다.
- 같은 GUID가 복구되면 연속 미확인 횟수를 초기화하고 관찰을 계속합니다.
- 세 번째 연속 미확인에서는 현재 카운터를 읽기 전에 `WlanIdentityUnavailable`로 중단합니다.
- 실제 다른 유효 GUID가 나타나면 임계값을 기다리지 않고 즉시 `AdapterChanged`로 중단합니다.
- identity 미확인 구간을 직전 정상 처리량과 연결해 정지·급락으로 오판정하지 않습니다.

### 3. 샘플 시간 연속성

```text
허용 상한 = max(5초, 예상 샘플 간격 × 4)
```

- 0 또는 음수 카운터 간격을 차단합니다.
- 허용 상한을 초과한 현재 카운터를 `BrowserObservationSample`로 만들기 전에 차단합니다.
- 비정상 구간의 바이트를 평균·최고·총 수신량·정지·급락·시간축에 포함하지 않습니다.
- 부분 결과의 종료 시각을 마지막 유효 카운터 시각으로 고정합니다.
- 구조화 종료 원인은 `TimingDiscontinuity`입니다.

### 4. 카운터 재설정 복구

- 누적 Rx·Tx 카운터 감소를 `CounterReset`으로 기록합니다.
- 재설정 구간의 Rx·Tx delta는 0, Mbps는 `null`로 처리합니다.
- 같은 고정 NIC에서 다음 정상 증가 카운터부터 계산을 재개합니다.
- 통합 보고서에 `BROWSER_OBSERVATION_COUNTER_RESET` Warning Finding을 추가합니다.
- 카운터 재설정 후 시간 단절이 발생해도 재설정 근거와 최종 `TimingDiscontinuity`를 모두 보존합니다.

### 5. Windows 절전·복귀

- WPF의 로컬 `WM_POWERBROADCAST` 메시지를 사용합니다.
- Suspend에서 활성 브라우저 관찰을 취소합니다.
- 사용자 중지와 시스템 절전을 thread-safe 우선순위 컨텍스트로 구분합니다.

```text
None < CanceledByUser < SystemSuspend
```

- 절전 전 유효 샘플만 결과에 유지합니다.
- 실제 Resume가 관측되고 관찰·측정이 유휴 상태가 된 뒤 어댑터 진단을 한 번만 다시 실행합니다.
- Critical Resume, Resume Suspend와 Automatic Resume를 처리합니다.
- AC·배터리 상태 변경만으로는 관찰을 중단하지 않습니다.

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

`Status`는 결과의 가용 수준을 나타내고 `TerminationReason`은 실행이 끝난 직접 원인을 나타냅니다. 예를 들어 사용자 중지와 시스템 절전은 모두 `Canceled` 상태일 수 있지만 서로 다른 종료 원인과 Finding을 가집니다.

## 보고서

### 브라우저 관찰 전용 보고서

```text
WlanBrowserObservation_yyyyMMdd_HHmmss.json
WlanBrowserObservation_yyyyMMdd_HHmmss.csv
WlanBrowserObservation_yyyyMMdd_HHmmss.html
WlanBrowserObservation_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

다음 내용을 저장합니다.

- 상태와 구조화 종료 원인
- 한국어 종료 설명
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

다음 원문은 관찰 전용·통합 보고서에 포함하지 않습니다.

- SSID와 BSSID
- 인터페이스 이름·설명·전체 GUID
- IPv4·IPv6와 MAC 주소
- 게이트웨이와 DNS 주소
- 내부·외부 URL과 파일명
- 이메일과 인증 정보

공용 텍스트 마스커에도 bare·braced GUID 제거를 추가했습니다. URL 파일명 힌트 안의 GUID도 제거하며, CSV 수식 주입 방지와 HTML 인코딩·Content Security Policy를 유지합니다.

## 고정 Finding

주요 브라우저 관찰 판정은 다음과 같습니다.

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

지원되지 않는 종료 원문은 Finding에 그대로 반사하지 않고 `BROWSER_OBSERVATION_TERMINATION_UNKNOWN`으로 처리합니다.

## 자동 검증

### Core·Windows Smoke

- 정확한 GUID 고정
- 일시 WLAN ID 누락과 동일 ID 복구
- 세 번째 미확인 중단
- 실제 다른 GUID 즉시 변경
- 카운터 재설정과 정상 복구
- 시간 연속성 경계값
- 사용자 중지·SystemSuspend 동시 요청 우선순위
- Resume 전 어댑터 재평가 차단
- 같은 NIC의 BSSID 로밍

### 주입식 end-to-end

- 정상 완료
- 사용자 취소
- Native WLAN 물리 NIC 변경
- 고정 NIC Down·제거
- 카운터 공급자 불일치
- WLAN ID 일시 누락 후 동일 ID 복구
- WLAN ID 3회 연속 미확인
- SystemSuspend
- TimingDiscontinuity
- CounterReset 후 정상 복구
- CounterReset 후 TimingDiscontinuity

### 보고서 행렬

모든 종료 원인이 전용·통합 JSON·CSV·HTML과 예상 Finding 코드·심각도에 포함되는지 확인합니다. enum에 새 종료 원인이 추가되고 행렬을 갱신하지 않으면 CI가 실패합니다.

### Portable ZIP

관찰·보고서 운영 문서와 이 릴리스 노트를 publish 결과와 실제 ZIP 엔트리에서 확인합니다.

- 필수 문서 존재
- 파일 크기 0 초과
- 정규화된 ZIP 경로
- 중복 엔트리 없음
- 실제 설정·로그·보고서·캡처 미포함

## 배포물

GitHub Release에는 정확히 다음 네 파일만 유지합니다.

```text
WlanLivePathTester-win-x64-portable.zip
WlanLivePathTester-win-x64-single-file.exe
SHA256SUMS.txt
THIRD_PARTY_NOTICES.md
```

두 실행 형태 모두 .NET 런타임을 포함하므로 Python과 별도 .NET 설치가 필요하지 않습니다.

## 통신·의존성 경계

- AI 또는 로컬 AI 없음
- 외부 분석 API 없음
- 텔레메트리 없음
- 자동 오류 전송 없음
- 자동 업데이트 없음
- 결과 업로드 없음

외부 HTTP·HTTPS 통신은 사용자가 명시적으로 시작한 속도 측정용 `HEAD`·`GET`만 사용합니다. 브라우저 관찰, WLAN 연속성, 절전 감지, 보고서 생성과 Finding은 로컬 Windows API와 메모리 데이터만 사용합니다.

## 현재 한계

- 상용 Authenticode 코드 서명 인증서가 없어 실행 파일은 아직 서명되지 않았습니다.
- 실제 회사 PAC·WPAD, HTTP 407 Negotiate·NTLM, TLS 검사, GPO·EDR·SmartScreen 호환성은 사용자 환경에서 확인해야 합니다.
- Modern Standby와 무선 드라이버별 전원 메시지 순서는 실제 Windows 11 장치에서 확인해야 합니다.
- 보고서 마스킹은 보조 수단이므로 회사 밖으로 공유하기 전에 내용을 직접 다시 검토해야 합니다.

## 권장 실제 검증

1. 내장 Wi-Fi 단독 정상 관찰
2. 내장·USB Wi-Fi 동시 활성과 정확한 GUID 선택
3. 같은 NIC의 Aruba AP 로밍
4. 다른 물리 Wi-Fi로 전환
5. USB Wi-Fi 제거 또는 NIC 비활성화
6. WLAN 상태 1·2회 일시 누락 후 동일 ID 복구
7. WLAN 연결 ID 3회 연속 미확인
8. 절전·최대 절전·Modern Standby
9. 무선 드라이버 재시작과 카운터 재설정
10. CPU 부하와 장시간 스케줄러 지연
11. 내부 DIRECT 다운로드
12. 외부 PROXY 다운로드
13. PAC·WPAD·407·TLS 검사
14. 전용·통합 보고서 개인정보 확인
15. Release 자산 SHA-256 재검증
