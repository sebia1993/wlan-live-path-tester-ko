# 브라우저 관찰 주입식 런타임 테스트 경계

브라우저 관찰 러너는 실제 Windows 실행에서는 기존 Native WLAN·NetworkInterface·시스템 시간·Task.Delay를 그대로 사용합니다. 자동 검증에서는 동일 러너에 합성 런타임을 주입해 실제 Wi-Fi 장치, 외부망, DNS 또는 회사 프록시 없이 전체 상태 전이를 재현합니다.

## 목적

기존 단위 테스트는 인터페이스 선택 정책이나 샘플 계산기를 개별적으로 확인할 수 있지만 다음 전체 흐름을 한 번에 증명하지는 못했습니다.

```text
초기 Native WLAN 확인
  ↓
WLAN ID와 카운터 ID 고정
  ↓
기준 샘플 수집
  ↓
활성 다운로드 구간 관찰
  ↓
NIC 변경·제거·공급자 불일치 판정
  ↓
요약 및 구조화 종료 원인
```

주입식 런타임은 제품 러너의 조건문과 결과 생성 경로를 그대로 실행하면서 운영체제 의존 부분만 합성 데이터로 바꿉니다.

## 런타임 계약

`IBrowserObservationRuntime`은 다음 의존성을 제공합니다.

```text
IsSupportedPlatform
UtcNow
ReadWlan()
ReadWlanIdentity()
ReadCounter(preferredId, preferredDescription, selectionMode)
DelayAsync(delay, cancellationToken)
```

### 실제 Windows 구현

기본 `BrowserObservationRunner()`는 내부적으로 `WindowsBrowserObservationRuntime`을 사용합니다.

- `NativeWlanReader.ReadCurrent()`
- `WlanInterfaceIdentityReader.ReadCurrent()`
- `WindowsInterfaceCounterReader.ReadCurrent()`
- `DateTimeOffset.UtcNow`
- `Task.Delay()`

따라서 기존 앱 생성 코드와 실행 동작은 변경되지 않습니다.

### 합성 테스트 구현

Windows Smoke 테스트는 `BrowserObservationRunner(IBrowserObservationRuntime)` 생성자에 메모리 큐 기반 런타임을 전달합니다.

- WLAN 상태를 순서대로 반환
- 카운터 스냅샷 또는 오류를 순서대로 반환
- 실제 대기 없이 delay 호출과 CancellationToken을 검증
- 마지막 WLAN·카운터 타임스탬프를 합성 현재 시각으로 사용
- 카운터 요청의 고정 ID·설명·선택 모드를 기록

## 자동 검증 시나리오

### 정상 완료

2초 기준 수집과 5초 활성 관찰을 500ms 간격으로 실행합니다.

```text
기준 샘플 4개
활성 샘플 10개
전체 샘플 14개
```

모든 WLAN·카운터 ID를 같은 물리 NIC로 유지하고 기준 1Mbps, 활성 원시 101Mbps를 합성합니다. 기대 결과는 다음과 같습니다.

- `Status=Success`
- `TerminationReason=Completed`
- 조정 평균 약 100Mbps
- `Confidence=Medium`
- 초기 및 후속 카운터 요청 모두 `RequireExactInterfaceId`
- 후속 요청은 설명 fallback 없이 고정 GUID만 사용

### 사용자 취소

초기 WLAN·카운터 고정 뒤 첫 샘플 전에 이미 취소된 토큰을 사용합니다.

- `Status=Canceled`
- `TerminationReason=CanceledByUser`
- 샘플 및 요약 없음
- 실제 또는 합성 delay 호출 없음

### Native WLAN 물리 NIC 변경

초기 인터페이스 A 다음에 인터페이스 B를 반환합니다.

- `Status=AdapterChanged`
- `TerminationReason=AdapterChanged`
- 변경 이후 카운터를 읽지 않음
- 서로 다른 NIC의 바이트를 결합하지 않음

### 고정 NIC 사용 불가

초기 카운터는 성공하지만 첫 후속 카운터에서 `InterfaceNotOperational`을 반환합니다.

- `Status=AdapterUnavailable`
- `TerminationReason=AdapterUnavailable`
- 다른 활성 Wi-Fi로 fallback하지 않음

### 카운터 공급자 불일치

고정 ID A를 요청했지만 합성 공급자가 ID B의 성공 스냅샷을 반환합니다.

- `Status=CounterProviderMismatch`
- `TerminationReason=CounterProviderMismatch`
- 불일치 샘플을 요약에 포함하지 않음

초기 Native WLAN ID와 첫 카운터 ID가 다른 경우도 샘플 loop 전에 같은 상태로 차단합니다.

### 동일 NIC의 BSSID 로밍

인터페이스 ID는 유지하고 중간에 BSSID만 한 번 변경합니다.

- 관찰 계속
- `TerminationReason=Completed`
- `BssidChangeCount=1`
- `AdapterChangeCount=0`
- 계획한 14개 샘플 모두 보존

### 미지원 실행 환경과 WLAN 미연결

- 미지원 런타임은 WLAN·ID·카운터 공급자를 호출하지 않고 `UnsupportedPlatform`
- 연결 WLAN이 없으면 카운터를 호출하지 않고 `NoWirelessConnection`

## 통신·데이터 경계

합성 런타임 테스트는 다음 작업을 하지 않습니다.

- 실제 Native WLAN API 호출
- 실제 NetworkInterface 카운터 조회
- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 연결
- 외부 API 또는 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

테스트의 SSID·BSSID·GUID는 문서화된 합성 값이며 실제 사내 식별정보를 사용하지 않습니다.

## 설계 경계

- 주입 생성자는 테스트와 향후 로컬 진단 하네스를 위한 명시적 경계입니다.
- 기본 생성자는 실제 Windows 런타임을 계속 사용합니다.
- 런타임은 측정 정책을 결정하지 않고 원시 WLAN·ID·카운터·시간·delay만 제공합니다.
- NIC 일치와 종료 원인 판정은 기존 Core 정책과 `BrowserObservationRunner`가 담당합니다.
- 테스트 런타임이 고정 ID 요청을 무시하고 다른 NIC를 반환해도 러너가 다시 검증해 fail-closed로 종료합니다.

## 후속 확장

다음 단계에서는 같은 경계에 샘플 시간 연속성 정책과 시스템 절전 이벤트 공급자를 연결해 다음 시나리오를 end-to-end로 검증합니다.

- 5초 초과 카운터 간격
- 0 또는 음수 카운터 간격
- 카운터 감소·재설정
- Suspend·Resume
- WLAN ID 일시 누락 후 복구
- 동일 NIC 로밍 중 정지·급락
- 종료 결과에서 통합 보고서와 Finding까지 생성
