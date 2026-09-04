# 브라우저 관찰 결과의 보고서 파이프라인 통합 검증

이 자동 검증은 실제 `BrowserObservationRunner`에서 생성된 결과가 두 종류의 로컬 보고서와 고정 Finding까지 동일한 구조화 종료 원인을 유지하는지 확인합니다.

```text
합성 WLAN·카운터 런타임
  ↓
BrowserObservationRunner
  ↓
BrowserObservationResult
  ├─ 브라우저 관찰 전용 보고서
  │    ├─ JSON
  │    ├─ CSV
  │    └─ 단일 HTML
  └─ 통합 LocalDiagnosticReport
       ├─ JSON
       ├─ CSV
       ├─ 단일 HTML
       └─ ReportFindingEngine
```

## 합성 시나리오

초기 Native WLAN과 첫 카운터는 같은 물리 Wi-Fi ID로 정상 고정합니다. 첫 후속 카운터 요청에서 공급자가 `CounterProviderMismatch`를 반환하도록 구성합니다.

실패 메시지에는 마스킹 회귀 확인을 위한 테스트 전용 이메일·IP·URL을 포함합니다.

```text
user@example.invalid
10.20.30.40
https://internal.example.invalid/private.bin
```

실제 사내 정보는 사용하지 않습니다.

## 러너 기대 결과

- `Status=CounterProviderMismatch`
- `TerminationReason=CounterProviderMismatch`
- 첫 후속 카운터 실패이므로 처리량 요약 없음
- 초기 고정 카운터와 실패한 후속 카운터만 읽음
- 다른 활성 Wi-Fi로 자동 전환하지 않음

## 관찰 전용 보고서

`BrowserObservationSessionReportWriter`는 러너 결과를 다음과 같이 보존해야 합니다.

```text
status: CounterProviderMismatch
terminationReason: CounterProviderMismatch
summary: null
```

JSON·CSV·HTML 모두 종료 원인을 포함해야 하지만 다음 원문은 포함하면 안 됩니다.

- 인터페이스 전체 GUID
- 인터페이스 이름·설명
- SSID와 BSSID
- 합성 이메일·IP·URL

## 통합 보고서와 Finding

`ReportObservationMapper`는 같은 결과를 다음 필드에 기록합니다.

```text
browserObservation.status
browserObservation.terminationReason
```

`ReportFindingEngine`은 다음 고정 Finding을 정확히 한 개 생성해야 합니다.

```text
code: BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH
severity: Warning
```

출력 형식별 역할은 다음과 같습니다.

| 형식 | Finding 표현 |
|---|---|
| JSON | 구조화 `code`, `severity`, `title`, `evidence`, `interpretation`, `nextStep` |
| CSV | `finding.N` 섹션에 구조화 코드와 각 필드 |
| HTML | 사람이 읽을 수 있는 제목·심각도·근거·해석·조치·한계 |

HTML은 사람이 읽는 보고서이므로 현재 고정 코드 문자열을 별도 표시하지 않고 코드에 대응하는 제목과 설명을 표시합니다. 자동 처리는 JSON 또는 CSV의 `code`를 사용합니다.

통합 파이프라인 테스트는 다음을 확인합니다.

- JSON·CSV에 고정 Finding 코드 존재
- JSON에는 해당 코드가 정확히 한 번 존재
- HTML에는 같은 Finding의 제목과 해석 존재
- 세 형식 모두 관찰 종료 원인 보존

## 원문 비노출 검증

다음 모든 렌더링 결과를 하나로 합쳐 합성 민감값이 남지 않는지 검사합니다.

- 관찰 전용 JSON
- 관찰 전용 CSV
- 관찰 전용 HTML
- 통합 JSON
- 통합 CSV
- 통합 HTML

검증 대상에는 전체 GUID·인터페이스 이름·SSID·BSSID·이메일·IP·URL과 URL 호스트가 포함됩니다.

## 통신 경계

테스트는 `IBrowserObservationRuntime` 합성 구현만 사용합니다.

다음 작업은 수행하지 않습니다.

- 실제 Native WLAN API 호출
- 실제 NetworkInterface 카운터 조회
- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 회사 프록시 연결
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

## 회귀 방지 의미

이 테스트는 각 계층의 단위 테스트가 개별적으로 성공해도 계층 연결 과정에서 종료 원인이나 마스킹이 누락되는 문제를 차단합니다.

탐지 가능한 회귀 예:

- 러너 종료 원인이 전용 보고서에서 사라짐
- 전용 보고서는 정상이나 통합 보고서에서 `terminationReason` 누락
- Finding 엔진이 구조화 종료 원인이 아니라 `status`만 사용
- 메시지 마스킹 전 원문이 JSON·CSV·HTML 중 하나에 남음
- 같은 종료 원인 Finding이 중복 생성
- HTML에서 Finding 제목·해석이 누락돼 사람이 원인을 확인할 수 없음

## 후속 확장

같은 파이프라인 검증을 다음 시나리오로 확대합니다.

- `AdapterChanged`
- `AdapterUnavailable`
- `SystemSuspend`
- `TimingDiscontinuity`
- 사용자 취소
- 정상 완료와 BSSID 로밍
- 카운터 재설정 후 낮은 신뢰도

각 시나리오는 실제 외부망이나 회사 프록시에 접근하지 않고 동일한 구조화 결과·보고서·Finding 계약을 검증합니다.
