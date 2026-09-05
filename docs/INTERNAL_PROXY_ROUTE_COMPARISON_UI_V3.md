# 내부 DIRECT ↔ 프록시 로컬 경로 비교 UI v3

`경로 비교` 탭은 승인된 내부 DIRECT 대상과 특정 외부 HTTP(S) 대상에 적용되는 프록시 지시문을 기준으로, 현재 Windows PC가 선택하는 첫 로컬 인터페이스를 비교합니다.

이 화면은 현재 `main`의 단일 코디네이터만 호출합니다.

```text
InternalProxyRouteComparisonCoordinator
  ├─ 프록시 출처 선택·실행 계획 검증
  ├─ 대상 스킴 기준 프록시 후보 선해석
  ├─ 내부 DIRECT Windows 로컬 경로
  ├─ 프록시 후보 Windows 로컬 경로
  └─ 전체 인터페이스 GUID 정확 비교
```

UI가 파서, DNS·라우팅 reader, 프록시 분석기 또는 비교기를 직접 다시 조합하지 않습니다.

## 입력

### 내부 DIRECT 기준 대상

회사 정책상 프록시를 우회하는 승인된 내부 대상만 입력합니다.

```text
https://internal.example/path/file.bin
internal.example
10.20.30.40
2001:db8::10
```

URL·DNS 호스트·IPv4·IPv6를 지원합니다. 최대 길이는 코디네이터의 `MaximumInternalTargetLength`와 동일하게 제한합니다.

### 외부 프록시 판정 대상 URL

```text
https://download.example/file.bin
http://download.example/file.bin
```

절대 HTTP 또는 HTTPS URL만 허용합니다. 다음 목적에 사용합니다.

- `http=`·`https=` 수동 매핑의 정확한 적용 범위 선택
- 현재 지시문에서 DIRECT가 먼저인지 판단
- 현재 외부 대상에 적용되는 프록시 후보 선별

FTP·상대 URL·빈 값은 DNS 또는 라우팅 조회 전에 `InvalidInput`으로 종료합니다.

### 프록시 지시문 또는 PAC/WPAD 판정 결과

```text
PROXY proxy-a.example:8080; DIRECT
HTTPS proxy-b.example:8443; PROXY proxy-a.example:8080
http=proxy-http.example:8080;https=proxy-connect.example:8080
SOCKS5 [2001:db8::5]:1080; DIRECT
```

현재 v3 화면에서는 사용자가 확인한 지시문을 직접 입력합니다. Windows 설정과 대상별 WinHTTP PAC/WPAD 판정을 자동으로 불러오는 기능은 후속 작업입니다.

입력 원문은 텍스트 상자와 현재 프로세스 메모리에만 유지하며 자동 파일 저장이나 외부 전송을 하지 않습니다.

## 사용자 실행 경계

다음 버튼을 누른 경우에만 비교를 시작합니다.

```text
내부↔프록시 경로 비교
```

실행 순서:

1. 현재 Native WLAN 연결과 인터페이스 ID를 로컬 Windows API로 확인합니다.
2. 코디네이터가 내부 대상, 외부 URL, 프록시 출처와 지시문을 검증합니다.
3. 현재 외부 대상에 적용되는 프록시 후보와 DIRECT 순서를 선해석합니다.
4. 필요한 경우에만 내부 대상의 운영체제 DNS와 Windows 최적 인터페이스를 확인합니다.
5. 내부 기준이 정확하고 단일한 경우에만 적용 프록시 후보의 DNS와 Windows 최적 인터페이스를 확인합니다.
6. 현재 WLAN GUID와 경로를 상관 분석합니다.
7. 메모리 내 전체 인터페이스 GUID로 내부·프록시 경로를 정확 비교합니다.
8. 구조화 실행 결과를 안전 렌더러와 고정 Finding으로 표시합니다.

## zero-read 조건

다음 조건에서는 내부·프록시 DNS와 Windows 최적 인터페이스 reader를 한 번도 호출하지 않습니다.

- 내부 대상 또는 외부 URL 입력 오류
- 외부 대상이 HTTP·HTTPS가 아님
- 프록시 출처가 `Blocked`
- 프록시 출처가 `Unavailable`
- 선택 결과가 `DirectOnly`
- 현재 외부 대상에 적용되는 프록시 후보 없음
- `DIRECT`가 프록시 후보보다 먼저 나타남
- 실행 전 사용자가 취소함

