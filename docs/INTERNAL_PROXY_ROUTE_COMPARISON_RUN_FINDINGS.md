# 내부 DIRECT–프록시 경로 비교 실행 Finding

`InternalProxyRouteComparisonRunFindingMapper`는 코디네이터의 실행 상태와 기존 비교 엔진의 최종 상태를 하나의 고정 `ReportFinding`으로 변환합니다.

UI·JSON·CSV·HTML은 한국어 메시지를 파싱하지 않고 Finding 코드와 심각도를 사용합니다.

## 실행 단계 Finding

| 실행 상태 | Finding 코드 | 심각도 |
|---|---|---|
| `InvalidInput` | `INTERNAL_PROXY_ROUTE_COMPARISON_INVALID_INPUT` | Warning |
| `DirectPathSelected` | `INTERNAL_PROXY_ROUTE_COMPARISON_DIRECT_PRIMARY` | Information |
| `InternalRouteUnavailable` | `INTERNAL_PROXY_ROUTE_COMPARISON_INTERNAL_UNAVAILABLE` | Warning |
| `Canceled` | `INTERNAL_PROXY_ROUTE_COMPARISON_CANCELED` | Information |
| `Failed` | `INTERNAL_PROXY_ROUTE_COMPARISON_FAILED` | Warning |
| 알 수 없는 값 | `INTERNAL_PROXY_ROUTE_COMPARISON_UNKNOWN` | Warning |

### 잘못된 입력

프록시 문자열을 현재 외부 URL 기준으로 안전하게 결정하지 못하거나 내부 기준 입력이 유효하지 않은 경우입니다.

```text
DNS·라우팅 조회 시작 안 함
```

입력 오류를 실제 네트워크 장애로 해석하지 않습니다.

### DIRECT 우선

`DIRECT` 또는 `DirectWithProxyAlternatives`처럼 DIRECT가 실제 적용 순서에서 먼저인 경우입니다.

```text
INTERNAL_PROXY_ROUTE_COMPARISON_DIRECT_PRIMARY
```

비교할 프록시 엔드포인트가 없으므로 정보성 Finding입니다. 다른 URL에도 DIRECT가 적용된다는 뜻은 아닙니다.

### 내부 기준 경로 확인 실패

내부 DIRECT 대상이 DNS·Windows 라우팅 기준으로 비교 가능한 경로를 제공하지 못한 경우입니다.

내부 기준이 없으므로 추가 프록시 후보 DNS 조회를 시작하지 않았다는 사실을 근거에 포함합니다.

### 사용자 중지

사용자가 실행을 중지한 상태입니다. 완료되지 않은 후보를 실패로 오인하지 않고 정보성 Finding을 생성합니다.

### 실행 오류

reader 또는 분석 서비스가 안전하게 완료되지 않은 상태입니다. 예외 메시지는 Finding에 반사하지 않습니다.

## 완료 후 비교 Finding

실행 상태가 `Completed`이고 구조화 비교 결과가 있을 때 다음 코드를 사용합니다.

| 비교 상태 | Finding 코드 | 심각도 |
|---|---|---|
| `Ready` | `INTERNAL_PROXY_ROUTE_COMPARISON_READY` | Information |
| `Diverged` | `INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED` | Warning |
| `Ambiguous` | `INTERNAL_PROXY_ROUTE_COMPARISON_AMBIGUOUS` | Warning |
| `Incomplete` | `INTERNAL_PROXY_ROUTE_COMPARISON_INCOMPLETE` | Information |
| 알 수 없는 값 | `INTERNAL_PROXY_ROUTE_COMPARISON_RESULT_UNKNOWN` | Warning |

실행 상태는 `Completed`인데 비교 객체가 없으면 다음 fail-closed Finding을 사용합니다.

```text
INTERNAL_PROXY_ROUTE_COMPARISON_RESULT_MISSING
Severity=Warning
```

## Ready

내부 DIRECT 대상과 분석된 모든 프록시 후보가 같은 Windows 로컬 인터페이스 지문을 사용합니다.

