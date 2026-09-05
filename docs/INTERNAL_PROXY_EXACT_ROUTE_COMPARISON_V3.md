# 내부 DIRECT–프록시 정확 로컬 인터페이스 비교 V3

이 기능은 승인된 내부 `DIRECT` 대상과 대상별 PAC/WPAD 또는 수동 설정에서 선택된 프록시 엔드포인트가 현재 Windows PC에서 어떤 첫 로컬 인터페이스를 선택하는지 비교합니다.

비교는 표시용 짧은 지문이 아니라 **같은 실행 세션의 메모리 내 전체 Windows 인터페이스 GUID**로만 수행합니다.

## 처리 흐름

```text
내부 DIRECT 대상
  → Windows DNS 또는 IP literal
  → Windows 최적 로컬 인터페이스
  → 전체 인터페이스 GUID + 표시용 지문

프록시 출처 선택
  → 실행 계획
  → 프록시 후보별 Windows 최적 로컬 인터페이스
  → 전체 인터페이스 GUID(메모리 전용) + 표시용 지문

InternalProxyRouteComparisonEvaluator
  → Ready / Diverged / Ambiguous / Incomplete
```

비교 판정기 자체는 이미 수집된 메모리 객체만 읽으며 DNS·라우팅 API·HTTP를 다시 호출하지 않습니다.

## 판정 상태

```text
Ready
Diverged
Ambiguous
Incomplete
```

### Ready

다음 조건을 모두 충족하고 내부·프록시 전체 GUID가 같습니다.

- 내부 입력 `Purpose=InternalDirectTarget`
- 내부 경로 `Status=Success`
- 내부 단일 인터페이스와 유효한 전체 GUID
- 프록시 실행 `Completed`
- 프록시 분석 `Success`
- 프록시 출처 파싱 오류 없음
- 분석 후보 수와 결과 수 일치
- 모든 프록시 후보 `RouteStatus=Success`
- 모든 프록시 후보의 전체 인터페이스 GUID 존재
- 모든 프록시 후보가 하나의 정확 GUID로 수렴

```text
Status=Ready
Relation=SameInterface
Code=SameLocalInterface
ExactIdentityComparisonPerformed=true
```

의미:

- 현재 PC에서 내부 DIRECT 대상과 프록시 서버까지의 첫 로컬 송출 NIC가 같음
- 이후 사내 라우팅, 프록시 처리, 인터넷 회선과 외부 대상 서버 경로가 같다는 뜻은 아님
- 동일 NIC라고 해서 내부·외부 처리량이 같아야 하는 것은 아님

### Diverged

위의 완전 증거 조건을 모두 충족하지만 내부 GUID와 프록시 GUID가 다릅니다.

```text
Status=Diverged
Relation=DifferentInterface
Code=DifferentLocalInterface
ExactIdentityComparisonPerformed=true
```

가능한 의미:

- 내부 대상은 현재 Wi-Fi, 프록시는 회사 VPN·터널
- 내부 대상은 유선, 프록시는 무선
- 목적지별 정적 경로 또는 인터페이스 메트릭
- 보안 에이전트의 분할 라우팅
- 의도된 프록시 전용 송출 경로

`Diverged`는 정보성 경로 분리이며 자동 장애 판정이 아닙니다.

### Ambiguous

단일 인터페이스 관계를 정할 수 없는 상태입니다.

- 내부 대상의 IPv4·IPv6 또는 주소별 경로가 여러 인터페이스 선택
- 기존 프록시 경로 분석 상태가 `MultipleInterfaces`
- 하나의 프록시 호스트가 주소별로 여러 인터페이스 선택
- 서로 다른 프록시 후보의 전체 GUID가 둘 이상
- 축약 지문은 같지만 메모리의 전체 GUID는 서로 다름

```text
Status=Ambiguous
Relation=MultipleInterfaces
Code=InternalRouteAmbiguous | ProxyRouteAmbiguous
ExactIdentityComparisonPerformed=false
```

후보 중 하나를 임의로 선택하지 않습니다.

### Incomplete

비교에 필요한 증거가 부족하거나 실행이 완료되지 않은 상태입니다.

예:

- 내부 경로 없음
- 내부 경로 목적이 `InternalDirectTarget`이 아님
- 내부 경로 부분 성공·실패
- 내부 전체 GUID 없음
- 프록시 실행 없음
- 프록시 출처 `Blocked` 또는 `Unavailable`
- `DirectOnly`
- 프록시 실행 취소·실패
- 프록시 분석 객체 없음
- 외부 대상에서 DIRECT가 첫 경로
- 대상 스킴에 적용 가능한 프록시 없음
- 프록시 분석 `PartialSuccess`, `Failed`, `Canceled`
- 출처 파싱 오류 존재
- 후보 수·성공 수·후보 상태 불일치
- 하나 이상의 프록시 전체 GUID 없음

```text
Status=Incomplete
Relation=Unknown
ExactIdentityComparisonPerformed=false
```

확인된 일부 후보만으로 전체 fallback 경로를 추정하지 않습니다.

## 전체 GUID 메모리 보존

기존 `ProxyEndpointRouteEvidenceItem`에 다음 속성을 추가합니다.

```csharp
[JsonIgnore]
public string? SelectedInterfaceIdentity { get; init; }
```

Windows 경로 분석기가 `DestinationRouteEvidence.SelectedInterface.InterfaceIdentity`를 이 필드에 넣습니다.

- 같은 실행 세션의 정확 비교에만 사용
- 기본 JSON 직렬화에서 제외
- 화면·CSV·HTML에 출력하지 않음
- 분석 결과를 JSON으로 저장했다가 다시 읽으면 이 값은 없음

따라서 직렬화된 안전 결과는 표시·보고서에는 사용할 수 있지만 정확 NIC 비교에는 사용할 수 없습니다.

## 지문-only 비교 금지

호스트와 인터페이스 지문은 SHA-256 앞 10자의 표시값입니다.

```text
0123456789
abcdef0123
```

지문은 보고서 간 후보를 구분하는 데 유용하지만 충돌 가능성이 있는 축약값입니다.

다음 상황에서는 지문이 같아도 `Ready`로 판정하지 않습니다.

```text
내부 표시 지문 = 0123456789
프록시 표시 지문 = 0123456789
프록시 SelectedInterfaceIdentity = null
```

결과:

```text
Status=Incomplete
Code=ProxyExactIdentityUnavailable
```

반대로 안전 지문을 의도적으로 같은 값으로 변조해도 메모리의 전체 GUID가 둘이면 `Ambiguous`입니다.

## 대상별 프록시 출처와 실행 상태

비교 입력은 `ProxyDirectiveRouteAnalysisExecutionResult<ProxyEndpointRouteAnalysisResult>`입니다.

비교 결과에 다음 안전 상태를 유지합니다.

```text
ProxyExecutionStatus
ProxySourceKind
ProxyPlanCode
ProxyAnalysisStatus
ProxyParseErrorsPresent
```

실행 상태별 처리:

| 실행 상태 | 비교 결과 |
|---|---|
| `Completed` | 메모리 분석 객체 검사 후 계속 |
| `DirectOnly` | `Incomplete / ProxyDirectOnly` |
| `Blocked` | `Incomplete / ProxySourceBlocked` |
| `Unavailable` | `Incomplete / ProxySourceUnavailable` |
| `Canceled` | `Incomplete / ProxyExecutionCanceled` |
| `Failed` | `Incomplete / ProxyExecutionFailed` |

`Completed`여도 `Analysis`가 null이면 직렬화된 실행 요약 또는 손상된 객체로 보고 `ProxyAnalysisMissing`입니다.

## DIRECT 처리

### DIRECT가 첫 경로

```text
DIRECT; PROXY later.example:8080
```

기존 분석기는 프록시 DNS·경로 조회를 하지 않고 `DirectPathSelected`를 반환합니다.

비교 결과:

```text
Incomplete
ProxyDirectPathSelected
```

### 프록시 뒤 DIRECT fallback

```text
PROXY proxy.example:8080; DIRECT
```

프록시 후보의 정확 인터페이스 비교는 수행할 수 있습니다. 다만 실제 프록시 연결이 실패해 DIRECT로 전환됐는지는 이 기능이 시험하지 않습니다.

`Ready`의 한계 문구에 DIRECT fallback 불확실성을 유지합니다.

### DIRECT 뒤 프록시 후보

기존 분석기는 DIRECT 뒤 후보를 조회하지 않습니다. `SkippedAfterDirectCount`를 안전 집계로 유지하며, 현재 적용 순서에 도달하지 않는 후보를 정확 비교 대상으로 포함하지 않습니다.

## Finding

비교 결과를 다음 고정 Finding으로 변환합니다.