내부 reader를 실행한 뒤 다음이 확인되면 프록시 후보 reader를 호출하지 않습니다.

- 내부 DNS 또는 라우팅 실패
- 내부 경로가 부분 성공
- 내부 주소 계열이 여러 인터페이스로 분기
- 선택 인터페이스 없음
- 전체 Windows 인터페이스 GUID 없음

현재 WLAN identity 확인은 Windows 로컬 API 읽기이며 DNS·HTTP·프록시 연결이 아닙니다.

## 수행할 수 있는 네트워크 관련 작업

모든 선행 검증을 통과하고 사용자가 실행한 경우에만 다음 작업이 가능합니다.

- 내부·프록시 DNS 호스트의 운영체제 DNS 주소 확인
- IPv4·IPv6 주소별 Windows 최적 로컬 인터페이스 판정

IP literal은 DNS를 사용하지 않습니다.

## 수행하지 않는 작업

```text
HTTP HEAD·GET 다운로드
프록시 TCP 연결
HTTP CONNECT
프록시 인증
PAC/WPAD 다운로드 또는 실행
프록시 서버 관리 API
프록시 CPU·세션·큐·정책·캐시 조회
프록시 이후 인터넷 경로 추적
외부 분석 API
AI 또는 로컬 AI
텔레메트리
자동 오류 전송
자동 업데이트
결과 업로드
```

속도 측정은 기존 별도 측정 탭에서 사용자가 시작한 HEAD·GET 기능으로만 수행합니다.

## 동시 실행 제어

경로 비교를 시작하기 전에 다음 상태를 확인합니다.

- 내부·외부 다운로드 측정 실행 여부
- 브라우저 관찰 실행 여부
- 기존 경로 비교 실행 여부

다운로드 측정 또는 브라우저 관찰이 실행 중이면 경로 비교를 시작하지 않습니다.

경로 비교가 실행되는 동안:

- 경로 비교 입력 상자를 잠급니다.
- 새 비교 시작 버튼을 비활성화합니다.
- 중지 버튼을 활성화합니다.
- 현재 경로 비교 탭을 제외한 다른 탭의 이전 활성 상태를 저장한 뒤 비활성화합니다.

종료 후 각 탭의 기존 활성 상태를 복원합니다. 모든 탭을 무조건 활성화하지 않으므로 다른 기능이 의도적으로 비활성화했던 상태를 덮어쓰지 않습니다.

## 사용자 취소와 창 종료

`경로 확인 중지`를 누르면 현재 `CancellationTokenSource`에 취소를 요청합니다.

- 실행 전 취소이면 route reader 호출 0회
- 내부 단계 이후 취소이면 프록시 분석 미시작
- 프록시 분석 중 취소이면 이후 후보 미조회
- 완료되지 않은 후보를 전체 fallback 근거로 사용하지 않음
- 원문 입력과 예외 메시지를 결과 영역에 표시하지 않음

창이 닫히면 활성 경로 비교에도 동일한 취소를 요청합니다.

## 실행 상태와 비교 상태

실행 상태:

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

비교 상태:

```text
Ready
Diverged
Ambiguous
Incomplete
```

`Completed`는 실행 파이프라인이 안전하게 종료됐다는 뜻입니다. 프록시 후보 일부가 실패한 경우 실행은 `Completed`이고 비교는 `Incomplete`일 수 있습니다.

### Ready

내부 DIRECT 대상과 분석된 모든 프록시 후보가 같은 정확한 Windows 인터페이스 GUID를 사용합니다.

첫 로컬 NIC가 같다는 뜻이며 이후 사내 라우팅·프록시·인터넷·대상 서버의 경로와 성능이 같다는 뜻은 아닙니다.

### Diverged

내부 DIRECT와 프록시 후보가 서로 다른 정확한 Windows 인터페이스 GUID를 사용합니다.

VPN·터널, 유선·무선 우선순위, 정적 경로 또는 의도된 분할 라우팅일 수 있으므로 자동 장애로 확정하지 않습니다.

### Ambiguous

내부 주소군 또는 프록시 후보가 여러 로컬 인터페이스로 나뉘어 단일 비교 결론을 내리지 않은 상태입니다.

### Incomplete

일부 DNS·라우팅 후보 실패, 파싱 오류, 전체 GUID 미확인 또는 fallback 근거 부족으로 정확한 동일·분기 결론을 내리지 않은 상태입니다.

## 안전한 결과 렌더링

