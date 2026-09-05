# 승인된 프록시 출처–Windows 경로 분석 연결 V3

`ProxyDirectiveRouteAnalysisCoordinator`는 프록시 출처 선택·실행 계획과 기존 `ProxyEndpointRouteAnalyzer`를 연결합니다. 별도의 중복 경로 결과 모델을 만들지 않고 현재 `Core.Routing.ProxyEndpointRouteAnalysisResult` 계약을 그대로 사용합니다.

## 처리 흐름

```text
ProxyDirectiveSourceSnapshot
  ↓ 대상별 PAC/WPAD·수동 설정 출처 선택
ProxyDirectiveSourceSelectionResult
  ↓ 실행 계획과 콜백 허용 여부 검사
ProxyDirectiveRouteAnalysisExecutor
  ├─ DirectOnly / Blocked / Unavailable / 사전 취소
  │    → parser callback 0회
  │    → route reader 0회
  │
  └─ AnalyzeProxyEndpoints
       ↓ 선택된 메모리 전용 지시문
     ProxyEndpointParser.Parse(text, targetUri)
       ↓ 대상 스킴에 적용되는 후보만 선택
     기존 ProxyEndpointRouteAnalyzer
       ↓ 사용자가 명시적으로 실행한 경우에만
     Windows DNS·최적 로컬 인터페이스 확인
```

## 입력

```text
ProxyDirectiveSourceSnapshot 또는 ProxyDirectiveSourceSelectionResult
targetUri
expectedWlanInterfaceId
dnsTimeoutSeconds
CancellationToken
```

`targetUri`는 절대 HTTP 또는 HTTPS URL이어야 합니다. DNS 제한 시간은 1~30초입니다. 잘못된 입력은 route reader 호출 전에 거부합니다.

## 대상별 판정 우선

대상별 PAC/WPAD 판정과 수동 프록시가 함께 있으면 출처 선택 정책의 결과를 그대로 따릅니다.

예:

```text
대상별: PROXY target.example:8080; DIRECT
수동:   PROXY manual.example:3128
```

Windows 경로 reader에는 `target.example`만 전달됩니다. 수동 프록시는 자동 fallback하지 않습니다.

대상별 판정을 시도했지만 실패한 경우에도 수동 프록시를 조회하지 않습니다.

```text
ExecutionStatus=Blocked
route reader calls=0
```

## DIRECT 경계

대상별 또는 수동 선택 결과가 DIRECT-only이면:

```text
ExecutionStatus=DirectOnly
Analysis=null
route reader calls=0
```

DIRECT는 프록시 엔드포인트가 아니므로 프록시 호스트 DNS나 Windows 최적 인터페이스 조회를 수행하지 않습니다.

## 수동 프로토콜별 선택

수동 프록시가 다음과 같고 대상 URL이 HTTPS인 경우:

```text
http=manual-http.example:8080;
https=manual-https.example:8443
```

기존 target-aware parser는 `https=` 후보만 선택합니다.

```text
TargetScheme=https
ApplicableEndpointCount=1
AnalyzedEndpointCount=1
```

`http=` 항목을 HTTPS fallback으로 임의 사용하지 않습니다.

## 기존 경로 분석 결과 재사용

Coordinator는 기존 다음 상태를 그대로 반환합니다.

```text
InvalidInput
DirectPathSelected
NoApplicableEndpoint
Success
PartialSuccess
MultipleInterfaces
Canceled
Failed
```

후보별로 다음 안전 근거를 유지합니다.

```text
입력 sequence
host fingerprint
적용 스킴
proxy transport와 port
route status
현재 WLAN correlation
선택 interface fingerprint와 category
virtual·VPN·Up·default gateway 여부
주소 성공·실패 개수
마스킹된 고정 메시지·경고
```

## 취소

호출 전에 `CancellationToken`이 이미 취소된 경우:

```text
ExecutionStatus=Canceled
parser callback=0
route reader calls=0
```

분석 중 취소되면 기존 분석기는 완료된 후보까지만 유지하고 이후 후보를 조회하지 않습니다.

## 예외 비반사 보강

기존 route reader가 예외를 던질 경우 분석기는 더 이상 `exception.Message`를 결과에 복사하지 않습니다.

고정 문구:

```text
로컬 라우팅 판정 중 예외가 발생했습니다.
예외 원문은 결과에 포함하지 않았습니다.
```

따라서 예외 문자열에 다음 값이 들어 있어도 결과에 반사되지 않습니다.

- 프록시 호스트
- 인증 토큰
- 전체 인터페이스 GUID
- 내부 경로
- 사용자 계정 정보

## 개인정보 경계

메모리에서만 사용하는 값:

```text
대상별·수동 프록시 원문
선택된 지시문
실제 프록시 호스트
전체 Windows 인터페이스 GUID
원본 DestinationRouteEvidence
```

공개 가능한 `ProxyEndpointRouteAnalysisResult`에는 다음만 유지합니다.

```text
호스트 SHA-256 앞 10자 지문
인터페이스 SHA-256 앞 10자 지문
인터페이스 범주와 상태 플래그
구조화 경로 상태
고정·마스킹 메시지
```

Executor의 `Analysis` 속성은 기본 JSON 직렬화에서 제외됩니다. 호출자가 분석 객체를 별도로 직렬화하더라도 기존 안전 경로 모델에는 프록시 호스트와 전체 인터페이스 GUID가 포함되지 않습니다.

## 통신 경계

Coordinator 자체가 추가하는 통신은 없습니다. 승인된 프록시 계획에서 사용자가 실행한 기존 분석기가 다음 작업만 할 수 있습니다.

- 프록시 DNS 이름의 Windows DNS 확인
- IP 리터럴이면 DNS 생략
- Windows 최적 로컬 인터페이스 판정
- 현재 Native WLAN GUID와 로컬 상관분석

수행하지 않는 작업:

- 프록시 TCP 연결
- HTTP CONNECT
- 프록시 인증
- HTTP·HTTPS HEAD·GET
- PAC/WPAD 다운로드 또는 실행
- 프록시 관리 API·로그·세션 조회
- 외부 분석 API
- AI 또는 로컬 AI
- 텔레메트리
- 자동 업데이트
- 결과 업로드

## 자동 검증

WindowsSmoke는 실제 DNS·프록시 없이 주입식 route reader로 다음을 확인합니다.

1. 대상별 프록시가 수동 프록시보다 우선
2. 대상별 프록시 한 개와 DIRECT fallback 순서 유지
3. 대상별 DIRECT에서 reader 호출 0회
4. 대상별 판정 실패 + 유효 수동 프록시에서 reader 호출 0회
5. 대상별 미시도 + 수동 HTTPS 설정에서 `https=` 후보만 조회
6. 유선 프록시 경로의 현재 WLAN `DifferentInterface`
7. 사전 취소에서 reader 호출 0회
8. reader 예외의 호스트·토큰·전체 GUID 비반사
9. 잘못된 대상 URL과 DNS timeout에서 reader 호출 0회
10. 구조화 분석 JSON의 프록시 호스트 원문 비노출