| 비교 상태 | Finding 코드 | 심각도 |
|---|---|---|
| `Ready` | `INTERNAL_PROXY_ROUTE_SAME_INTERFACE` | Information |
| `Diverged` | `INTERNAL_PROXY_ROUTE_DIVERGED` | Information |
| `Ambiguous` | `INTERNAL_PROXY_ROUTE_AMBIGUOUS` | Warning |
| `Incomplete` | `INTERNAL_PROXY_ROUTE_INCOMPLETE` | Warning |

`Diverged`는 의도된 VPN·분할 라우팅일 수 있으므로 단독으로 Warning 장애 판정을 만들지 않습니다.

Finding의 Evidence에는 다음만 사용합니다.

- 비교 상태·관계·원인 코드
- 내부·프록시 실행·분석 상태
- 적용·분석·성공 후보 수
- 서로 다른 인터페이스 수
- 전체 GUID 정확 비교 수행 여부

호스트·전체 GUID·축약 인터페이스 지문도 일반 Finding에는 자동 복사하지 않습니다.

## 안전 텍스트 렌더링

`InternalProxyRouteComparisonTextRenderer`는 다음 값만 표시합니다.

- 비교 상태·관계·원인 코드
- 출처·계획·실행·분석 상태
- 내부 인터페이스 범주와 짧은 지문
- 프록시 후보별 sequence·transport·scope·port
- 호스트·인터페이스 짧은 지문
- route status와 WLAN 상관
- VPN·가상 여부
- DIRECT·fallback·파싱 오류 집계
- 비교 판정의 고정 설명·해석·한계·다음 확인

의도적으로 사용하지 않는 값:

- `EndpointLabel`
- 후보 `Message`와 `Warnings`
- `SelectedInterfaceIdentity`
- 원본 내부 URL·프록시 호스트
- 전체 인터페이스 GUID
- 인터페이스 이름·설명
- 예외 원문

정의되지 않은 enum·scope·port·지문은 `Unknown`, `unknown`, `-`, `확인 불가`로 치환합니다.

## 개인정보 경계

비교 결과 모델과 Finding에 포함하지 않는 값:

- 내부 대상 URL·호스트·IP
- 프록시 호스트와 PAC 원문
- 전체 인터페이스 GUID
- 인터페이스 표시 이름·설명
- IPv4·IPv6·MAC
- 게이트웨이·DNS
- SSID·BSSID
- 사용자·이메일·인증 정보
- 원본 예외 메시지

허용되는 식별값:

- 검증된 10자리 호스트 지문
- 검증된 10자리 인터페이스 지문
- 인터페이스 범주와 상태 플래그

## 통신 경계

비교·Finding·텍스트 렌더링은 이미 수집된 메모리 객체만 사용합니다.

직접 수행하지 않는 작업:

- DNS 조회
- Windows 라우팅 API
- TCP 연결
- HTTP·HTTPS
- 프록시 인증
- PAC/WPAD 다운로드·실행
- 프록시 관리 API
- 외부 분석 API
- AI 또는 로컬 AI
- 텔레메트리
- 자동 업데이트
- 결과 업로드

## 자동 검증

Core SelfTest와 WindowsSmoke는 다음을 확인합니다.

1. 중괄호·대소문자가 다른 같은 GUID의 `Ready`
2. 다른 정확 GUID의 `Diverged`
3. 같은 축약 지문이지만 서로 다른 전체 GUID의 `Ambiguous`
4. 기존 `MultipleInterfaces` 상태의 `Ambiguous`
5. DIRECT·Blocked·Unavailable·Canceled의 `Incomplete`
6. 파싱 오류·부분 경로의 `Incomplete`
7. 지문만 있고 전체 GUID가 없는 결과의 `Incomplete`
8. 잘못된 내부 목적과 GUID가 아닌 identity 거부
9. 비교 결과 JSON의 URL·호스트·GUID·인터페이스 설명 비노출
10. Finding 코드·심각도와 지문 미복사
11. 안전 텍스트의 후보 순서·지문·범주·DIRECT 표시
12. 안전 텍스트가 후보 label·message·warning·전체 GUID를 읽지 않음
13. Windows 분석기가 전체 GUID를 메모리에 보존
14. 기본 분석 JSON에서 전체 GUID 속성과 값 제외
15. 실패 경로에서 전체 GUID를 임의 생성하지 않음
