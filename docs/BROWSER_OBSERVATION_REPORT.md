# 브라우저 관찰 전용 구조화 보고서

`관찰 보고서` 탭은 가장 최근 브라우저 다운로드 관찰의 상태, 구조화 종료 원인, 처리량 요약과 시간축 샘플을 로컬 파일로 저장합니다.

통합 로컬 진단 보고서와 달리 이 보고서는 브라우저 관찰 결과만 독립적으로 전달하거나 비교할 때 사용합니다.

## 생성 파일

```text
WlanBrowserObservation_yyyyMMdd_HHmmss.json
WlanBrowserObservation_yyyyMMdd_HHmmss.csv
WlanBrowserObservation_yyyyMMdd_HHmmss.html
WlanBrowserObservation_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

- JSON: 프로그램 또는 후속 로컬 분석을 위한 구조화 데이터
- CSV: `section,key,value` 구조의 요약·시간축 데이터
- HTML: 외부 리소스 없이 사람이 읽을 수 있는 단일 보고서
- SHA-256: JSON·CSV·HTML 세 파일의 무결성 확인값

같은 초에 여러 번 생성하면 `_1`, `_2`와 같은 suffix를 붙여 기존 파일을 덮어쓰지 않습니다.

## 통신 경계

보고서 생성은 다음 값만 사용합니다.

- 현재 앱 메모리의 가장 최근 `BrowserObservationResult`
- 로컬 파일 시스템
- 보고서 생성 시각

다음 작업은 수행하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 평가
- 프록시 연결
- 외부 API 또는 AI 호출
- 텔레메트리·자동 오류 전송
- 결과 업로드
- 자동 업데이트

보고서 생성 중에는 내부·외부 다운로드 측정과 브라우저 관찰을 새로 실행하지 않습니다.

## 종료 원인

보고서는 `status`와 `terminationReason`을 별도 필드로 저장합니다.

| 종료 원인 | 의미 |
|---|---|
| `Completed` | 계획된 관찰 절차 정상 완료 |
| `CanceledByUser` | 사용자가 관찰 중지 |
| `AdapterChanged` | 관찰 중 Native WLAN 물리 NIC 변경 |
| `AdapterUnavailable` | 고정 Wi-Fi가 Down·제거됐거나 통계 조회 불가 |
| `CounterProviderMismatch` | 고정 WLAN ID와 카운터 공급자 ID 불일치 |
| `SystemSuspend` | Windows 절전·최대 절전 전환으로 중단 |
| `TimingDiscontinuity` | 카운터 샘플 시간 간격이 허용 범위를 벗어남 |
| `InvalidOptions` | 관찰 시간 또는 샘플 간격 설정 오류 |
| `UnsupportedPlatform` | 필요한 Windows API를 사용할 수 없는 환경 |
| `NoWirelessConnection` | 관찰 시작 시 연결 WLAN 없음 |
| `Failed` | 더 구체적인 범주로 분류되지 않은 오류 |

명시적인 종료 원인이 없는 이전 결과에는 `EffectiveTerminationReason`을 적용합니다. 예를 들어 이전 `Canceled` 결과도 `CanceledByUser`로 기록합니다.

`Completed`는 관찰 절차가 끝났다는 뜻이며 WLAN·프록시·인터넷 서비스 품질이 정상이라는 의미는 아닙니다.

## 요약 구조

```text
startedAt
completedAt
observedSeconds
baselineReceiveMbps
averageAdjustedReceiveMbps
peakAdjustedReceiveMbps
totalReceiveBytes
activeSampleCount
pauseCount
suddenDropCount
bssidChangeCount
adapterChangeCount
counterResetCount
wlanDisconnectedSampleCount
confidence
summaryMessage
limitation
samples[]
```

`observedSeconds`는 계산 가능한 활성 관찰 샘플의 실제 간격 합입니다. 기준 수집 구간이나 처리량 계산에서 제외된 시간 단절 구간은 포함하지 않습니다.

## 시간축 샘플

각 샘플에는 다음 값만 저장합니다.

- 순번과 타임스탬프
- 샘플 간격
- 기준 수집 또는 활성 관찰 구분
- 수신·송신 바이트 델타
- 원시 Rx·Tx Mbps
- 백그라운드 기준치를 제외한 Rx Mbps
- RSSI
- Rx·Tx PHY 링크 속도
- 잘못된 간격 여부
- 물리 NIC 변경 여부
- 카운터 재설정 여부
- WLAN 미확인 여부
- BSSID 변경 여부
- 정지·급락 여부
- 마스킹된 메모

샘플의 `InterfaceId`와 BSSID 원문은 모델에 복사하지 않습니다.

## 숫자 안전 경계

보고서 생성 시 다음 비정상 값을 정규화합니다.

- `NaN`, 양·음의 무한대 처리량 → `null` 또는 0
- 음수 수신·송신 바이트 델타 → 0
- 음수 총 수신량 → 0
- 음수 샘플 간격 → 0

이 경계는 손상된 합성 입력이나 향후 코드 회귀 때문에 JSON 직렬화가 실패하거나 음수 처리량이 노출되는 것을 막는 방어 계층입니다. 실제 관찰 계산기는 이보다 앞에서 카운터 재설정과 시간 단절을 판정합니다.

## 개인정보 경계

전용 보고서 모델에는 다음 원문을 포함하지 않습니다.

- SSID
- BSSID
- 물리·가상 인터페이스 ID와 전체 GUID
- 인터페이스 이름과 설명
- IPv4·IPv6 주소
- MAC 주소
- 기본 게이트웨이와 DNS 주소
- 다운로드 URL과 파일 이름
- 프록시 호스트와 PAC URL
- 브라우저 쿠키·세션·인증 헤더

`BrowserObservationResult.Message`, 요약 메시지·한계와 샘플 메모에는 다음 처리를 순서대로 적용합니다.

1. 초기 WLAN의 ID·설명·SSID·BSSID 정확값 치환
2. 임의 텍스트 안의 GUID 형식 제거
3. 기존 URL·IP·MAC·이메일·Windows 사용자 경로 마스킹

초기 WLAN 객체 자체는 보고서 문서에 저장하지 않습니다.

마스킹은 보조 수단이므로 회사 밖으로 공유하기 전에 JSON·CSV·HTML을 직접 다시 확인합니다.

## CSV 안전성

CSV는 다음 구조를 사용합니다.

```text
section,key,value
metadata,...
observation,...
summary,...
sample.1,...
limitation,...
```

값이 `=`, `+`, `-`, `@`, tab 또는 carriage return으로 시작하면 앞에 apostrophe를 붙여 스프레드시트 수식으로 실행되지 않도록 합니다.

## HTML 안전성

HTML에는 다음 보호를 적용합니다.

- 동적 값 HTML 인코딩
- Content Security Policy
- 외부 JavaScript 없음
- 외부 CSS 없음
- 웹폰트 없음
- 외부 이미지 없음
- iframe 없음
- form action 없음
- 화면·인쇄용 반응형 레이아웃

HTML을 자동으로 브라우저에 전송하거나 원격 리소스를 불러오지 않습니다.

## SHA-256

`_SHA256SUMS.txt`에는 JSON·CSV·HTML 세 파일의 SHA-256을 기록합니다.

```powershell
Get-FileHash .\WlanBrowserObservation_*.json -Algorithm SHA256
Get-FileHash .\WlanBrowserObservation_*.csv -Algorithm SHA256
Get-FileHash .\WlanBrowserObservation_*.html -Algorithm SHA256
```

보고서를 전달한 뒤 파일이 변경되지 않았는지 확인할 수 있습니다.

## 권장 사용 순서

1. `브라우저 관찰` 탭에서 관찰을 실행합니다.
2. 종료 상태와 구조화 종료 원인을 확인합니다.
3. `관찰 보고서` 탭을 엽니다.
4. `브라우저 관찰 보고서 생성`을 누릅니다.
5. JSON·CSV·HTML·SHA-256 파일이 생성됐는지 확인합니다.
6. 최신 HTML을 열어 처리량·종료 원인·시간축 플래그를 검토합니다.
7. 공유 전 민감정보가 남지 않았는지 직접 확인합니다.
8. 필요하면 SHA-256을 별도로 재계산합니다.

앱을 종료하면 가장 최근 관찰 결과가 사라지므로 필요한 보고서는 종료 전에 생성합니다.

## 자동 검증

ReportSmoke는 다음을 확인합니다.

- 명시 종료 원인과 이전 결과의 fallback 종료 원인
- JSON·CSV·HTML의 동일한 상태·종료 원인
- 시간축 샘플과 카운터 재설정·BSSID 변경 횟수
- CSV 수식 비활성화
- HTML CSP와 외부 script·iframe·stylesheet 부재
- GUID·인터페이스 설명·SSID·BSSID·이메일·IP·URL 비노출
- `NaN`·무한대·음수 숫자 정규화
- JSON 직렬화 가능성
- 네 파일 생성과 SHA-256 재계산 일치
