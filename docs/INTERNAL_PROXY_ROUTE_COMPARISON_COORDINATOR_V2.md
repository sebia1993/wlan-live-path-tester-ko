# 내부 DIRECT–프록시 경로 비교 코디네이터 v2

이 코디네이터는 현재 저장소의 프록시 출처 선택, 실행 계획, 대상 스킴 파서, Windows 로컬 경로 분석과 전체 인터페이스 GUID 비교기를 하나의 사용자 실행 흐름으로 조합합니다. 같은 기능을 다시 구현하지 않습니다.

## 입력

- 회사 정책상 DIRECT인 승인된 내부 URL·호스트·IP
- `ProxyDirectiveSourceSelectionResult`
- 비교할 외부 절대 HTTP·HTTPS URL
- 선택적 현재 Native WLAN 인터페이스 GUID
- DNS 제한 시간 1~30초
- `CancellationToken`

수동 입력을 위한 `RunManualDirectiveAsync`는 원문을 수동 프록시 출처로 먼저 평가한 뒤 동일한 안전 실행 경로를 사용합니다.

## 실행 순서

```text
입력과 DNS timeout 검증
  ↓
프록시 출처 선택 결과 → 실행 계획
  ├─ Blocked / Unavailable / DirectOnly → 모든 route reader 0회
  ↓
대상 URL 기준 ProxyEndpointParser 선해석
  ├─ 오류·적용 후보 없음 → 모든 route reader 0회
  ├─ DIRECT 우선 → 모든 route reader 0회
  ↓
사전 취소 확인
  ├─ 취소 → 모든 route reader 0회
  ↓
내부 DIRECT 대상의 Windows 로컬 경로
  ├─ 정확하고 단일한 인터페이스 근거 없음
  │    → 프록시 route reader 0회
  ↓
기존 ProxyDirectiveRouteAnalysisCoordinator
  ↓
기존 InternalProxyRouteComparisonEvaluator
```

## 실행 상태

```text
InvalidInput
ProxySourceBlocked
ProxySourceUnavailable
DirectPathSelected
InternalRouteUnavailable
Completed
Canceled
Failed
```

실행 상태와 비교 상태는 분리합니다. 예를 들어 프록시 후보 하나만 실패해도 전체 실행은 종료됐으므로 `Completed`일 수 있지만 비교 결과는 `Incomplete`입니다.

비교 상태:

```text
Ready
Diverged
Ambiguous
Incomplete
```

## zero-read 경계

다음 조건에서는 내부·프록시 DNS와 Windows 최적 인터페이스 reader를 한 번도 호출하지 않습니다.

- 내부 대상 또는 외부 URL 입력 오류
- HTTP·HTTPS가 아닌 외부 URL
- 대상 스킴에 적용되는 프록시 후보 없음
- 출처 선택 `Blocked`
- 출처 선택 `Unavailable`
- `DIRECT-only`
- `DIRECT`가 프록시 후보보다 먼저 나타남
- 실행 전 취소

내부 경로 reader가 실행된 뒤 다음 조건이 확인되면 프록시 reader는 호출하지 않습니다.

- 내부 DNS·라우팅 실패
- 내부 경로 부분 성공
- 내부 주소별 경로가 여러 인터페이스
- 선택 인터페이스 없음
- 전체 Windows 인터페이스 GUID 없음

프록시 경로를 확인해도 정확 비교가 불가능한 상태에서 불필요한 내부 DNS 조회를 늘리지 않기 위한 경계입니다.

## 현재 컴포넌트 재사용

- `ProxyDirectiveSourceSelectionPolicy`
- `ProxyDirectiveRouteAnalysisPlanPolicy`
- `ProxyEndpointParser`
- `ProxyDirectiveRouteAnalysisCoordinator`
- `ProxyEndpointRouteAnalyzer`
- `LocalRouteEvidenceReader`
- `InternalProxyRouteComparisonEvaluator`

프록시 TCP 연결, WinHTTP 인증 시험 또는 새 P/Invoke를 추가하지 않습니다.

## 개인정보 경계

공개 실행 결과에 포함하지 않는 값:

- 내부 대상 URL·호스트·IP
- 프록시 지시문 원문
- 실제 프록시 호스트
- 전체 인터페이스 GUID
- 인터페이스 이름과 설명
- 원본 `DestinationRouteEvidence`
- 원본 `ProxyDirectiveRouteAnalysisExecutionResult`
- 예외 메시지

원본 내부 경로와 프록시 실행 결과는 같은 프로세스에서 후속 로컬 보고서를 만들기 위해 `[JsonIgnore]` 메모리 필드로만 유지합니다.

공개 결과에는 상태, 고정 코드, 대상 스킴, 후보 수, DIRECT 위치, 짧은 인터페이스 지문과 비교 결과만 남습니다.

## 통신 경계

사용자가 실행하고 모든 선행 검증을 통과한 경우에만 기존 reader가 다음을 수행할 수 있습니다.

- 내부·프록시 DNS 이름의 운영체제 DNS 확인
- Windows 최적 로컬 인터페이스 판정

수행하지 않는 작업:

- HTTP HEAD·GET
- 프록시 TCP 연결·CONNECT·인증
- PAC/WPAD 다운로드
- 프록시 관리 API·내부 상태 조회
- AI·로컬 AI·외부 분석 API
- 텔레메트리·자동 오류 전송
- 자동 업데이트·결과 업로드

## 자동 검증

WindowsSmoke는 실제 외부 DNS나 프록시를 사용하지 않고 주입식 reader로 다음을 확인합니다.

1. 잘못된 URL·적용 후보 없음·DIRECT 우선에서 모든 reader 0회
2. Blocked·Unavailable에서 모든 reader 0회
3. 사전 취소에서 모든 reader 0회
4. 내부 경로 실패 후 프록시 reader 0회
5. 같은 전체 GUID의 `Completed / Ready / SameInterface`
6. 다른 전체 GUID의 `Completed / Diverged / DifferentInterface`
7. 일부 프록시 실패의 `Completed / Incomplete`
8. 후보·성공·DIRECT fallback 집계
9. 결과 JSON에서 내부 대상·프록시 호스트·전체 GUID·인터페이스 설명·원본 근거 비노출
10. 같은 프로세스 메모리에는 후속 보고서용 원본 근거 유지
11. 잘못된 DNS timeout에서 모든 reader 0회
