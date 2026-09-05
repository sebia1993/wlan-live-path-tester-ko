# 내부 DIRECT–프록시 경로 실행 Finding v2

이 기능은 `InternalProxyRouteComparisonRunResult`의 실행 상태와 구조화 비교 결과를 고정 `ReportFinding`으로 변환합니다. 한국어 자유형 메시지를 파싱하지 않고 enum·상태·개수·Boolean 필드만 사용합니다.

## 실행 상태 Finding

| 실행 상태 | Finding 코드 | 심각도 |
|---|---|---|
| `InvalidInput` | `INTERNAL_PROXY_ROUTE_RUN_INVALID_INPUT` | Warning |
| `ProxySourceBlocked` | `INTERNAL_PROXY_ROUTE_RUN_SOURCE_BLOCKED` | Warning |
| `ProxySourceUnavailable` | `INTERNAL_PROXY_ROUTE_RUN_SOURCE_UNAVAILABLE` | Information |
| `DirectPathSelected` | `INTERNAL_PROXY_ROUTE_RUN_DIRECT_PRIMARY` | Information |
| `InternalRouteUnavailable` | `INTERNAL_PROXY_ROUTE_RUN_INTERNAL_UNAVAILABLE` | Warning |
| `Canceled` | `INTERNAL_PROXY_ROUTE_RUN_CANCELED` | Information |
| `Failed` | `INTERNAL_PROXY_ROUTE_RUN_FAILED` | Warning |
| 정의되지 않은 값 | `INTERNAL_PROXY_ROUTE_RUN_UNKNOWN` | Warning |

### 입력 오류

내부 기준 대상, 외부 HTTP(S) 대상 또는 현재 대상에 적용되는 프록시 지시문이 비교 조건을 충족하지 않은 경우입니다. 입력 오류만으로 네트워크 장애를 판단하지 않습니다.

### 프록시 출처 차단

대상별 PAC·WPAD 판정, 수동 프록시 설정 또는 실행 계획이 서로 모순된 경우입니다. 편의상 DIRECT나 다른 프록시 출처로 대체하지 않은 fail-closed 결과입니다.

### 프록시 출처 없음

대상별 또는 수동 프록시 지시문을 확인하지 못한 경우입니다. 출처 없음은 DIRECT 또는 정상 연결을 뜻하지 않습니다.

### DIRECT 우선

현재 외부 대상에서 DIRECT가 첫 경로여서 비교할 프록시 엔드포인트가 없는 경우입니다. 다른 URL에도 동일한 정책이 적용된다고 해석하지 않습니다.

### 내부 경로 확인 불가

내부 DIRECT 대상에서 정확하고 단일한 Windows 인터페이스 근거를 얻지 못해 프록시 후보 조회를 추가로 수행하지 않은 경우입니다.

## 완료된 비교 Finding

실행 상태가 `Completed`이고 비교 결과가 존재하면 기존 비교 코드 체계를 그대로 사용합니다.

| 비교 상태 | Finding 코드 | 심각도 |
|---|---|---|
| `Ready` | `INTERNAL_PROXY_ROUTE_SAME_INTERFACE` | Information |
| `Diverged` | `INTERNAL_PROXY_ROUTE_DIVERGED` | Information |
| `Ambiguous` | `INTERNAL_PROXY_ROUTE_AMBIGUOUS` | Warning |
| `Incomplete` | `INTERNAL_PROXY_ROUTE_INCOMPLETE` | Warning |

`Diverged`는 VPN·터널·유선 우선순위 또는 의도된 분할 라우팅일 수 있으므로 자동 장애 경고가 아니라 정보성 구조화 결과로 유지합니다.

다음 내부 계약 오류도 별도 코드로 처리합니다.

```text
Completed + Comparison=null
  → INTERNAL_PROXY_ROUTE_RUN_RESULT_MISSING

Completed + 정의되지 않은 comparison status
  → INTERNAL_PROXY_ROUTE_RUN_RESULT_UNKNOWN
```

## Finding 근거

실행 근거에는 다음 값만 사용합니다.

```text
실행 상태
프록시 출처·선택 상태
실행 계획 상태·코드
프록시 실행 상태
프록시 endpoint 형식과 결정
http 또는 https 대상 스킴
내부·프록시 경로 상태
파싱·적용·분석·성공 후보 수
서로 다른 프록시 인터페이스 수
DIRECT 존재·우선·fallback 여부
프록시 파싱 오류 여부
현재 WLAN 전체 ID 확인 여부
내부·프록시 단계 수행 여부
```

완료 비교 근거에는 다음을 추가합니다.

```text
비교 상태
관계
원인 코드
전체 인터페이스 ID 정확 비교 여부
비교 모델의 적용·분석·성공·distinct 후보 수
```

음수 집계는 0으로 제한합니다. 대상 스킴은 `http`, `https`만 허용하고 그 외 값은 `없음`으로 표시합니다. 정의되지 않은 enum의 숫자값은 출력하지 않습니다.

## 개인정보 경계

Finding mapper가 읽거나 복사하지 않는 필드:

- 실행 `Message`, `Limitation`
- 비교 `Message`, `Interpretation`, `Limitation`, `NextStep`
- 내부·프록시 인터페이스 지문
- 인터페이스 범주 목록
- 내부 URL·호스트·IP
- 프록시 호스트·지시문
- 전체 인터페이스 GUID
- 원본 route evidence와 execution payload
- 예외 메시지

Finding의 사람이 읽는 제목·해석·한계·다음 단계는 상태별 고정 문장으로 생성합니다. 자유형 필드에 URL·이메일·IP·GUID·지문이 들어 있어도 Finding으로 반사하지 않습니다.

## 통신 경계

Finding mapper는 메모리 객체만 읽는 순수 Core 로직입니다.

수행하지 않는 작업:

- DNS
- Windows 라우팅 API
- TCP·HTTP·HTTPS
- 프록시 인증
- PAC·WPAD 다운로드
- 파일 생성
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·결과 업로드

## 자동 검증

ReportSmoke는 다음을 확인합니다.

1. 비완료 실행 상태 7개의 코드와 심각도
2. 완료 비교 상태 4개의 코드와 심각도
3. `Completed` 결과 누락
4. 정의되지 않은 실행·비교 enum의 fail-closed 처리
5. 음수 후보 수 0 제한
6. HTTP(S) 대상 스킴 정규화
7. 실행·계획·분석 상태의 구조화 근거
8. 실행·비교 자유형 메시지 비반사
9. 인터페이스 지문·URL·호스트·이메일·IP·전체 GUID 비노출
