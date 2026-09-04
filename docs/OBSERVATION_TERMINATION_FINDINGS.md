# 브라우저 관찰 종료 원인별 고정 판정

통합 로컬 진단 보고서는 `browserObservation.terminationReason`을 단순 표시값으로만 저장하지 않고, 고정 규칙에 따라 별도의 `ReportFinding`으로 해석합니다.

이 판정은 AI나 외부 분석 서비스가 아니라 `ReportFindingEngine`의 결정론적 switch 규칙으로 생성됩니다. 같은 구조화 입력에는 같은 코드·심각도·설명·다음 확인 항목이 생성됩니다.

## 상태와 종료 원인

```text
status
  → 결과의 가용 수준: Success, PartialSuccess, Canceled, Failed 등

terminationReason
  → 실행이 끝난 직접 원인: Completed, AdapterChanged 등
```

종료 원인을 판정할 때는 `terminationReason`을 우선합니다. 예를 들어 다음 결과는 `status=Success`라고 적혀 있어도 `AdapterChanged` Finding을 생성합니다.

```text
status: Success
terminationReason: AdapterChanged
```

이는 구조화 종료 원인이 실제 종료 경계를 더 구체적으로 설명하기 때문입니다. 입력 간 불일치 자체를 자동으로 정상 상태로 바꾸지는 않습니다.

## 판정 코드

| 종료 원인 | Finding 코드 | 심각도 |
|---|---|---|
| `Completed` | `BROWSER_OBSERVATION_COMPLETED` | Information |
| `CanceledByUser` | `BROWSER_OBSERVATION_CANCELED_BY_USER` | Information |
| `AdapterChanged` | `BROWSER_OBSERVATION_ADAPTER_CHANGED` | Warning |
| `AdapterUnavailable` | `BROWSER_OBSERVATION_ADAPTER_UNAVAILABLE` | Warning |
| `CounterProviderMismatch` | `BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH` | Warning |
| `SystemSuspend` | `BROWSER_OBSERVATION_SYSTEM_SUSPEND` | Warning |
| `TimingDiscontinuity` | `BROWSER_OBSERVATION_TIMING_DISCONTINUITY` | Warning |
| `InvalidOptions` | `BROWSER_OBSERVATION_INVALID_OPTIONS` | Warning |
| `UnsupportedPlatform` | `BROWSER_OBSERVATION_UNSUPPORTED_PLATFORM` | Warning |
| `NoWirelessConnection` | `BROWSER_OBSERVATION_NO_WLAN_CONNECTION` | Warning |
| `Failed` | `BROWSER_OBSERVATION_FAILED` | Warning |
| 알 수 없는 값 | `BROWSER_OBSERVATION_TERMINATION_UNKNOWN` | Warning |

## 정상 완료

`BROWSER_OBSERVATION_COMPLETED`는 관찰 절차가 계획된 종료 지점까지 실행됐다는 뜻입니다.

다음을 의미하지는 않습니다.

- WLAN 성능 정상
- 애플리케이션 속도 정상
- 프록시·인터넷 경로 정상
- 로밍·정지·급락 없음

RSSI, PHY 링크 속도, 내부·외부 다운로드, 신뢰도, BSSID 변경과 처리량 이벤트를 함께 확인합니다.

## 사용자 중지

`BROWSER_OBSERVATION_CANCELED_BY_USER`는 장애 강제 종료와 구분되는 정보성 판정입니다.

중지 전 샘플은 남을 수 있지만 관찰 시간이 짧거나 다운로드의 전체 구간을 포함하지 않으면 대표성이 낮습니다. 비교가 필요하면 같은 조건에서 계획된 시간까지 다시 실행합니다.

## 물리 Wi-Fi 변경

`BROWSER_OBSERVATION_ADAPTER_CHANGED`는 관찰 중 Native WLAN 인터페이스 ID가 시작 시 고정한 물리 NIC와 달라졌음을 나타냅니다.

프로그램은 서로 다른 NIC의 누적 카운터를 한 결과에 합치지 않습니다. 가능한 원인은 다음과 같습니다.

- 내장 Wi-Fi와 USB Wi-Fi 사이 전환
- 무선 드라이버 재시작
- WLAN AutoConfig 연결 재구성
- 인터페이스 ID 조회의 지속적 변화

원인 확정에는 Windows WLAN 보고서, 장치 관리자, 드라이버 이벤트와 다중 어댑터 진단이 필요합니다.

## 고정 Wi-Fi 사용 불가

`BROWSER_OBSERVATION_ADAPTER_UNAVAILABLE`는 시작 시 고정한 NIC가 Down·제거됐거나 누적 통계를 읽을 수 없음을 나타냅니다.

- USB Wi-Fi 분리
- 장치 비활성화
- 절전 전원 관리
- 드라이버 재시작
- 권한 또는 EDR 제한

이 결과를 낮은 Mbps의 정상 관찰로 해석하지 않습니다.