UI는 실행 객체의 자유형 메시지나 원본 route evidence를 직접 출력하지 않습니다.

```text
InternalProxyRouteComparisonRunResult
  → InternalProxyRouteComparisonRunFindingMapper
  → InternalProxyRouteComparisonRunTextRenderer
```

표시하는 값:

- 검증된 실행·선택·계획·분석 상태
- 프록시 출처와 대상 스킴
- 파싱·적용·분석·성공 후보 수
- DIRECT 존재·우선·fallback
- 내부·프록시 단계 수행 여부
- 비교 상태·관계·고정 원인 코드
- 전체 GUID 정확 비교 수행 여부
- 알려진 인터페이스 범주
- 소문자 10자리 16진수 호스트·인터페이스 지문
- 주소 성공·전체 개수
- 고정 Finding 코드·심각도·근거·해석·한계·다음 확인

의도적으로 읽지 않는 값:

- 실행 `Message`, `Limitation`
- 비교 `Message`, `Interpretation`, `Limitation`, `NextStep`
- 프록시 분석 `Message`, `Warnings`, `Limitation`
- 후보 `EndpointLabel`, `Message`, `Warnings`
- 내부 URL·외부 URL·프록시 호스트
- 전체 인터페이스 GUID·이름·설명
- 주소별 원본 route evidence
- 예외 메시지

정의되지 않은 enum, 잘못된 scope·port·지문과 음수 개수는 `Unknown`, `all`, `-`, `없음`, `0` 같은 고정 안전값으로 치환합니다.

## 메모리 상태

가장 최근 구조화 실행 결과는 현재 앱 프로세스 동안 다음 필드에만 유지합니다.

```text
LatestRouteComparisonRunV3
```

후속 전용 보고서 기능은 이 메모리 결과를 안전한 보고서 DTO로 재매핑해 저장해야 합니다. 원본 내부·프록시 route evidence를 그대로 직렬화하면 안 됩니다.

앱 종료 시 메모리 결과와 사용자가 입력한 원문은 사라집니다.

## 자동 검증

Release 검증의 첫 단계에서 `test-route-comparison-ui-v3-contract.ps1`을 실행합니다.

검사 항목:

- UI가 `InternalProxyRouteComparisonCoordinator`만 호출
- `RunManualDirectiveAsync` 호출 정확히 1회
- UI에서 직접 파서·route reader·프록시 분석기·비교기 호출 없음
- UI에서 HTTP·WebRequest·WinHTTP 직접 호출 없음
- 실행 중 탭 상태 저장·복원과 취소 경계
- 안전 렌더러가 고정 Finding mapper 사용
- 렌더러가 실행·비교·분석·후보의 자유형 문장을 읽지 않음
- 완료·DIRECT·Blocked·악성 구조화 필드·자유형 비반사 테스트 존재

ReportSmoke는 다음을 확인합니다.

1. 완료된 `Diverged` 비교와 후보 순서
2. DIRECT·Blocked에서 비교·프록시 근거 미생성
3. 정의되지 않은 enum과 잘못된 scope·port·지문·음수 개수 치환
4. 실행·비교·route 자유형 URL·호스트·이메일·IP·GUID 비반사
5. 안전한 10자리 지문과 고정 Finding 유지

## 실제 Windows 검증

1. 경로 비교 탭이 앱 시작 후 한 번만 생성되는지 확인합니다.
2. Wi-Fi 단독 상태에서 내부·프록시 경로를 비교합니다.
3. VPN 연결 전후 `Ready`·`Diverged` 변화를 확인합니다.
4. 유선과 Wi-Fi 동시 연결 시 인터페이스 우선순위를 확인합니다.
5. 복수 프록시와 DIRECT fallback 순서가 유지되는지 확인합니다.
6. DIRECT가 첫 경로일 때 내부·프록시 DNS 조회가 발생하지 않는지 확인합니다.
7. 내부 DNS 실패 후 프록시 후보 조회가 시작되지 않는지 확인합니다.
8. 실행 중 다른 탭이 비활성화되고 종료 후 이전 상태로 복원되는지 확인합니다.
9. 중지 버튼과 창 종료 시 이후 후보 조회가 중단되는지 확인합니다.
10. 결과 영역에 실제 입력 URL·프록시 호스트·전체 GUID·인터페이스 이름이 나타나지 않는지 확인합니다.

실제 회사 PAC·WPAD, 프록시, VPN과 Windows 11 UI 동작 검증은 사용자 환경에서 수행해야 합니다.
