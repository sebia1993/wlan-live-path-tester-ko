# 브라우저 관찰 카운터 재설정 판정

브라우저 관찰은 시작 시 고정한 물리 Wi-Fi 인터페이스의 누적 Rx·Tx 바이트 카운터 차이로 처리량을 계산합니다. 현재 누적값이 이전 누적값보다 작아지면 해당 구간은 정상 증가 카운터가 아니므로 `CounterReset`으로 표시합니다.

통합 로컬 진단 보고서는 `counterResetCount`가 1 이상이면 다음 전용 Finding을 생성합니다.

```text
BROWSER_OBSERVATION_COUNTER_RESET
```

## 의미

카운터 재설정은 다음과 같은 상황에서 발생할 수 있습니다.

- 무선 드라이버 재시작
- 장치 비활성화·활성화
- 운영체제 네트워크 통계 공급자 초기화
- NIC 또는 드라이버 내부 누적 카운터 wrap
- 절전·복귀 과정의 장치 재초기화
- USB Wi-Fi의 짧은 연결 재설정

카운터 감소만으로 어느 원인인지 확정하지 않습니다.

## 처리량 계산 경계

재설정 구간에서는 다음 값을 사용하지 않습니다.

```text
ReceiveBytesDelta = 0
TransmitBytesDelta = 0
RawReceiveMbps = null
RawTransmitMbps = null
AdjustedReceiveMbps = null
```

따라서 음수 바이트, 음수 Mbps 또는 비정상적으로 큰 wrap 값이 평균·최고·총 수신량에 들어가지 않습니다.

다음 정상 증가 카운터가 같은 고정 물리 Wi-Fi ID에서 확인되면 관찰은 계속할 수 있습니다. 이 경우 관찰 종료 원인은 `Completed`일 수 있지만, 재설정 구간이 있었으므로 신뢰도는 낮아질 수 있습니다.

## Finding 구조

```text
code: BROWSER_OBSERVATION_COUNTER_RESET
severity: Warning
```

Finding에는 다음 정보를 기록합니다.

- 재설정 횟수
- 해당 구간의 델타와 Mbps를 통계에서 제외했다는 사실
- 가능한 드라이버·장치·운영체제 통계 초기화 원인
- 단일 원인으로 확정할 수 없다는 한계
- 확인할 Windows 시스템·WLAN AutoConfig·드라이버·전원 관리 항목

## 다른 종료 원인과의 차이

| 상태 또는 Finding | 의미 |
|---|---|
| `BROWSER_OBSERVATION_COUNTER_RESET` | 같은 고정 NIC의 누적 카운터가 감소했지만 이후 관찰을 계속할 수 있음 |
| `AdapterChanged` | Native WLAN 물리 인터페이스 ID가 다른 NIC로 바뀜 |
| `AdapterUnavailable` | 고정 NIC가 Down·제거됐거나 통계를 읽을 수 없음 |
| `CounterProviderMismatch` | 고정 WLAN ID와 반환된 카운터 ID가 일치하지 않음 |
| `TimingDiscontinuity` | 카운터 타임스탬프 간격이 허용 상한을 벗어남 |

카운터 재설정만으로 NIC 변경·공급자 불일치·NIC 사용 불가를 자동 추정하지 않습니다.

## 함께 생성될 수 있는 판정

재설정 후 관찰이 정상 완료된 경우 다음 판정이 함께 존재할 수 있습니다.

```text
BROWSER_OBSERVATION_COMPLETED
BROWSER_OBSERVATION_COUNTER_RESET
BROWSER_OBSERVATION_LOW_CONFIDENCE
```

각 코드는 역할이 다릅니다.

- `COMPLETED`: 관찰 절차가 끝까지 종료됨
- `COUNTER_RESET`: 유효하지 않은 누적 카운터 구간이 있었음
- `LOW_CONFIDENCE`: 전체 관찰 결과를 대표값으로 사용할 때 주의가 필요함

코드 기준 중복 제거를 적용하므로 재설정 횟수가 여러 번이어도 전용 Finding은 한 개만 생성하고 실제 횟수를 Evidence에 기록합니다.

## 보고서 표현

- JSON: `findings[].code`와 구조화 필드
- CSV: `finding.N` 섹션의 코드·근거·해석·조치
- HTML: 사람이 읽을 수 있는 제목·심각도·근거·해석·조치·한계

HTML은 머신용 코드 대신 사람이 읽을 수 있는 Finding 제목과 설명을 표시합니다. 자동 처리는 JSON 또는 CSV의 코드를 사용합니다.

## 통신·데이터 경계

이 판정은 이미 마스킹된 `ReportObservationSection.CounterResetCount`만 사용합니다.

다음 작업은 수행하지 않습니다.

- 실제 NIC 카운터 재조회
- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 연결
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

Finding에는 SSID·BSSID·인터페이스 이름·전체 GUID·IP·MAC·게이트웨이·DNS·URL 원문을 추가하지 않습니다.

## 자동 검증

ReportSmoke는 다음을 확인합니다.

- 재설정 1회에 전용 Warning Finding 생성
- 재설정 여러 회에도 코드 한 개만 생성하고 실제 횟수 기록
- 재설정 0 또는 알 수 없음에는 Finding 미생성
- NIC 변경·공급자 불일치·NIC 사용 불가를 잘못 추정하지 않음
- 정상 완료와 낮은 신뢰도 Finding을 함께 유지
- JSON·CSV에 머신용 코드 존재
- HTML에 동일 Finding의 제목과 해석 존재
