# 브라우저 관찰 종료 원인 보고서 행렬

브라우저 관찰은 결과 상태와 직접 종료 원인을 분리해 저장합니다. 이 문서는 모든 지원 종료 원인이 관찰 전용 보고서, 통합 보고서와 고정 Finding에서 동일하게 유지되는지 정의합니다.

## 전체 행렬

| 결과 상태 | 종료 원인 | Finding 코드 | 심각도 |
|---|---|---|---|
| `Success` | `Completed` | `BROWSER_OBSERVATION_COMPLETED` | Information |
| `Canceled` | `CanceledByUser` | `BROWSER_OBSERVATION_CANCELED_BY_USER` | Information |
| `AdapterChanged` | `AdapterChanged` | `BROWSER_OBSERVATION_ADAPTER_CHANGED` | Warning |
| `AdapterUnavailable` | `AdapterUnavailable` | `BROWSER_OBSERVATION_ADAPTER_UNAVAILABLE` | Warning |
| `AdapterUnavailable` | `WlanIdentityUnavailable` | `BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE` | Warning |
| `CounterProviderMismatch` | `CounterProviderMismatch` | `BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH` | Warning |
| `Canceled` | `SystemSuspend` | `BROWSER_OBSERVATION_SYSTEM_SUSPEND` | Warning |
| `PartialSuccess` | `TimingDiscontinuity` | `BROWSER_OBSERVATION_TIMING_DISCONTINUITY` | Warning |
| `InvalidOptions` | `InvalidOptions` | `BROWSER_OBSERVATION_INVALID_OPTIONS` | Warning |
| `UnsupportedPlatform` | `UnsupportedPlatform` | `BROWSER_OBSERVATION_UNSUPPORTED_PLATFORM` | Warning |
| `NoWirelessConnection` | `NoWirelessConnection` | `BROWSER_OBSERVATION_NO_WLAN_CONNECTION` | Warning |
| `Failed` | `Failed` | `BROWSER_OBSERVATION_FAILED` | Warning |

상태는 결과의 가용 수준을 나타내고 종료 원인은 실행이 끝난 직접 이유를 나타냅니다. 예를 들어 `SystemSuspend`는 사용자 중지와 같은 `Canceled` 상태를 사용하지만 직접 원인과 심각도는 다릅니다.

## 관찰 전용 보고서

`BrowserObservationSessionReportWriter`는 다음 필드를 기록합니다.

```text
status
terminationReason
terminationDisplay
message
summary
limitations
```

각 종료 원인은 JSON·CSV·단일 HTML에 동일하게 나타나야 합니다.

- JSON: 구조화 필드
- CSV: `observation,terminationReason`과 `observation,terminationDisplay`
- HTML: enum 값과 한국어 표시

샘플이 없는 시작 실패·취소·절전 결과도 보고서 생성이 가능해야 합니다.

## 통합 보고서

`ReportObservationMapper`는 같은 값을 다음에 기록합니다.

```text
browserObservation.status
browserObservation.terminationReason
```

통합 HTML은 마스킹된 관찰 메시지에 사람이 읽을 수 있는 한국어 종료 설명과 enum을 한 번만 추가합니다.

## Finding 표현

`ReportFindingPipeline`은 기존 `ReportFindingEngine` 규칙을 모두 유지하고 WLAN ID 연속성 규칙까지 포함한 최종 Finding 집합을 반환합니다.

- JSON·CSV: 머신용 Finding 코드와 구조화 필드
- HTML: 사람이 읽는 제목·심각도·근거·해석·조치·한계

구조화 종료 원인이 있으면 다음 일반 판정은 함께 존재하면 안 됩니다.

```text
NO_CLEAR_FAILURE_PATTERN
```

각 종료 원인 Finding은 정확히 한 개여야 합니다.

## 자동 행렬 완전성

테스트는 하드코딩한 사례만 실행하는 데 그치지 않고 실제 enum 전체를 비교합니다.

```text
Enum.GetValues<BrowserObservationTerminationReason>()
  - None
  ↓
행렬의 Reason 집합과 비교
```

다음 조건에서 실패합니다.

- 새 enum이 추가됐지만 행렬에 사례가 없음
- 같은 종료 원인이 행렬에 중복됨
- 예상 Finding 코드가 없거나 여러 개임
- 심각도가 계약과 다름
- 구조화 종료 원인과 `NO_CLEAR_FAILURE_PATTERN`이 함께 존재함

따라서 향후 종료 원인을 추가할 때 전용 보고서, 통합 보고서와 Finding 계약을 함께 갱신하지 않으면 CI가 통과하지 않습니다.

## 개인정보 회귀

각 행렬 사례의 메시지에는 합성 이메일·IP·URL·GUID를 넣고, 초기 WLAN에는 합성 SSID·BSSID·인터페이스 설명을 넣습니다.

다음 모든 출력에서 원문이 없어야 합니다.

- 관찰 전용 JSON
- 관찰 전용 CSV
- 관찰 전용 HTML
- 통합 JSON
- 통합 CSV
- 통합 HTML

종료 원인은 고정 enum이므로 사용자·장비·네트워크 식별정보를 포함하지 않습니다.

## 통신 경계

행렬 테스트는 메모리 내 합성 결과만 사용합니다.

다음 작업은 수행하지 않습니다.

- Native WLAN API
- NetworkInterface 카운터
- DNS
- HTTP/HTTPS
- PAC/WPAD 또는 프록시
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

## 해석 원칙

- `Completed`는 절차 완료이며 서비스 정상 보장이 아닙니다.
- `CanceledByUser`는 장애가 아니라 사용자 동작입니다.
- `AdapterChanged`는 실제 다른 유효 WLAN GUID가 확인된 경우입니다.
- `WlanIdentityUnavailable`은 고정 카운터는 유지됐지만 WLAN 연결 ID를 연속 임계 횟수 확인하지 못한 경우입니다.
- `CounterProviderMismatch`는 고정 ID 요청과 카운터 공급자 결과가 일치하지 않은 경우입니다.
- `SystemSuspend`는 명시적인 Windows 전원 전환 취소입니다.
- `TimingDiscontinuity`는 실제 카운터 시간 간격이 허용 범위를 벗어난 보조 안전망입니다.

상태와 종료 원인, 처리량 요약, 이벤트 횟수와 Finding을 함께 판단합니다.
