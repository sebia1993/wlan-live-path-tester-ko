# 내부 DIRECT 경로와 프록시 엔드포인트 경로 비교

이 기능은 승인된 내부망 `DIRECT` 대상과 실제 프록시 후보까지 Windows가 선택하는 첫 로컬 인터페이스를 비교합니다. 회사 프록시 서버 내부 상태, 인터넷 구간 또는 Aruba 인프라 장비에 접근하지 않습니다.

## 비교 입력

### 내부 기준 경로

```text
DestinationRouteEvidence
Purpose=InternalDirectTarget
Status=Success
SelectedInterface=<단일 Windows 인터페이스>
```

일반 목적지 또는 프록시 엔드포인트 근거를 내부 DIRECT 기준으로 재사용하지 않습니다. 입력 용도를 고정해 서로 다른 의미의 경로를 잘못 비교하는 것을 방지합니다.

### 프록시 기준 경로

```text
ProxyEndpointRouteAnalysisResult
Status=Success
ParseStatus=Success
WasTruncated=false
모든 비-DIRECT 후보 Status=Success
```

PAC fallback의 모든 확인 대상 프록시 후보가 단일 Windows 인터페이스를 선택해야 완전 비교로 인정합니다.

## 판정 상태

```text
Ready
Incomplete
Ambiguous
Diverged
```

### Ready

내부 DIRECT 대상과 모든 확인된 프록시 후보가 같은 정확한 Windows 인터페이스 GUID를 선택합니다.

```text
Status=Ready
Relation=SameInterface
Code=SameLocalInterface
ExactIdentityComparisonPerformed=true
```

의미:

- 현재 PC에서 첫 로컬 송출 NIC가 같음
- 이후 사내 라우팅·프록시·인터넷·대상 서버 경로까지 같다는 뜻은 아님
- 동일 NIC라고 해서 내부·외부 처리량이 같아야 하는 것은 아님

### Diverged

내부 DIRECT 대상과 모든 확인된 프록시 후보가 각각 하나의 인터페이스로 확정됐지만 서로 다른 정확 GUID를 선택합니다.

```text
Status=Diverged
Relation=DifferentInterface
Code=DifferentLocalInterface
ExactIdentityComparisonPerformed=true
```

가능한 원인:

- 회사 VPN 또는 터널 정책
- 유선과 무선의 목적지별 우선순위
- 내부망과 인터넷 경계의 의도된 분할 라우팅
- 프록시 전용 경로

`Diverged`는 자동 장애 판정이 아닙니다. 의도된 설계일 수 있으므로 인터페이스 범주·VPN 정책·실제 측정 결과를 함께 확인합니다.

### Ambiguous

다음 중 하나이면 단일 인터페이스 관계를 결정하지 않습니다.

- 내부 대상의 IPv4·IPv6 경로가 여러 인터페이스를 선택
- 하나의 프록시 호스트가 주소 계열별로 여러 인터페이스를 선택
- 서로 다른 프록시 후보가 둘 이상의 정확한 인터페이스를 선택

```text
Status=Ambiguous
Relation=MultipleInterfaces
ExactIdentityComparisonPerformed=false
```

PAC fallback 후보가 서로 다른 NIC를 선택하면 실제 요청이 어느 후보를 사용했는지 이 비교만으로 알 수 없습니다.

### Incomplete

다음 조건에서는 비교 증거가 불완전합니다.

- 내부 경로 없음
- 내부 입력이 `InternalDirectTarget` 용도가 아님
- 내부 경로가 성공 상태 또는 단일 인터페이스가 아님
- 프록시 분석 없음·빈 입력·잘못된 입력
- `DIRECT`만 있고 프록시 엔드포인트 없음
- 일부 프록시 후보 실패·취소·부분 성공
- 파서 부분 성공
- 후보 상한으로 분석 잘림
- 전체 인터페이스 GUID 없음

```text
Status=Incomplete
Relation=Unknown
ExactIdentityComparisonPerformed=false
```

확인된 일부 후보만으로 전체 fallback 경로를 임의 요약하지 않습니다.

## 정확 ID 비교 원칙

보고서와 UI에는 SHA-256 앞 10자의 짧은 인터페이스 지문을 표시하지만, 같은 NIC 판정에는 사용하지 않습니다.

