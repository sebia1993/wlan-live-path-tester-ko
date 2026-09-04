# 브라우저 관찰 카운터 재설정 감지와 복구

브라우저 다운로드 관찰은 같은 물리 Wi-Fi 인터페이스의 누적 수신·송신 바이트 차이를 계산합니다. 무선 드라이버 재시작, 인터페이스 통계 재초기화 또는 운영체제 상태 변화로 누적 카운터가 이전 값보다 작아질 수 있습니다.

카운터 감소를 일반 바이트 델타로 계산하면 음수 처리량이나 비정상적인 평균이 만들어질 수 있으므로, 해당 구간을 `CounterReset` 샘플로 기록하고 처리량 계산에서 제외합니다.

## 고정 처리 규칙

```text
현재 BytesReceived < 이전 BytesReceived
또는
현재 BytesSent < 이전 BytesSent
  ↓
CounterReset = true
ReceiveBytesDelta = 0
TransmitBytesDelta = 0
RawReceiveMbps = null
RawTransmitMbps = null
AdjustedReceiveMbps = null
```

카운터 재설정은 물리 Wi-Fi 인터페이스 ID 변경과 다릅니다. 같은 고정 NIC가 계속 사용 가능하고 다음 카운터가 정상 증가하면 관찰을 이어갈 수 있습니다.

```text
정상 카운터
  ↓
카운터 감소·재설정 샘플
  ↓ 처리량 계산 제외
다음 정상 증가 카운터
  ↓
새 기준에서 처리량 계산 재개
```

## 종료 상태

단일 카운터 재설정 뒤 같은 NIC에서 정상 샘플이 계속되면 관찰은 다음처럼 완료될 수 있습니다.

```text
status: Success
terminationReason: Completed
counterResetCount: 1
confidence: Low
```

`CounterReset`은 샘플 품질 경고이며, 반드시 `AdapterChanged`나 `AdapterUnavailable` 종료를 뜻하지 않습니다.

다음 경우에는 별도 종료 원인이 우선합니다.

- 고정 NIC ID가 달라짐 → `CounterProviderMismatch` 또는 `AdapterChanged`
- 고정 NIC가 Down·제거됨 → `AdapterUnavailable`
- 샘플 시간 간격이 허용 상한 초과 → `TimingDiscontinuity`
- 사용자 중지 → `CanceledByUser`

## 통계 포함 경계

카운터 재설정 샘플은 상태 근거로 시간축에 남지만 다음 통계에는 처리량 표본으로 사용하지 않습니다.

- 활성 처리량 샘플 수
- 평균 조정 수신 Mbps
- 최고 조정 수신 Mbps
- 해당 구간의 수신·송신 바이트 델타

다음 정상 카운터부터 계산을 재개합니다. 따라서 카운터 감소로 인한 음수 Mbps가 생성되지 않습니다.

## 신뢰도

관찰 중 카운터 재설정이 한 번이라도 있으면 신뢰도는 `Low`로 낮아집니다.

통합 보고서에서는 다음 근거를 함께 볼 수 있습니다.

```text
counterResetCount
confidence
terminationReason
```

현재 고정 Finding 엔진은 정상 완료에 대해 `BROWSER_OBSERVATION_COMPLETED`, 낮은 신뢰도에 대해 `BROWSER_OBSERVATION_LOW_CONFIDENCE`를 생성합니다. 카운터 재설정 자체의 전용 Finding은 후속 개선 항목입니다.

## 주입식 end-to-end 자동 검증

합성 런타임은 다음 흐름을 실제 `BrowserObservationRunner`에 전달합니다.

```text
기준 샘플 4개
활성 정상 샘플
카운터 감소 샘플 1개
활성 정상 샘플 재개
전체 관찰 완료
```

자동 검증 항목:

- `Status=Success`
- `TerminationReason=Completed`
- 전체 시간축 샘플 유지
- `CounterResetCount=1`
- `AdapterChangeCount=0`
- 재설정 샘플의 바이트 델타 0
- 재설정 샘플의 Rx·Tx·조정 Mbps `null`
- 모든 샘플의 바이트 델타와 Mbps가 음수가 아님
- 다음 정상 카운터에서 처리량 계산 복구
- 활성 통계에서 재설정 샘플 제외
- `Confidence=Low`
- 통합 보고서에 재설정 횟수와 `Completed` 유지
- 정상 완료 및 낮은 신뢰도 Finding 생성
- 재설정 전후 모든 카운터 요청이 같은 고정 물리 Wi-Fi ID 사용

## 통신·데이터 경계

카운터 재설정 검사는 이미 로컬에서 읽은 누적 바이트 숫자만 비교합니다.

다음 작업은 수행하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- PAC/WPAD 또는 프록시 연결
- 외부 API 또는 AI
- 텔레메트리·자동 오류 전송
- 관찰 결과 업로드

합성 테스트는 실제 WLAN, 실제 NetworkInterface, 회사 URL 또는 프록시를 사용하지 않습니다.

## 실제 환경 검증

1. 같은 물리 Wi-Fi에서 정상 관찰을 완료합니다.
2. 테스트 가능한 환경에서 무선 드라이버 또는 인터페이스 통계를 재초기화합니다.
3. 물리 NIC ID가 유지되는지 확인합니다.
4. 보고서의 `counterResetCount`가 증가하는지 확인합니다.
5. 재설정 샘플에 음수 바이트·Mbps가 없는지 확인합니다.
6. 다음 정상 증가 카운터에서 처리량 계산이 재개되는지 확인합니다.
7. 결과 신뢰도가 `Low`인지 확인합니다.
8. 실제 NIC가 제거·변경된 경우에는 카운터 재설정이 아니라 적절한 인터페이스 종료 원인이 기록되는지 확인합니다.

실제 사내 SSID·BSSID·인터페이스 GUID·IP·MAC·프록시·PAC 주소는 공개 Issue나 테스트 fixture에 원문으로 남기지 않습니다.
