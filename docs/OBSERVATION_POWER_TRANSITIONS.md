# 브라우저 관찰의 시스템 절전·복귀 처리

브라우저 관찰은 시작 시 고정한 물리 Wi-Fi 인터페이스의 누적 Rx·Tx 카운터 차이를 시간 순서대로 계산합니다. 관찰 중 Windows가 절전 또는 최대 절전 상태로 전환되면 전원 전환 전후의 긴 시간 간격과 누적 카운터를 한 세션으로 결합하면 잘못된 처리량이 만들어질 수 있습니다.

이 기능은 로컬 Windows 창 메시지로 전원 전환을 감지하고, 활성 관찰을 `SystemSuspend`로 중단하며, 복귀 후 어댑터 진단을 다시 수행합니다.

## 로컬 Windows 메시지

WPF 창의 `WM_POWERBROADCAST`를 사용합니다.

| 이벤트 | 처리 |
|---|---|
| `PBT_APMSUSPEND` | 활성 브라우저 관찰 취소, `SystemSuspend` 기록 |
| `PBT_APMRESUMESUSPEND` | 관찰 정리 후 Wi-Fi·VPN·가상 NIC 재평가 |
| `PBT_APMRESUMEAUTOMATIC` | 자동 복귀 후 어댑터 재평가 |
| `PBT_APMPOWERSTATUSCHANGE` | AC·배터리 상태만 변경된 것으로 처리하고 관찰 유지 |

전원 메시지 훅은 WPF 창의 네이티브 handle이 준비된 뒤 등록하며 창이 닫힐 때 해제합니다. handle 생성 순서가 달라도 `SourceInitialized`와 Loaded 후 ContextIdle 재확인으로 한 번만 연결합니다.

## 관찰 취소 원인 우선순위

사용자 중지와 시스템 절전은 같은 `CancellationToken`을 사용할 수 있으므로 별도의 `BrowserObservationCancellationContext`로 직접 원인을 기록합니다.

```text
None < CanceledByUser < SystemSuspend
```

- 일반 중지 버튼은 `CanceledByUser`를 요청합니다.
- Windows Suspend 메시지는 `SystemSuspend`를 요청합니다.
- 거의 동시에 두 요청이 발생하면 `SystemSuspend`가 우선합니다.
- Suspend 뒤 늦게 들어온 사용자 중지 요청이 원인을 다시 낮추지 못합니다.
- 새 관찰 시작 전에는 이전 요청 원인을 Reset합니다.
- 컨텍스트가 없는 기존 호출의 토큰 취소는 계속 `CanceledByUser`로 처리합니다.

이 우선순위는 `Volatile`과 `Interlocked.CompareExchange`를 사용해 동시에 요청돼도 결정론적으로 유지합니다.

## 절전 중단 흐름

```text
브라우저 관찰 실행 중
  ↓ WM_POWERBROADCAST / PBT_APMSUSPEND
SystemSuspend 요청 기록
  ↓
현재 CancellationTokenSource 취소
  ↓
러너가 OperationCanceledException 처리
  ↓
Status=Canceled
TerminationReason=SystemSuspend
  ↓
절전 전 유효 샘플까지만 요약·보고서에 유지
```

러너는 다음 메시지를 사용합니다.

```text
시스템 절전 또는 최대 절전 전환으로 브라우저 관찰을 중단했습니다.
전원 전환 전후의 Wi-Fi 카운터를 한 결과에 결합하지 않습니다.
```

화면의 상태도 일반 `사용자 중지`가 아니라 `시스템 절전으로 중단`으로 표시합니다.

## 복귀 후 어댑터 재평가

Suspend를 감지하면 관찰 실행 여부와 관계없이 어댑터 재평가 필요 상태를 기록합니다.

Resume 시점에 다음 조건을 확인합니다.

```text
내부·외부 다운로드 측정 없음
브라우저 관찰 정리 완료
```

유휴 상태이면 다음 로컬 진단을 다시 수행합니다.

- Native WLAN 현재 연결 identity
- 활성 물리 Wi-Fi 후보
- 내장·USB Wi-Fi 선택 점수와 모호성
- VPN·터널
- Hyper-V·VMware·WSL 등 가상 NIC
- Wi-Fi Direct·Hosted Network 후보

관찰이 아직 취소 정리 중이면 즉시 선택을 바꾸지 않습니다. `finally`에서 관찰 상태를 완료한 뒤 pending 재평가를 한 번만 수행합니다.

어댑터 진단 탭이 아직 생성되지 않은 경우에도 해당 탭은 최초 생성 시 자체적으로 최신 상태를 다시 읽습니다.

## 합성 end-to-end 검증

주입식 `IBrowserObservationRuntime`으로 실제 절전이나 장치 없이 다음 흐름을 재현합니다.

