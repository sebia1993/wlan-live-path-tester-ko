# WLAN Live Path Tester KO v0.1.0-alpha.10

Windows 11 x64용 사전 릴리스입니다.

## 배포물

GitHub Release에는 다음 네 파일만 제공합니다.

- `WlanLivePathTester-win-x64-portable.zip`: 호환성을 우선하는 권장 배포물
- `WlanLivePathTester-win-x64-single-file.exe`: 단일 실행 파일
- `SHA256SUMS.txt`: ZIP·EXE·제3자 고지 파일의 SHA-256
- `THIRD_PARTY_NOTICES.md`: 제3자 구성요소 고지

두 실행 형태 모두 .NET 런타임을 포함하므로 Python과 별도 .NET 설치가 필요하지 않습니다.

## 이번 버전의 핵심 변경

### 물리 Wi-Fi 카운터 고정

- Native WLAN에서 선택한 물리 Wi-Fi GUID를 저수준 `NetworkInterface` 카운터 공급자까지 고정합니다.
- 후속 샘플은 `RequireExactInterfaceId`만 사용합니다.
- 같은 설명의 다른 Wi-Fi, 첫 번째 활성 Wi-Fi, Wi-Fi Direct, VPN·터널 또는 가상 NIC로 자동 우회하지 않습니다.
- 고정 WLAN ID와 카운터 공급자가 일치하지 않으면 `CounterProviderMismatch`로 종료합니다.

### WLAN 연결 ID 연속성

- Native WLAN 연결 또는 인터페이스 ID를 1·2회 일시적으로 확인하지 못하면 시작 시 고정한 카운터로만 제한적으로 계속 확인합니다.
- identity가 없는 샘플은 시간축 증거로 남기되 RSSI·BSSID·PHY와 대표 처리량 통계에서 제외합니다.
- 같은 GUID가 복구되면 관찰을 계속합니다.
- 세 번째 연속 미확인에서는 현재 카운터를 읽기 전에 `WlanIdentityUnavailable`로 중단합니다.
- 실제 다른 GUID가 확인되면 임계값을 기다리지 않고 즉시 `AdapterChanged`로 종료합니다.

### 시간 연속성과 카운터 재설정

```text
허용 상한 = max(5초, 예상 샘플 간격 × 4)
```

- 0·음수 또는 허용 상한 초과 간격을 `TimingDiscontinuity`로 차단합니다.
- 비정상 구간의 바이트를 평균·최고·총량·정지·급락·시간축에서 제외합니다.
- 누적 Rx·Tx 카운터가 감소하면 `CounterReset`으로 기록하고 해당 구간의 델타와 Mbps를 계산하지 않습니다.
- 같은 고정 NIC에서 다음 정상 증가 카운터부터 계산을 재개합니다.

### Windows 절전·복귀

- 로컬 `WM_POWERBROADCAST`로 절전·복귀를 감지합니다.
- 사용자 중지와 시스템 절전을 별도 종료 원인으로 기록합니다.

```text
None < CanceledByUser < SystemSuspend
```

- 절전 전 유효 샘플만 결과에 유지합니다.
- 실제 Resume가 확인되고 측정·관찰이 유휴 상태가 된 뒤 Wi-Fi·VPN·가상 NIC 진단을 다시 수행합니다.
- 일반 AC·배터리 상태 변경만으로는 관찰을 중단하지 않습니다.

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

결과 `Status`와 직접 종료 원인을 분리하므로 사용자 중지, 시스템 절전, WLAN ID 연속 미확인, 물리 NIC 변경과 카운터 공급자 불일치를 구분할 수 있습니다.

## 보고서

### 브라우저 관찰 전용 보고서

