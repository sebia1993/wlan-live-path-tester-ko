# 브라우저 관찰의 WLAN 연결 ID 연속성

브라우저 관찰은 시작 시 Native WLAN 연결과 물리 Wi-Fi 누적 카운터를 같은 인터페이스 GUID로 고정합니다. 관찰 중 Windows WLAN API가 연결 또는 인터페이스 ID를 한 번 반환하지 못했다고 즉시 다른 NIC로 판단하면 짧은 드라이버·WLAN AutoConfig 지연 때문에 정상 관찰이 불필요하게 중단될 수 있습니다.

반대로 WLAN ID를 무기한 확인하지 못하는 상태에서 카운터만 계속 읽으면 WLAN 메타데이터와 처리량이 실제로 같은 연결 상태인지 보장할 수 없습니다.

이 기능은 두 경계를 모두 만족하도록 연속 미확인 임계값을 적용합니다.

## 고정 규칙

```text
실제 같은 GUID 확인
  → Stable
  → 연속 미확인 횟수 0으로 초기화

연결 또는 GUID 미확인 1회
  → TransientlyUnavailable 1/3
  → 고정 카운터만 계속 읽음

연결 또는 GUID 미확인 2회
  → TransientlyUnavailable 2/3
  → 고정 카운터만 계속 읽음

연결 또는 GUID 미확인 3회
  → UnavailableThresholdExceeded
  → 현재 카운터를 읽기 전에 관찰 중단
  → WlanIdentityUnavailable

실제 다른 GUID 확인
  → Changed
  → 임계값을 기다리지 않고 즉시 AdapterChanged
```

기본 임계값은 3회입니다. 추적기 자체는 테스트·향후 관리 설정을 위해 1~20 범위를 허용하지만 현재 제품 러너는 기본값 3을 사용합니다.

## 일시 미확인 샘플

첫 두 번의 일시 미확인 동안에는 시작 시 고정한 카운터 인터페이스 ID를 계속 정확히 요청합니다.

```text
selectionMode = RequireExactInterfaceId
preferredInterfaceId = 시작 시 고정한 물리 Wi-Fi GUID
preferredInterfaceDescription = null
```

다른 활성 Wi-Fi, 같은 설명의 다른 NIC, Wi-Fi Direct 또는 가상 인터페이스로 전환하지 않습니다.

현재 WLAN identity가 없으므로 해당 샘플에서는 RSSI·BSSID·PHY 같은 WLAN 메타데이터를 신뢰하지 않습니다.

```text
WlanDisconnected = true
AdjustedReceiveMbps 계산 가능 여부와 무관하게
대표 처리량 통계에서는 제외
```

시간축에는 다음 메모와 함께 증거로 남깁니다.

```text
WLAN 연결 ID 일시 미확인 1/3; 시작 시 고정한 카운터만 사용
WLAN 연결 ID 일시 미확인 2/3; 시작 시 고정한 카운터만 사용
```

## 동일 ID 복구

임계값 전에 시작 시 고정한 같은 GUID가 다시 확인되면 다음과 같이 처리합니다.

- 연속 미확인 횟수 0으로 초기화
- 관찰 계속
- 물리 NIC 변경으로 기록하지 않음
- 복구된 현재 샘플부터 WLAN 메타데이터를 다시 사용
- 복구 메모 기록

```text
WLAN 연결 ID가 1회 미확인 후 시작 시 고정한 동일 인터페이스로 복구
```

일시 미확인 샘플이 포함됐으므로 관찰 전체 신뢰도는 Low가 될 수 있습니다. 종료 원인은 정상 완료 시 `Completed`입니다.

예시:

```text
Status=Success
TerminationReason=Completed
WlanDisconnectedSampleCount=1
AdapterChangeCount=0
Confidence=Low
```

이 경우 `BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE` 종료 Finding은 생성하지 않습니다. 대신 정상 완료와 낮은 신뢰도 판정이 함께 존재할 수 있습니다.

## 세 번 연속 미확인

세 번째 연속 미확인에서는 해당 시점의 카운터를 읽지 않고 중단합니다.

```text
Status=AdapterUnavailable
TerminationReason=WlanIdentityUnavailable
```

`Status`는 결과 가용 수준을 기존 UI·호환 코드에 전달하고, `TerminationReason`은 직접 원인이 고정 카운터 자체의 Down이 아니라 Native WLAN 연결 ID의 연속 미확인임을 구분합니다.

임계값 전 두 샘플은 시간축에 보존되지만 WLAN identity가 없어 대표 처리량 통계에는 사용하지 않습니다.

```text
기준 샘플 4개
일시 미확인 활성 샘플 2개
세 번째 미확인에서 중단
Counter read 없음
```

## 실제 다른 GUID

미확인 뒤라도 실제 다른 유효 GUID가 확인되면 즉시 `AdapterChanged`입니다.

```text
일시 미확인 1/3
  ↓
다른 물리 Wi-Fi GUID 확인
  ↓
AdapterChanged
```