```text
초기 카운터
기준 샘플 4개
활성 샘플 1개
여섯 번째 Delay에서 SystemSuspend 요청 + 토큰 취소
```

기대 결과:

- `Status=Canceled`
- `TerminationReason=SystemSuspend`
- 기준 4개와 활성 1개만 보존
- 활성 관찰 시간 0.5초
- 절전 뒤 WLAN·카운터 추가 조회 없음
- 전용 관찰 JSON·CSV·HTML에 `SystemSuspend`
- 통합 JSON·CSV·HTML에 `SystemSuspend`
- `BROWSER_OBSERVATION_SYSTEM_SUSPEND` Warning Finding
- 전체 GUID·인터페이스 설명·SSID·BSSID 비노출

별도 합성 사용자 중지에서는 `CanceledByUser`가 유지되는지도 검증합니다.

## `TimingDiscontinuity`와의 관계

| 종료 원인 | 직접 근거 |
|---|---|
| `SystemSuspend` | Windows가 명시적인 Suspend 메시지를 전달하고 취소 컨텍스트에 기록함 |
| `TimingDiscontinuity` | 실제 카운터 타임스탬프 간격이 허용 상한을 초과하거나 0·음수임 |

Suspend 메시지가 정상적으로 먼저 도착하면 `SystemSuspend`를 사용합니다. 메시지를 받지 못했지만 복귀 뒤 카운터 간격이 길게 벌어지면 시간 연속성 정책이 `TimingDiscontinuity`로 비정상 구간을 차단합니다.

두 기능 모두 전원 전환 전후의 바이트를 정상 실시간 처리량으로 합치지 않는 것이 목적입니다.

## 보고서 처리

### 관찰 전용 보고서

```text
status: Canceled
terminationReason: SystemSuspend
terminationDisplay: 시스템 절전 전환
```

중단 전 샘플과 요약만 저장하며 SSID·BSSID·인터페이스 ID·설명은 포함하지 않습니다.

### 통합 보고서

```text
browserObservation.status
browserObservation.terminationReason
```

고정 Finding:

```text
BROWSER_OBSERVATION_SYSTEM_SUSPEND
severity: Warning
```

JSON·CSV는 머신용 코드를 제공하고 HTML은 사람이 읽을 수 있는 제목·근거·해석·조치·한계를 표시합니다.

## 통신·데이터 경계

이 기능이 사용하는 정보는 다음뿐입니다.

- WPF 창의 로컬 `WM_POWERBROADCAST`
- 현재 관찰의 로컬 CancellationTokenSource
- 메모리 내 취소 원인 상태
- 로컬 Native WLAN·NetworkInterface 어댑터 진단

다음 작업은 수행하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 연결
- 외부 API 또는 AI·로컬 AI
- 전원·인터페이스 상태 업로드
- 텔레메트리·자동 오류 전송
- 자동 업데이트

전원 메시지에는 SSID·BSSID·IP·MAC·전체 GUID 또는 다운로드 URL이 들어가지 않습니다.

## 판단 한계

- 일부 Modern Standby 장치나 드라이버는 전원 메시지 순서와 시점이 다를 수 있습니다.
- 강제 전원 차단, 시스템 크래시 또는 프로세스 강제 종료는 정상 Suspend 메시지를 제공하지 않을 수 있습니다.
- 복귀 직후 WLAN AutoConfig와 무선 드라이버가 완전히 준비되기 전에는 어댑터 진단이 일시적으로 실패할 수 있습니다.
- 이 기능은 관찰 데이터 혼합 방지용이며 절전 원인, 배터리, 펌웨어 또는 드라이버 장애를 단독 진단하지 않습니다.
- 전원 메시지가 누락된 장치에서는 `TimingDiscontinuity`가 보조 안전망으로 동작합니다.

## 실제 환경 검증

1. 브라우저 관찰을 시작하고 기준 수집 뒤 활성 샘플을 확보합니다.
2. Windows 절전 또는 최대 절전을 실행합니다.
3. 시스템을 복귀시킵니다.
4. 관찰이 자동으로 끝나고 `SystemSuspend`가 표시되는지 확인합니다.
5. 일반 `사용자 중지`로 표시되지 않는지 확인합니다.
6. 중단 전 샘플만 보고서에 남는지 확인합니다.
7. 복귀 후 어댑터 진단이 내장·USB Wi-Fi, VPN·가상 NIC를 다시 평가하는지 확인합니다.
8. AC 어댑터 연결·분리만으로 관찰이 중단되지 않는지 확인합니다.
9. 관찰 전용·통합 JSON·CSV·HTML에서 종료 원인이 동일한지 확인합니다.
10. 실제 SSID·BSSID·인터페이스 GUID·IP·MAC·프록시 주소가 보고서에 남지 않는지 확인합니다.

실제 사내 식별정보는 공개 Issue·테스트 fixture·스크린샷에 원문으로 남기지 않습니다.