## 카운터 공급자 불일치

`BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH`는 Native WLAN의 물리 NIC와 `NetworkInterface` 카운터 후보를 안전하게 같은 장치로 확정하지 못했음을 나타냅니다.

프로그램은 이 상황에서 첫 번째 활성 Wi-Fi나 설명이 비슷한 다른 NIC로 자동 전환하지 않습니다.

다음 항목을 확인합니다.

- 내장·USB Wi-Fi 동시 활성
- Wi-Fi Direct 가상 어댑터
- 중복 인터페이스 설명
- VPN·보안 에이전트 가상 NIC
- 드라이버별 ID 표현

## 시스템 절전

`BROWSER_OBSERVATION_SYSTEM_SUSPEND`는 Windows 절전·최대 절전 전환으로 관찰이 중단됐음을 나타냅니다.

전원 전환 전후의 긴 시간 간격과 누적 카운터를 한 세션으로 결합하지 않은 안전 종료입니다. 절전이 발생하지 않는 조건에서 다시 실행합니다.

## 샘플 시간 연속성 중단

`BROWSER_OBSERVATION_TIMING_DISCONTINUITY`는 카운터 샘플 간격이 허용 상한을 벗어났음을 나타냅니다.

가능한 원인은 다음과 같습니다.

- 절전 메시지 누락
- Modern Standby
- 무선 드라이버 정지
- 높은 CPU 부하
- 운영체제 스케줄러 지연
- 디버거 중단

비정상 구간의 바이트 델타는 처리량 통계에 포함하지 않는 것이 원칙입니다.

## 설정·환경·일반 오류

- `BROWSER_OBSERVATION_INVALID_OPTIONS`: 기준 시간·관찰 시간·샘플 간격 오류
- `BROWSER_OBSERVATION_UNSUPPORTED_PLATFORM`: 필요한 Windows API를 사용할 수 없는 환경
- `BROWSER_OBSERVATION_NO_WLAN_CONNECTION`: 시작 시 연결된 Native WLAN 없음
- `BROWSER_OBSERVATION_FAILED`: 더 구체적인 구조화 범주로 분류되지 않은 오류

## 알 수 없는 종료 원인

보고서의 종료 원인이 지원 enum으로 해석되지 않으면 `BROWSER_OBSERVATION_TERMINATION_UNKNOWN`을 생성합니다.

보안상 알 수 없는 원문을 Finding의 Evidence에 그대로 반사하지 않습니다. 원문에 CSV 수식, URL, 사용자 데이터 또는 임의 문자열이 들어 있어도 판정에는 다음 일반 정보만 기록합니다.

- 브라우저 관찰 상태
- 지원되는 종료 원인으로 해석할 수 없다는 사실
- 현재 프로그램에서 보고서를 다시 생성하라는 조치

## 기존 보고서 호환성

`terminationReason`이 없는 이전 `ReportObservationSection`에는 특정 종료 원인 Finding을 추정하지 않습니다. 기존 신뢰도·BSSID 변경·정지·급락 규칙은 계속 적용됩니다.

현재 앱의 `ReportObservationMapper`는 기존 `BrowserObservationResult`도 `EffectiveTerminationReason`으로 안전하게 변환하므로, 새로 생성하는 통합 보고서에는 정상적으로 종료 원인이 포함됩니다.

## 다른 관찰 Finding과의 관계

종료 원인 Finding은 다음 기존 규칙과 함께 존재할 수 있습니다.

- `BROWSER_OBSERVATION_LOW_CONFIDENCE`
- `BSSID_CHANGE_WITH_THROUGHPUT_DROP`
- `BROWSER_THROUGHPUT_INTERRUPTION`

예를 들어 다음과 같이 두 개 이상의 판정이 생성될 수 있습니다.

```text
BROWSER_OBSERVATION_ADAPTER_CHANGED
BROWSER_OBSERVATION_LOW_CONFIDENCE
```

코드 기준 중복 제거를 적용하므로 같은 종료 원인 Finding은 한 번만 생성됩니다.

## 데이터·통신 경계

판정 엔진은 이미 마스킹된 로컬 보고서 모델만 읽습니다. 다음 작업은 수행하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 연결
- 외부 API 또는 AI
- 텔레메트리
- 자동 오류 전송
- 결과 업로드

Finding에는 SSID·BSSID, 인터페이스 이름·전체 GUID, IP·MAC·게이트웨이·DNS, URL과 프록시 호스트 원문을 추가하지 않습니다.

## 자동 검증

ReportSmoke는 다음을 확인합니다.

- 알려진 종료 원인마다 정확한 Finding 코드와 심각도 생성
- 구조화 종료 원인이 `status`보다 우선
- 알 수 없는 원문을 Finding에 반사하지 않음
- 종료 원인이 없는 이전 보고서에 특정 원인을 추정하지 않음
- 저신뢰도 등 다른 관찰 Finding과 함께 있을 때도 종료 원인 Finding이 중복되지 않음
