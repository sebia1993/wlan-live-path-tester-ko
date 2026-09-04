# 브라우저 관찰 전용 보고서

`관찰 보고서` 탭은 가장 최근 브라우저 다운로드 관찰의 종료 원인, 처리량 요약과 시간축 샘플을 로컬 파일로 저장합니다.

## 생성 파일

```text
WlanBrowserObservation_yyyyMMdd_HHmmss.json
WlanBrowserObservation_yyyyMMdd_HHmmss.csv
WlanBrowserObservation_yyyyMMdd_HHmmss.html
WlanBrowserObservation_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

- JSON: 프로그램이나 후속 로컬 분석에서 사용할 구조화 데이터
- CSV: `section,key,value` 형식의 요약·샘플 데이터
- HTML: 외부 리소스 없는 사람이 읽기 쉬운 단일 보고서
- SHA-256: JSON·CSV·HTML의 무결성 확인값

보고서 생성은 현재 앱 메모리의 관찰 결과와 로컬 파일 시스템만 사용합니다. DNS·HTTP·PAC/WPAD·프록시·외부 API 요청을 만들지 않습니다.

## 구조화 종료 원인

| 값 | 의미 |
|---|---|
| `Completed` | 계획한 관찰 시간을 정상 완료 |
| `CanceledByUser` | 사용자가 중지 버튼을 누름 |
| `AdapterChanged` | 관찰 중 Native WLAN 물리 Wi-Fi 인터페이스가 변경됨 |
| `AdapterUnavailable` | 고정한 Wi-Fi가 Down·소멸하거나 통계를 읽을 수 없음 |
| `CounterProviderMismatch` | 고정 ID와 대응되는 물리 Wi-Fi 카운터를 찾지 못하거나 후보가 중복됨 |
| `InvalidOptions` | 기준 시간·관찰 시간·샘플 간격 설정 오류 |
| `UnsupportedPlatform` | Windows가 아니거나 필요한 API를 사용할 수 없음 |
| `NoWirelessConnection` | 관찰 시작 시 연결된 Native WLAN이 없음 |
| `Failed` | 위 범주로 분류되지 않는 실행 오류 |
| `None` | 이전 버전 호출이나 종료 원인이 명시되지 않은 결과 |

기존 `BrowserObservationStatus`도 함께 기록합니다. `PartialSuccess`는 일부 샘플이 유효하지만 종료 전에 NIC 변경·사용 불가 등이 발생했다는 의미일 수 있으므로 `terminationReason`과 함께 확인합니다.

## 기록 항목

### 관찰 요약

- 시작·종료 시각과 관찰 시간
- 백그라운드 기준 수신 Mbps
- 기준치 제외 평균·최고 수신 Mbps
- 총 수신 바이트
- 활성 샘플 수
- 정지·급락 횟수
- BSSID 변경 횟수
- 인터페이스 변경 횟수
- 카운터 재설정 횟수
- WLAN 미확인 샘플 수
- 신뢰도와 판단 한계

### 시간축 샘플

- 샘플 순번과 로컬 시각
- 기준 수집 또는 실제 관찰 구분
- 수신·송신 바이트 차이
- 원시 Rx·Tx Mbps와 기준치 제외 Rx Mbps
- RSSI
- Rx·Tx PHY 링크 속도
- 잘못된 간격, NIC 변경, 카운터 재설정, WLAN 미확인, BSSID 변경, 정지와 급락 플래그

## 포함하지 않는 정보

보고서 모델 자체에서 다음 값을 제외합니다.

- SSID
- BSSID 원문
- 인터페이스 ID와 전체 GUID
- 인터페이스 이름과 설명
- IPv4·IPv6 주소
- MAC 주소
- 기본 게이트웨이와 DNS 주소
- 브라우저 다운로드 URL과 파일 이름
- 쿠키·세션·인증 헤더

BSSID가 바뀌었는지는 Boolean 플래그와 총 횟수로만 기록합니다. 텍스트 메시지와 샘플 메모에는 기존 민감정보 마스킹을 적용합니다.

## CSV·HTML 안전 경계

- CSV의 `=`, `+`, `-`, `@` 수식 시작 문자를 비활성화합니다.
- HTML의 동적 텍스트는 HTML 인코딩합니다.
- HTML은 Content Security Policy를 포함합니다.
- 외부 JavaScript·CSS·웹폰트·이미지·iframe을 포함하지 않습니다.
- 보고서를 자동으로 열거나 외부로 업로드하지 않습니다.

## 해석 예

### `Completed`

관찰이 계획대로 끝났습니다. 다만 Wi-Fi 인터페이스 전체 트래픽이라는 한계 때문에 신뢰도는 최대 Medium입니다.

### `CanceledByUser`

사용자가 중지했습니다. 결과에 남은 샘플만 해석하며 관찰 시간이 짧으면 신뢰도가 낮을 수 있습니다.

### `AdapterChanged`

내장 Wi-Fi에서 USB Wi-Fi로 전환되는 등 물리 인터페이스가 달라졌습니다. 서로 다른 NIC 카운터를 결합하지 않고 종료했으므로 변경 이전 샘플만 참고합니다.

같은 NIC에서 AP가 바뀌는 BSSID 로밍은 `AdapterChanged`가 아니라 `bssidChanged` 플래그로 기록합니다.

### `AdapterUnavailable`

고정한 NIC가 비활성화·제거됐거나 통계를 읽지 못했습니다. 드라이버 재시작, USB 분리, 절전 복귀, 권한·EDR 제한을 확인합니다.

### `CounterProviderMismatch`

Native WLAN에서 고정한 ID와 로컬 카운터 공급자의 물리 Wi-Fi 후보가 일치하지 않습니다. 다른 활성 Wi-Fi를 임의 선택하지 않은 안전 차단입니다.

## 실제 환경 검증

- 정상 관찰 완료 후 `Completed` 보고서 생성
- 사용자 중지 후 `CanceledByUser` 보고서 생성
- 관찰 중 다른 물리 Wi-Fi로 전환해 `AdapterChanged` 확인
- 고정 Wi-Fi를 비활성화·제거해 `AdapterUnavailable` 확인
- 내장·USB Wi-Fi 중 고정 ID와 다른 NIC만 남겨 `CounterProviderMismatch` 확인
- 같은 NIC의 BSSID 로밍이 `AdapterChanged`로 오인되지 않는지 확인
- JSON·CSV·HTML에서 SSID·BSSID·전체 GUID·IP·MAC가 남지 않는지 확인
- `SHA256SUMS` 값과 실제 파일 해시 비교

실제 사내 식별정보가 포함될 가능성을 완전히 배제할 수 없으므로 회사 밖으로 공유하기 전에는 내용을 직접 다시 확인합니다.
