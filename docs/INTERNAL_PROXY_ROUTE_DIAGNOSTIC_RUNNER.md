# 내부 DIRECT–프록시 로컬 경로 진단 Runner

`InternalProxyRouteDiagnosticRunner`는 프록시 출처 스냅샷, 출처 선택, 실행 계획, 내부 DIRECT 경로, 기존 프록시 엔드포인트 경로 분석과 기존 비교 엔진을 한 번의 사용자 실행으로 연결합니다.

## 처리 순서

```text
ProxyDirectiveSourceSnapshot
  ↓
대상별 PAC/WPAD·수동 프록시 출처 선택
  ↓
실행 계획 검증
  ├─ DirectOnly
  ├─ Blocked
  ├─ Unavailable
  └─ AnalyzeProxyEndpoints
        ↓
    내부 DIRECT 대상 로컬 경로 확인
        ↓
    승인된 프록시 지시문을 기존 경로 분석기에 전달
        ↓
    기존 InternalProxyRouteComparison.Compare
        ↓
    Ready / Diverged / Ambiguous / Incomplete
```

## 네트워크 조회 차단 순서

출처 선택과 실행 계획은 문자열·상태만 처리하므로 네트워크 요청이 없습니다.

다음 상태에서는 내부 대상 DNS조차 조회하지 않습니다.

```text
DirectOnly
Blocked
Unavailable
사전 취소
알 수 없는 실행 계획
```

따라서 다음 호출이 모두 0회입니다.

- 내부 대상 DNS·Windows 경로 reader
- 프록시 브리지
- 프록시 DNS·Windows 경로 reader

프록시 엔드포인트가 없는 DIRECT-only 상태에서 내부 경로를 먼저 조회해 불필요한 네트워크 동작을 만들지 않습니다.

## 사용자 실행 시 허용되는 동작

`AnalyzeProxyEndpoints` 계획이고 사용자가 실행한 경우에만:

1. 승인된 내부 DIRECT 대상의 IP 또는 Windows DNS 확인
2. 내부 대상의 Windows 최적 로컬 인터페이스 확인
3. 출처 선택 정책이 승인한 프록시 지시문을 기존 parser로 대상 스킴 필터링
4. 유효 비-DIRECT 프록시 후보의 IP 또는 Windows DNS 확인
5. 프록시 후보별 Windows 최적 로컬 인터페이스 확인
6. 현재 Native WLAN GUID와 경로 상관분석
7. 기존 내부–프록시 비교 엔진 실행

HTTP 요청, 프록시 인증 또는 서버 연결은 수행하지 않습니다.

## 실행 상태

```text
Completed
DirectOnly
Blocked
Unavailable
Canceled
Failed
```

### Completed

내부 경로와 프록시 분석 결과가 생성되고 기존 비교 엔진까지 실행됐습니다.

`Completed`는 비교 상태와 다릅니다.

```text
실행 Status=Completed
비교 Status=Ready
```

```text
실행 Status=Completed
비교 Status=Diverged
```

```text
실행 Status=Completed
비교 Status=Incomplete
```

프록시 경로를 찾지 못했더라도 구조화 분석 결과가 반환되면 비교 엔진이 `Incomplete`를 만들 수 있으므로 실행 자체는 `Completed`입니다.

### DirectOnly

현재 대상의 출처 선택 결과가 DIRECT-only입니다.

- 내부 DNS 미조회
- 프록시 DNS 미조회
- 비교 미수행

### Blocked

대상별 boolean과 지시문이 모순되거나 출처·실행 계획 계약이 유효하지 않습니다.

- 수동 프록시 자동 fallback 없음
- DIRECT 추정 없음
- 내부·프록시 DNS 미조회

### Unavailable

대상별 판정을 수행하지 않았고 수동 프록시 출처도 없습니다.

- DIRECT 추정 없음
- 내부·프록시 DNS 미조회

### Canceled

- 실행 전 취소: 모든 reader 0회
- 내부 경로 확인 취소: 프록시 브리지 0회
- 프록시 분석 취소: 내부 결과만 메모리에 유지 가능
- 기존 분석 결과가 `Canceled`: 비교 미수행

### Failed

- 내부 reader 예외
- 프록시 브리지 예외
- 승인된 계획이 완료 분석 payload를 반환하지 않음
- 비교 엔진 예외

대상·프록시 지시문·예외 원문은 안전 결과 메시지에 복사하지 않습니다.

## 결과 모델

```text
status
selectionStatus
sourceKind
planCode
internalRouteStatus
proxyRouteStatus
comparisonStatus
sameLocalInterface
proxyEndpointCount
successfulProxyRouteCount
directDirectiveCount
proxyAnalysisWasTruncated
message
hasCompleteComparison
redactedDisplay
```

다음 메모리 전용 객체는 기본 JSON에서 제외합니다.

```text
InternalRouteEvidence
ProxyRouteAnalysis
Comparison
```

`hasCompleteComparison`은 기존 비교 상태가 `Ready` 또는 `Diverged`일 때만 true입니다.

## 개인정보 경계

기본 진단 결과 JSON과 `ToString()`에는 다음 원문을 포함하지 않습니다.

- 내부 대상 URL·호스트·IP
- 외부 대상 URL
- 프록시 호스트와 지시문 원문
- 전체 인터페이스 GUID
- 인터페이스 이름과 설명
- SSID·BSSID
- 주소별 원본 경로 근거
- 예외 원문

안전한 상태·고정 코드·개수·비교 상태만 유지합니다.

원본 경로와 비교 객체는 같은 실행 세션의 UI·전용 보고서 생성에 사용할 수 있도록 메모리에서만 유지합니다.

## 기존 구현 재사용

Runner는 다음 검증된 기존 구현을 다시 사용합니다.

- `LocalRouteEvidenceReader`
- `ProxyEndpointParser`
- `ProxyEndpointRouteAnalyzer`
- `InternalProxyRouteComparison.Compare`

새 DNS 구현, 새 Windows 라우팅 P/Invoke 또는 중복 비교 모델을 만들지 않습니다.

## 자동 검증

Windows Smoke는 실제 DNS·프록시 없이 주입식 reader를 사용합니다.

1. 내부·프록시가 같은 Wi-Fi 인터페이스인 `Completed / Ready`
2. 내부 Wi-Fi와 프록시 Tunnel인 `Completed / Diverged`
3. DIRECT·Blocked·Unavailable에서 내부·프록시 reader 0회
4. 사전 취소에서 reader 0회
5. 내부 경로 취소 후 프록시 브리지 0회
6. 프록시 경로 미확정의 `Completed / Incomplete`
7. 후보·성공·DIRECT 집계
8. 기본 JSON에서 내부·외부 대상, 프록시 호스트, 전체 GUID와 원본 payload 비노출

## 통신 경계

수행하지 않는 작업:

- HTTP·HTTPS HEAD·GET
- 프록시 TCP 연결·CONNECT·인증
- PAC/WPAD 다운로드·실행
- 프록시 관리 API
- 외부 분석 API·AI·로컬 AI
- 텔레메트리·자동 오류 전송
- 자동 업데이트
- 결과 업로드

사용자가 진단을 실행했을 때 허용되는 네트워크 관련 동작은 Windows DNS와 로컬 최적 인터페이스 확인뿐입니다.
