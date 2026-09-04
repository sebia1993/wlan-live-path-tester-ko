# 브라우저 관찰 구조화 종료 원인

브라우저 다운로드 관찰은 화면 문장과 별개로 종료 원인을 고정된 enum 값으로 기록합니다. 자동 보고서와 후속 분석은 한국어 문장을 파싱하지 않고 이 값을 사용합니다.

## 결과 계약

기존 네 값 결과 생성자는 유지됩니다.

```text
BrowserObservationResult(
  Status,
  Summary,
  InitialWlan,
  Message)
```

구조화 종료 원인이 필요한 런타임 호출은 다섯 번째 값을 사용합니다.

```text
BrowserObservationResult(
  Status,
  Summary,
  InitialWlan,
  Message,
  TerminationReason)
```

기존 네 값 deconstruction도 유지됩니다. 따라서 기존 호출 코드를 깨지 않고 다음 두 속성을 추가합니다.

- `TerminationReason`: 런타임이 명시적으로 기록한 값
- `EffectiveTerminationReason`: 명시값이 없을 때 기존 `Status`에서 안전하게 해석한 값

## 종료 원인

| 값 | 의미 |
|---|---|
| `None` | 명시값과 안전한 상태 매핑이 없음 |
| `Completed` | 계획된 관찰 정상 완료 |
| `CanceledByUser` | 사용자가 중지 버튼을 누름 |
| `AdapterChanged` | 관찰 중 Native WLAN 물리 인터페이스 변경 |
| `AdapterUnavailable` | 고정 Wi-Fi가 Down·제거됐거나 통계를 읽지 못함 |
| `CounterProviderMismatch` | 고정 WLAN ID와 카운터 공급자 ID가 불일치하거나 모호함 |
| `SystemSuspend` | Windows 절전·최대 절전 전환으로 중단 |
| `TimingDiscontinuity` | 카운터 샘플 시간 간격이 허용 범위를 벗어남 |
| `InvalidOptions` | 관찰 시간·간격 설정 오류 |
| `UnsupportedPlatform` | 지원하지 않는 운영체제 또는 API 환경 |
| `NoWirelessConnection` | 관찰 시작 시 연결된 Native WLAN이 없음 |
| `Failed` | 위 범주로 분리되지 않은 실행 오류 |

`SystemSuspend`와 `TimingDiscontinuity`는 후속 전원·시간 연속성 보호 기능이 명시적으로 지정할 수 있도록 계약에 먼저 포함합니다.

## 현재 러너의 명시 매핑

```text
정상 관찰 종료                 → Completed
사용자 CancellationToken 취소 → CanceledByUser
Native WLAN NIC 변경           → AdapterChanged
고정 NIC Down·통계 실패        → AdapterUnavailable
카운터 ID 불일치               → CounterProviderMismatch
설정 오류                      → InvalidOptions
Windows 미지원                 → UnsupportedPlatform
연결 WLAN 없음                 → NoWirelessConnection
예외 종료                      → Failed
```

정상 실행이 끝났지만 유효한 활성 샘플이 부족해 `PartialSuccess`인 경우에도 종료 과정 자체는 `Completed`로 명시됩니다. 반대로 예외 전에 일부 샘플이 남아 `PartialSuccess`가 되더라도 종료 원인은 `Failed`입니다. 이 구분 때문에 `Status`와 `TerminationReason`을 함께 봐야 합니다.

## 화면 표시

관찰 종료 후 다음 형식으로 표시합니다.

```text
종료 원인: 정상 완료 (Completed)
종료 원인: 사용자 중지 (CanceledByUser)
종료 원인: 관찰 Wi-Fi 인터페이스 변경 (AdapterChanged)
종료 원인: 고정 ID와 카운터 공급자 불일치 (CounterProviderMismatch)
```

표시 문구는 중앙 정책에서 생성하므로 UI·전용 보고서·통합 보고서가 같은 의미를 사용하게 됩니다.

## 데이터·통신 경계

종료 원인 구조화는 이미 결정된 로컬 상태를 enum으로 저장하는 작업입니다. 다음 통신을 추가하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 조회
- 외부 API
- AI 또는 로컬 AI
- 텔레메트리
- 자동 오류 전송
- 결과 업로드

전체 인터페이스 GUID, SSID, BSSID, IP, MAC, 게이트웨이와 URL은 종료 원인 필드에 포함되지 않습니다.

## 보고서 사용 원칙

- `Status`는 결과 가용성과 성공 수준을 나타냅니다.
- `TerminationReason`은 실행이 왜 끝났는지 나타냅니다.
- 일부 샘플이 있어도 `AdapterChanged`, `AdapterUnavailable`, `CounterProviderMismatch`, `SystemSuspend`, `TimingDiscontinuity`이면 변경 이후 구간을 같은 세션으로 합치지 않습니다.
- `CanceledByUser`는 장애가 아니라 사용자 동작입니다.
- `Completed`는 관찰 완료를 뜻하며 서비스 품질 정상 판정을 뜻하지 않습니다.

## 자동 검증

결정론적 테스트는 다음을 확인합니다.

- 기존 네 값 생성자와 deconstruction 호환
- 명시 종료 원인 저장
- 기존 상태의 안전한 fallback 매핑
- 명시값이 fallback보다 우선
- 한국어 표시 문구의 안정성
- 정상 `PartialSuccess`와 예외 `PartialSuccess`를 런타임에서 다른 종료 원인으로 기록