다른 실제 ID는 정보 부재가 아니라 인터페이스 변경 근거이므로 세 번의 임계값을 기다리지 않습니다. 해당 현재 카운터도 읽지 않으며 서로 다른 NIC의 바이트를 한 결과에 결합하지 않습니다.

## 구조화 종료 원인

새 종료 원인:

```text
WlanIdentityUnavailable
```

한국어 표시:

```text
WLAN 연결 ID 연속 미확인
```

통합 보고서 고정 Finding:

```text
BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE
severity: Warning
```

Finding은 다음을 설명합니다.

- 고정 카운터 인터페이스는 다른 NIC로 전환하지 않았음
- Native WLAN 연결 또는 GUID를 연속 임계 횟수 확인하지 못함
- WLAN 메타데이터와 카운터 상관을 더 이상 보장하지 않아 중단함
- WlanSvc·드라이버·절전·권한·EDR·실제 분리 중 어느 원인인지 단독 확정 불가
- Windows WLAN 보고서와 관련 이벤트를 확인해야 함

다음 Finding으로 오인하지 않습니다.

```text
BROWSER_OBSERVATION_ADAPTER_CHANGED
BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH
```

## Finding 파이프라인

기존 `ReportFindingEngine`의 모든 WLAN·프록시·측정·관찰 규칙은 그대로 유지합니다. 앱과 Windows end-to-end 검증은 `ReportFindingPipeline`을 사용해 기존 결과에 WLAN ID 연속성 Finding을 추가합니다.

구조화 WLAN ID 오류가 있으면 일반적인 다음 판정은 제거합니다.

```text
NO_CLEAR_FAILURE_PATTERN
```

따라서 보고서가 특정 종료 원인과 “명확한 실패 패턴 없음”을 동시에 표시하지 않습니다.

## 개인정보와 통신 경계

연속성 추적기는 다음 로컬 값만 사용합니다.

- 시작 시 고정한 WLAN·카운터 GUID
- 현재 Native WLAN 연결 여부와 GUID
- 연속 미확인 횟수

다음 작업은 수행하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 연결
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드
- 다른 NIC 자동 선택

전체 GUID는 메모리 내 정확 일치에만 사용합니다. 전용·통합 보고서에는 원문 GUID, SSID, BSSID, 인터페이스 이름·설명, IP, MAC, 게이트웨이, DNS와 URL을 저장하지 않습니다.

## 자동 검증

### Core 추적기

- 같은 ID는 Stable
- 첫 두 미확인은 계속
- 세 번째 미확인은 종료
- 같은 ID 복구 시 연속 횟수 초기화
- 실제 다른 ID는 즉시 Changed
- 미확인 뒤 다른 ID도 즉시 Changed
- 종료 상태는 세션 끝까지 유지
- 임계값 1~20 범위 검증

### 주입식 러너

1. 한 번 미확인 후 동일 ID 복구
   - 14개 샘플 완료
   - WLAN 미확인 1개
   - 활성 처리량 샘플 9개
   - Adapter 변경 0
   - `Completed / Low`

2. 세 번 연속 미확인
   - 기준 4개와 미확인 2개만 보존
   - 세 번째 현재 카운터 미조회
   - `AdapterUnavailable / WlanIdentityUnavailable`

3. 한 번 미확인 후 다른 실제 GUID
   - 다른 GUID 확인 즉시 중단
   - 현재 카운터 미조회
   - `AdapterChanged`

### 보고서

- 전용 JSON·CSV·HTML에 새 종료 원인과 한국어 설명
- 통합 JSON·CSV에 머신용 Finding 코드
- 통합 HTML에 Finding 제목과 해석
- 특정 종료 원인과 `NO_CLEAR_FAILURE_PATTERN` 동시 생성 방지

## 실제 환경 검증

1. 정상 관찰에서 `Completed`를 확인합니다.
2. WLAN AutoConfig 또는 드라이버가 잠깐 상태를 갱신하는 환경에서 1회 미확인 후 동일 ID 복구를 확인합니다.
3. 복구 샘플 메모와 `WlanDisconnectedSampleCount`를 확인합니다.
4. Wi-Fi를 끊어 3회 연속 미확인을 유발하고 `WlanIdentityUnavailable`을 확인합니다.
5. 내장 Wi-Fi에서 USB Wi-Fi로 전환해 즉시 `AdapterChanged`인지 확인합니다.
6. 세 번째 미확인이나 다른 ID 확인 시 현재 카운터가 결과에 포함되지 않는지 확인합니다.
7. 전용·통합 보고서에서 실제 GUID·SSID·BSSID가 남지 않는지 확인합니다.
8. Windows WLAN 보고서, WLAN AutoConfig·드라이버·시스템 이벤트를 같은 시각 기준으로 비교합니다.

실제 사내 식별정보는 공개 Issue·테스트 fixture·스크린샷에 원문으로 남기지 않습니다.
