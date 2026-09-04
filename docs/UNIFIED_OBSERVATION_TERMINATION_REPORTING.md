# 통합 진단 보고서의 브라우저 관찰 종료 원인

통합 로컬 진단 보고서는 WLAN·프록시·다운로드 측정과 브라우저 관찰을 한 JSON·CSV·HTML 묶음으로 저장합니다. 브라우저 관찰 섹션에는 결과 상태와 별도로 구조화된 종료 원인을 기록합니다.

## 상태와 종료 원인의 차이

```text
status
  → 결과가 성공·일부 성공·취소·실패 중 어떤 수준인지

terminationReason
  → 관찰이 왜 끝났는지
```

예를 들어 유효한 샘플이 일부 남아 있으면 `status=PartialSuccess`일 수 있지만 실제 종료 원인은 서로 다를 수 있습니다.

```text
PartialSuccess + AdapterChanged
PartialSuccess + AdapterUnavailable
PartialSuccess + TimingDiscontinuity
PartialSuccess + Failed
```

따라서 자동 분석은 `status`만으로 원인을 추정하지 않고 `terminationReason`을 함께 사용합니다.

## JSON

브라우저 관찰 섹션에 optional 필드가 추가됩니다.

```json
{
  "browserObservation": {
    "status": "PartialSuccess",
    "terminationReason": "AdapterChanged",
    "observedSeconds": 12.0,
    "activeSampleCount": 9,
    "message": "... 종료 원인: 관찰 Wi-Fi 인터페이스 변경 (AdapterChanged)"
  }
}
```

기존 결과처럼 구조화 종료 원인이 명시되지 않은 경우에는 기존 상태에서 안전하게 해석한 `EffectiveTerminationReason`을 사용합니다. 해석도 불가능하면 `terminationReason`은 `null`일 수 있습니다.

## CSV

기존 `section,key,value` 구조를 유지하며 다음 행을 추가합니다.

```text
browserObservation,terminationReason,AdapterChanged
```

CSV 값에는 기존과 동일하게 수식 주입 방지를 적용합니다.

## HTML

기존 HTML 렌더러는 브라우저 관찰의 마스킹된 `message`를 표시합니다. 보고서 매퍼가 종료 원인을 다음 형식으로 한 번만 추가하므로 HTML에서도 사람이 읽을 수 있는 설명과 고정 enum을 함께 확인할 수 있습니다.

```text
종료 원인: 관찰 Wi-Fi 인터페이스 변경 (AdapterChanged)
```

HTML은 계속 외부 JavaScript·CSS·웹폰트·이미지·iframe을 사용하지 않고 Content Security Policy와 HTML 인코딩을 유지합니다.

## 지원 종료 원인

| 값 | 의미 |
|---|---|
| `Completed` | 계획한 관찰 정상 완료 |
| `CanceledByUser` | 사용자가 중지 |
| `AdapterChanged` | 관찰 중 다른 물리 Wi-Fi 인터페이스로 변경 |
| `AdapterUnavailable` | 고정 Wi-Fi가 Down·제거됐거나 통계를 읽지 못함 |
| `CounterProviderMismatch` | Native WLAN ID와 카운터 공급자 ID 불일치 또는 모호성 |
| `SystemSuspend` | Windows 절전·최대 절전 전환 |
| `TimingDiscontinuity` | 카운터 샘플 시간 연속성 중단 |
| `InvalidOptions` | 관찰 설정 오류 |
| `UnsupportedPlatform` | 지원하지 않는 실행 환경 |
| `NoWirelessConnection` | 관찰 시작 시 연결 WLAN 없음 |
| `Failed` | 위 범주로 분류되지 않은 실행 오류 |

`SystemSuspend`와 `TimingDiscontinuity`는 해당 런타임 보호 기능이 병합되면 명시적으로 사용됩니다. 그 전에도 보고서 스키마와 테스트는 값을 안전하게 저장할 수 있습니다.

## 이전 코드와 호환성

`ReportObservationSection`의 기존 positional 생성자와 deconstruction 순서는 변경하지 않습니다. `TerminationReason`은 record 본문의 optional init 속성입니다.

따라서 기존 생성 코드는 그대로 동작합니다.

```csharp
var section = new ReportObservationSection(
    Status: "Success",
    // 기존 필드 생략
    Samples: samples);
```

새 코드만 다음처럼 종료 원인을 설정합니다.

```csharp
section = section with
{
    TerminationReason = "Completed"
};
```

## 개인정보 경계

종료 원인은 고정 enum 문자열이며 다음 원문을 포함하지 않습니다.

- SSID와 BSSID
- 인터페이스 이름·설명·전체 GUID
- IP·MAC·게이트웨이·DNS 주소
- 다운로드 URL·파일명
- 프록시 호스트와 PAC URL

관찰 원문 메시지는 기존 `SensitiveDataRedactor`를 거친 뒤 종료 설명을 추가합니다. 종료 설명 자체에도 사용자·장비·네트워크 식별값을 넣지 않습니다.

## 통신 경계

통합 보고서 매핑과 렌더링은 이미 메모리에 있는 관찰 결과와 로컬 파일 시스템만 사용합니다.

다음 통신을 새로 수행하지 않습니다.

- DNS
- HTTP/HTTPS
- PAC/WPAD
- 프록시 연결
- 외부 API 또는 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

## 자동 검증

ReportSmoke는 다음을 확인합니다.

- 명시된 종료 원인이 JSON·CSV·HTML에 동일하게 기록됨
- 기존 네 값 `BrowserObservationResult`가 안전한 종료 원인으로 매핑됨
- 기존 `ReportObservationSection` positional 생성자와 deconstruction 호환
- 한국어 설명과 enum이 HTML에 함께 존재
- 이메일·IP·URL 원문이 출력에 남지 않음
- 종료 설명이 메시지에 중복 추가되지 않음
