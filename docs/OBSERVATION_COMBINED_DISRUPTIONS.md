# 브라우저 관찰 복합 장애: 카운터 재설정 후 시간 단절

브라우저 관찰 중 하나의 이상 상태만 발생한다고 가정할 수 없습니다. 같은 물리 Wi-Fi에서 누적 카운터가 한 번 재설정된 뒤 정상 증가가 복구되고, 이후 드라이버 정지·절전 메시지 누락·스케줄러 지연으로 샘플 시간이 끊길 수 있습니다.

이 문서는 다음 복합 순서의 결과 계약을 정의합니다.

```text
정상 기준 수집
  ↓
활성 구간의 Rx·Tx 카운터 감소
  ↓ CounterReset 샘플 기록, 처리량 계산 제외
같은 고정 NIC에서 정상 카운터 증가 복구
  ↓ 정상 처리량 샘플 기록
5초 초과 카운터 시간 간격
  ↓ TimingDiscontinuity로 종료, 현재 구간 제외
```

## 합성 검증 시나리오

500ms 샘플 간격과 다음 카운터를 사용합니다.

| 순서 | 시각 | 구간 | 누적 Rx | 처리 |
|---:|---:|---|---:|---|
| 0 | 0.0초 | 초기 | 1,000,000 | 기준점 |
| 1 | 0.5초 | 기준 | 1,062,500 | 1Mbps |
| 2 | 1.0초 | 기준 | 1,125,000 | 1Mbps |
| 3 | 1.5초 | 기준 | 1,187,500 | 1Mbps |
| 4 | 2.0초 | 기준 | 1,250,000 | 1Mbps |
| 5 | 2.5초 | 활성 | 500,000 | 카운터 감소·재설정 |
| 6 | 3.0초 | 활성 | 6,812,500 | 정상 증가 복구·약 101Mbps 원시 |
| 7 | 8.001초 | 활성 | 56,812,500 | 5.001초 단절·현재 50MB 구간 제외 |

## 기대 러너 결과

```text
Status: PartialSuccess
TerminationReason: TimingDiscontinuity
Samples: 6
CounterResetCount: 1
ActiveSampleCount: 1
ObservedDuration: 0.5초
TotalReceiveBytes: 6,312,500
AverageAdjustedReceiveMbps: 약 100Mbps
Confidence: Low
CompletedAt: 마지막 유효 카운터 시각 3.0초
```

시간축에 남는 여섯 개 샘플은 기준 4개, 카운터 재설정 1개와 복구된 정상 활성 샘플 1개입니다.

8.001초의 카운터는 시간 단절 판정을 위해 읽지만 `BrowserObservationSample`로 만들지 않습니다.

## 카운터 재설정 샘플

재설정 샘플은 증거로 시간축에 남기되 처리량에는 사용하지 않습니다.

```text
CounterReset: true
ReceiveBytesDelta: 0
TransmitBytesDelta: 0
RawReceiveMbps: null
RawTransmitMbps: null
AdjustedReceiveMbps: null
```

다음 정상 증가 카운터는 같은 고정 인터페이스 ID를 다시 검증한 뒤 새로운 기준점에서 처리량 계산을 재개합니다.

## 시간 단절 샘플

예상 간격이 500ms이므로 허용 상한은 다음과 같습니다.

```text
max(5초, 500ms × 4) = 5초
```

마지막 유효 카운터와 현재 카운터 사이가 5.001초이므로 `TimingDiscontinuity`입니다.

현재 카운터의 50MB 증가분은 다음에 포함하지 않습니다.

- 시간축 샘플
- 총 수신 바이트
- 평균·최고 처리량
- 정지·급락 판정
- 관찰 시간

부분 요약의 종료 시각은 비정상 현재 카운터가 아니라 마지막 유효 카운터 시각입니다.

## 종료 원인과 누적 증거

최종 직접 종료 원인은 `TimingDiscontinuity`입니다. 그러나 그 전에 발생한 카운터 재설정은 독립 증거로 유지합니다.

```text
terminationReason: TimingDiscontinuity
counterResetCount: 1
```

하나의 종료 원인만 저장하더라도 이전 이벤트의 집계와 샘플 플래그를 잃지 않는 것이 핵심입니다.

## 통합 Finding

복합 결과에는 다음 세 판정이 함께 있어야 합니다.

```text
BROWSER_OBSERVATION_TIMING_DISCONTINUITY
BROWSER_OBSERVATION_COUNTER_RESET
BROWSER_OBSERVATION_LOW_CONFIDENCE
```

다음 판정은 생성하면 안 됩니다.

```text
BROWSER_OBSERVATION_ADAPTER_CHANGED
BROWSER_OBSERVATION_ADAPTER_UNAVAILABLE
BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH
BROWSER_OBSERVATION_COMPLETED
```

카운터 재설정과 시간 단절은 같은 NIC에서 발생할 수 있으며, 그것만으로 NIC 변경·제거·카운터 공급자 ID 불일치를 추정하지 않습니다. 최종 실행이 시간 단절로 중단됐으므로 정상 완료 판정도 추가하지 않습니다.

## 보고서 계약

### 관찰 전용 보고서

다음을 함께 보존합니다.

- `terminationReason=TimingDiscontinuity`
- `counterResetCount=1`
- `activeSampleCount=1`
- 시간 단절 전 샘플 6개
- 재설정 샘플의 Boolean 플래그

### 통합 보고서

JSON·CSV는 두 머신용 Finding 코드를 모두 제공합니다.

```text
BROWSER_OBSERVATION_TIMING_DISCONTINUITY
BROWSER_OBSERVATION_COUNTER_RESET
```

HTML은 같은 Finding의 사람이 읽을 수 있는 제목과 해석을 표시합니다.

- 브라우저 관찰 샘플 시간 연속성 중단
- 브라우저 관찰 중 인터페이스 카운터 재설정

## 개인정보·통신 경계

합성 런타임은 문서화된 테스트 GUID·SSID·BSSID만 사용합니다. 전용·통합 보고서 출력에는 원문이 남지 않아야 합니다.

테스트와 판정 과정은 다음 작업을 수행하지 않습니다.

- 실제 Native WLAN API 호출
- 실제 NetworkInterface 카운터 조회
- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 연결
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

## 실제 환경 검증

실환경에서는 카운터 재설정을 직접 유발하지 않아도 다음 사례에서 관찰될 수 있습니다.

- 무선 드라이버 재시작
- USB Wi-Fi 순간 재초기화
- 절전·복귀 중 통계 공급자 초기화
- NIC 비활성화·활성화

재설정 뒤 시간 단절이 함께 기록되면 다음 정보를 같은 시각 기준으로 비교합니다.

1. Windows 시스템 이벤트
2. WLAN AutoConfig 이벤트
3. 무선 드라이버 이벤트
4. 장치 전원 관리 상태
5. 절전·Modern Standby 이력
6. 같은 시점의 BSSID·RSSI·PHY 변화

카운터 증거만으로 특정 드라이버 또는 Aruba AP 장애를 확정하지 않습니다.