```text
WlanBrowserObservation_yyyyMMdd_HHmmss.json
WlanBrowserObservation_yyyyMMdd_HHmmss.csv
WlanBrowserObservation_yyyyMMdd_HHmmss.html
WlanBrowserObservation_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

### 통합 로컬 진단 보고서

- WLAN·프록시·내부·외부 다운로드 측정과 브라우저 관찰을 함께 기록합니다.
- JSON·CSV에는 머신용 Finding 코드를 저장합니다.
- HTML에는 사람이 읽을 수 있는 제목·근거·해석·조치·한계를 표시합니다.

주요 관찰 Finding은 다음과 같습니다.

```text
BROWSER_OBSERVATION_ADAPTER_CHANGED
BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE
BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH
BROWSER_OBSERVATION_SYSTEM_SUSPEND
BROWSER_OBSERVATION_TIMING_DISCONTINUITY
BROWSER_OBSERVATION_COUNTER_RESET
BROWSER_OBSERVATION_LOW_CONFIDENCE
```

## 개인정보·데이터 경계

다음 원문은 관찰 전용·통합 보고서에서 제외합니다.

- SSID와 BSSID
- 인터페이스 이름·설명·전체 GUID
- IPv4·IPv6와 MAC 주소
- 게이트웨이와 DNS
- 내부·외부 URL과 파일명
- 이메일과 인증 정보

공용 텍스트 마스커는 bare·braced GUID와 URL 파일명 힌트 안의 GUID도 제거합니다. CSV 수식 주입 방지와 외부 리소스 없는 CSP 적용 HTML을 유지합니다.

## 통신·의존성 경계

다음 기능은 없습니다.

- AI 또는 로컬 AI
- 외부 분석 API
- 텔레메트리
- 자동 오류 전송
- 자동 업데이트
- 결과 업로드

외부 HTTP·HTTPS 통신은 사용자가 명시적으로 실행한 속도 측정용 `HEAD`·`GET`만 사용합니다. 브라우저 관찰과 보고서 생성은 새로운 네트워크 요청을 만들지 않습니다.

## 자동 검증

- Release 빌드와 Core SelfTest
- Windows WLAN API·WinHTTP 프록시 인증·다운로드·브라우저 관찰 Smoke
- 정확한 고정 GUID, WLAN ID 일시 누락·복구·임계값 초과
- 같은 NIC의 BSSID 로밍
- 카운터 재설정과 시간 단절
- 사용자 중지와 `SystemSuspend`
- 전용·통합 JSON·CSV·HTML의 종료 원인·Finding·개인정보 마스킹
- Portable ZIP·single-file 구조, PE, ProductVersion와 SHA-256
- 관찰·보고서 운영 문서와 릴리스 노트의 Portable ZIP 포함
- 공개 자산 재다운로드 후 GitHub digest·`SHA256SUMS.txt`·`BUILD_INFO.txt`·태그 commit 재검증

자동 HTTP 시험은 `127.0.0.1` 합성 서버·프록시만 사용하며 실제 외부 사이트나 회사 프록시에 접속하지 않습니다.

## 현재 한계

- 상용 Authenticode 인증서가 없어 실행 파일은 아직 코드 서명되지 않았습니다.
- 실제 회사 PAC·WPAD, HTTP 407 Negotiate·NTLM, TLS 검사, GPO·EDR·SmartScreen 호환성은 사용자 환경에서 확인해야 합니다.
- Modern Standby와 드라이버별 전원 메시지 순서는 실제 Windows 11 장치에서 확인해야 합니다.
- 보고서 마스킹은 보조 수단이므로 회사 밖으로 공유하기 전에 내용을 직접 다시 검토해야 합니다.

## 권장 실제 검증

1. 내장 Wi-Fi 단독 정상 관찰
2. 내장·USB Wi-Fi 동시 활성과 정확한 GUID 선택
3. 같은 NIC에서 Aruba AP 로밍
4. 다른 물리 Wi-Fi로 전환
5. USB Wi-Fi 제거 또는 NIC 비활성화
6. WLAN ID 1·2회 일시 미확인 후 동일 ID 복구
7. WLAN ID 3회 연속 미확인
8. 절전·최대 절전·Modern Standby
9. 드라이버 재시작과 카운터 재설정
10. 높은 CPU 부하 또는 장시간 스케줄러 지연
11. 내부 DIRECT 다운로드
12. 외부 PROXY 다운로드
13. PAC·WPAD·407·TLS 검사
14. 전용·통합 보고서의 실제 개인정보 마스킹
15. Release 자산의 SHA-256 재검증