```text
Information
```

같은 첫 로컬 NIC가 확인됐다는 뜻이며 다음을 의미하지 않습니다.

- 내부 서비스 정상
- 프록시 정상
- 인터넷 회선 정상
- 대상 서버 정상
- 내부·외부 처리량 동일

## Diverged

내부 DIRECT 대상과 프록시 엔드포인트가 서로 다른 Windows 로컬 인터페이스 지문을 사용합니다.

```text
Warning
```

사용자가 확인해야 할 경로 차이이므로 Warning이지만 자동 장애 판정은 아닙니다.

가능한 정상 설계:

- 회사 VPN 또는 터널
- 목적지별 유선·무선 우선순위
- 정적 경로
- 의도된 분할 라우팅
- 프록시 전용 인터페이스

## Ambiguous

내부 주소군 또는 프록시 후보가 여러 인터페이스로 나뉘거나 메타데이터가 충돌한 상태입니다.

```text
Warning
```

하나의 경로로 임의 축약하지 않습니다.

## Incomplete

내부 또는 프록시 경로와 fallback 후보의 근거가 부족한 상태입니다.

```text
Information
```

불완전한 수집 자체는 확정 장애가 아니므로 정보성 Finding으로 유지합니다. 다음 확인 절차를 제공합니다.

## Finding 근거

실행 단계 공통 근거:

```text
실행 상태
프록시 출처 종류
프록시 결정
파싱 후보 수
분석 후보 수
성공 후보 수
내부 경로 조회 수행 여부
프록시 경로 분석 수행 여부
DIRECT 존재 여부
DIRECT fallback 여부
```

완료된 비교의 추가 근거:

```text
비교 상태
같은 로컬 인터페이스 여부
프록시 경로의 서로 다른 인터페이스 수
VPN·터널 포함 여부
가상 인터페이스 포함 여부
```

## 개인정보 경계

Finding mapper는 다음 값을 읽거나 복사하지 않습니다.

- 실행 결과의 자유형 `Message`
- 실행 결과의 자유형 `Limitation`
- 비교 결과의 자유형 `Message`
- 비교 결과의 자유형 `Limitation`
- 비교 결과의 `Warnings`
- 내부·프록시 인터페이스 지문
- 원본 경로 근거
- 내부 URL·호스트·IP
- 프록시 호스트와 지시문
- 전체 인터페이스 GUID

고정 코드·고정 문장과 구조화 enum·숫자·Boolean만 사용합니다.

따라서 입력 객체의 자유형 서술에 URL·이메일·IP·GUID 또는 내부 호스트가 주입돼도 Finding에는 반사되지 않습니다.

## 알 수 없는 enum 처리

정의되지 않은 실행 또는 비교 상태가 전달되면 알려진 상태로 추정하지 않습니다.

```text
INTERNAL_PROXY_ROUTE_COMPARISON_UNKNOWN
INTERNAL_PROXY_ROUTE_COMPARISON_RESULT_UNKNOWN
```

둘 다 Warning이며 애플리케이션·스키마 버전 확인을 안내합니다.

## 통신 경계

Finding mapper는 메모리의 구조화 실행 결과만 읽습니다.

수행하지 않는 작업:

- DNS 조회
- Windows 라우팅 API
- TCP·HTTP·HTTPS
- 프록시 인증
- PAC·WPAD
- 파일 저장
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·결과 업로드

## 자동 검증

ReportSmoke는 다음을 확인합니다.

1. 실행 상태 5개와 예상 코드·심각도
2. 비교 상태 4개와 예상 코드·심각도
3. `Completed`인데 결과 없음의 Warning
4. 알 수 없는 실행·비교 enum의 고정 코드
5. 구조화 후보 수와 실행 단계 근거
6. 프록시 인터페이스 집계 근거
7. 자유형 메시지·경고에 주입한 URL·호스트·이메일·GUID 비반사
8. 내부·프록시 10자리 지문도 일반 Finding에는 자동 복사하지 않음