```text
표시용 지문
  → 사람이 같은 결과를 비교하기 위한 비가역 축약값
  → 충돌 가능성이 있으므로 정확 일치 판정에 사용하지 않음

메모리 내 전체 Windows GUID
  → 중괄호 제거
  → Guid 표준 D 형식 정규화
  → 대소문자 무시 정확 비교
```

프록시 분석 결과를 JSON으로 저장했다가 다시 불러와 전체 `RouteEvidence`가 없는 경우, 표시 지문이 내부 지문과 같아도 `Ready`로 판정하지 않습니다.

```text
Code=ExactIdentityUnavailable
```

정확 비교는 같은 실행 세션에서 원본 로컬 경로 근거가 메모리에 있을 때만 수행합니다.

## 개인정보 경계

비교 결과에는 다음 값만 유지합니다.

- 내부·프록시 상태
- 비교 상태·관계·고정 코드
- 인터페이스 짧은 지문
- 인터페이스 범주
- 프록시 후보·성공·DIRECT 개수
- 후보 잘림 여부
- 고정된 설명·해석·한계·다음 단계

다음 원문은 결과 모델에 복사하지 않습니다.

- 내부 대상 URL 또는 호스트
- 프록시 호스트
- 전체 인터페이스 GUID
- 인터페이스 이름과 설명
- IPv4·IPv6·MAC
- 게이트웨이·DNS
- SSID·BSSID
- PAC URL과 원문 프록시 문자열

입력 `DestinationRouteEvidence`와 프록시 후보의 전체 `RouteEvidence`는 비교 중 메모리에서만 읽습니다.

## 결과 필드

```text
status
relation
code
internalRouteStatus
proxyAnalysisStatus
internalInterfaceFingerprint
internalInterfaceCategory
proxyInterfaceFingerprints[]
proxyInterfaceCategories[]
proxyEndpointCount
successfulProxyRouteCount
directDirectiveCount
proxyAnalysisWasTruncated
exactIdentityComparisonPerformed
message
interpretation
limitation
nextStep
```

`HasCompleteComparableEvidence`는 `Ready` 또는 `Diverged`에서만 true입니다.

## 통신 경계

비교 판정기는 이미 수집된 메모리 객체만 읽습니다.

다음 작업은 수행하지 않습니다.

- DNS 조회
- Windows 라우팅 API 재호출
- TCP 연결
- HTTP/HTTPS 요청
- PAC/WPAD 다운로드·실행
- 프록시 인증 또는 관리 API
- 외부 API·AI 또는 로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

내부·프록시 경로 수집은 사용자가 별도로 명시적으로 실행하는 단계입니다.

## 자동 검증

Core SelfTest는 다음을 확인합니다.

- 중괄호·대소문자가 다른 같은 GUID의 `Ready`
- 다른 정확 GUID의 `Diverged`
- 여러 프록시 인터페이스의 `Ambiguous`
- 내부 주소 계열 모호성
- DIRECT-only·입력 누락의 `Incomplete`
- 파서 부분 성공·후보 잘림·실패 후보의 `Incomplete`
- 표시 지문만 같고 전체 GUID가 없는 결과를 비교하지 않음
- 잘못된 내부 목적과 GUID가 아닌 identity 거부
- JSON에서 내부 URL·프록시 호스트·전체 GUID·인터페이스 이름·설명 비노출

## 해석 예시

### 같은 무선 NIC

```text
내부 DIRECT: Wireless / 지문 a1b2c3d4e5
프록시 후보: Wireless / 지문 a1b2c3d4e5
결과: Ready
```

내부와 프록시까지의 첫 NIC는 같지만 외부 속도 저하는 프록시 인증·큐·인터넷 회선·사이트·CDN 영향을 받을 수 있습니다.

### 내부 Wi-Fi, 프록시 VPN

```text
내부 DIRECT: Wireless / 지문 a1b2c3d4e5
프록시 후보: Tunnel / 지문 f6e7d8c9b0
결과: Diverged
```

목적지별 VPN 또는 터널 정책일 수 있습니다. 장애로 단정하지 않고 VPN 정책과 실제 외부 측정을 확인합니다.

### 프록시 후보마다 다른 NIC

```text
proxy-a → Wireless
proxy-b → Tunnel
DIRECT fallback 존재
결과: Ambiguous
```

실제 요청이 어떤 fallback을 사용했는지 프록시 판정·오류·인증 결과와 함께 확인해야 합니다.
