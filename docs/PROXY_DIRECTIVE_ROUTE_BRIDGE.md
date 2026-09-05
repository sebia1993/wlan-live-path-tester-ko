# 선택된 프록시 지시문–Windows 경로 분석 브리지

이 브리지는 대상별 PAC/WPAD 또는 수동 프록시 출처 선택 결과를 저장소의 기존 `ProxyEndpointParser`와 `ProxyEndpointRouteAnalyzer`에 연결합니다. 별도의 중복 DNS·라우팅 분석기를 만들지 않습니다.

## 실행 순서

```text
ProxyDirectiveSourceSelectionResult
  ↓
ProxyDirectiveRouteAnalysisPlanPolicy
  ↓
ProxyDirectiveRouteAnalysisExecutor
  ├─ DirectOnly / Blocked / Unavailable / 사전 취소
  │    → 분석 콜백 0회
  │    → DNS·Windows route reader 0회
  │
  └─ AnalyzeProxyEndpoints
       → 승인된 메모리 전용 지시문만 콜백에 전달
       → 기존 ProxyEndpointParser로 대상 URL 스킴 적용
       → 기존 ProxyEndpointRouteAnalyzer로 로컬 경로 확인
```

## 대상별 판정 우선

대상별 PAC/WPAD 판정과 수동 프록시가 함께 있어도 출처 선택 정책이 승인한 문자열만 분석합니다.

```text
대상별: PROXY target-proxy.example:8080; DIRECT
수동:   PROXY manual-proxy.example:3128
```

브리지는 `target-proxy.example`만 기존 경로 분석기에 전달합니다. 선택되지 않은 수동 호스트는 DNS·route reader에 전달하지 않습니다.

## 수동 스킴 매핑

```text
http=proxy-http.example:8080;
https=proxy-https.example:8443
```

대상 URL이 HTTPS이면 기존 `ProxyEndpointParser`가 `https=` 후보만 선택합니다. 브리지는 수동 문자열 전체를 직접 해석해 임의 후보를 만들지 않습니다.

## 조회 금지 상태

다음 상태에서는 기존 경로 분석기 콜백 자체를 호출하지 않습니다.

```text
DirectOnly
Blocked
Unavailable
사용자 사전 취소
```

따라서 프록시 호스트 DNS와 Windows 최적 인터페이스 reader 호출도 0회입니다.

## 잘못된 대상 URL 스킴

HTTP·HTTPS가 아닌 대상 URI가 승인된 프록시 계획과 함께 전달되면 기존 `ProxyEndpointParser`가 `InvalidInput`을 반환합니다. 기존 경로 분석기는 DNS·route reader를 호출하지 않고 구조화된 `InvalidInput` 분석 결과를 반환합니다.

브리지 실행 콜백 자체는 예외 없이 결과를 반환했으므로 외부 실행 상태는 `Completed`, 내부 분석 상태는 `InvalidInput`입니다. UI와 보고서는 두 상태를 함께 확인해야 합니다.

## 개인정보 경계

메모리에서만 필요한 값:

- 승인된 프록시 지시문 원문
- 정규화 프록시 호스트
- 기존 경로 분석 결과와 전체 인터페이스 근거

기본 실행 결과 JSON에서 제외되는 값:

- `DirectiveText`
- `Analysis`
- 프록시 호스트 원문

기본 JSON에는 다음 안전 정보만 유지됩니다.

```text
status
planStatus
planCode
sourceKind
selectionStatus
proxyEndpointCount
directDirectiveCount
hasParseErrors
message
redactedDisplay
hasCompletedAnalysis
```

## 통신 경계

브리지 자체는 다음 작업만 합니다.

- 선택 정책이 승인한 문자열 전달
- 기존 로컬 파서 호출
- 기존 Windows 경로 분석기 호출

사용자가 실행했고 `AnalyzeProxyEndpoints` 계획일 때 기존 분석기가 수행할 수 있는 작업:

- 프록시 DNS 이름의 Windows DNS 확인
- 주소별 Windows 최적 로컬 인터페이스 판정
- 현재 Native WLAN GUID와 상관분석

수행하지 않는 작업:

- 프록시 TCP 연결
- HTTP CONNECT·인증
- HEAD·GET 다운로드
- PAC/WPAD 다운로드
- 프록시 관리 API
- 외부 분석 API·AI·로컬 AI
- 텔레메트리·결과 업로드

## 자동 검증

Windows Smoke는 다음을 확인합니다.

- 대상별 프록시만 분석하고 수동 프록시 호스트 미조회
- 수동 `http=`·`https=` 중 대상 스킴 후보만 분석
- DIRECT·Invalid·Unavailable·사전 취소에서 reader 호출 0회
- 지원하지 않는 대상 스킴에서 구조화 `InvalidInput`과 reader 0회
- DIRECT fallback 유지
- 기본 JSON에서 원문 지시문·프록시 호스트·분석 payload 비노출
- `hasCompletedAnalysis`와 고정 계획 코드 유지
