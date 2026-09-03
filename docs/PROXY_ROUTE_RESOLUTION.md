# 대상 URL별 프록시 경로 판정

## 목적

회사 프록시 서버를 직접 관리하거나 내부 로그를 볼 수 없어도, 현재 로그인한 Windows 사용자에게 적용된 설정을 기준으로 특정 HTTP/HTTPS URL이 `PROXY` 또는 `DIRECT` 경로를 사용할지 확인합니다.

이 기능은 프록시 서버 상태를 진단하지 않습니다. URL별 예상 경로를 확인해 이후 내부·외부 다운로드 측정 결과를 올바르게 분류하기 위한 사전 단계입니다.

## 두 기능의 차이

### 로컬 프록시 설정 확인

- `WinHttpGetIEProxyConfigForCurrentUser`만 호출
- 수동 프록시 존재 여부, PAC URL 존재 여부, 자동 감지 여부를 확인
- 네트워크 조회 없음
- 실제 프록시 주소와 PAC URL은 표시하지 않음

### 대상 URL별 프록시 경로 확인

- 사용자가 URL과 내부망·외부망 구분을 입력하고 버튼을 눌렀을 때만 실행
- 수동 프록시와 바이패스는 로컬에서 계산
- 자동 감지 또는 PAC가 설정된 경우 WinHTTP가 WPAD/PAC를 조회하고 스크립트를 평가
- 대상 외부 사이트의 파일 본문은 다운로드하지 않음
- PAC 스크립트가 `dnsResolve` 등 네트워크 함수를 사용하면 대상 호스트와 관련된 DNS 조회가 발생할 수 있음

## 판정 순서

```text
입력 URL 검증
  ↓
현재 사용자 프록시 설정 읽기
  ↓
자동 감지 사용 시 WPAD 판정
  ├─ 성공 → PROXY 또는 DIRECT
  └─ 복구 가능한 실패
       ↓
명시적 PAC URL이 있으면 PAC 판정
  ├─ 성공 → PROXY 또는 DIRECT
  └─ 복구 가능한 실패
       ↓
수동 프록시가 있으면 바이패스 포함 fallback
  ├─ 해석 가능 → PROXY 또는 DIRECT
  └─ 없음/해석 불가 → 판단 불가
```

WPAD와 PAC 플래그를 한 호출에 동시에 넘기지 않습니다. 각 단계를 분리해 실제로 어느 방식이 결과를 만들었는지 기록합니다.

## Windows 통합 인증 재시도

PAC 또는 WPAD 정보를 가져오는 과정에서 WinHTTP가 `ERROR_WINHTTP_LOGIN_FAILURE`를 반환한 경우에만 `fAutoLogonIfChallenged`를 활성화해 한 번 다시 시도합니다.

처음부터 자동 로그온을 항상 켜지 않는 이유는 불필요한 자격 증명 전달 범위를 줄이고 WinHTTP의 자동 프록시 캐시 동작을 방해하지 않기 위해서입니다.

이 인증은 **PAC/WPAD 파일을 가져오는 단계의 인증**입니다. 향후 내부·외부 파일 다운로드 요청에서 프록시가 반환하는 HTTP `407 Proxy Authentication Required` 처리와는 별개이며, 실제 요청의 407 처리는 Issue #4에서 구현합니다.

## 수동 프록시와 바이패스

지원하는 기본 형식:

```text
proxy.example.invalid:8080
http=proxy-http.example.invalid:8080;https=proxy-https.example.invalid:8443
```

지원하는 바이패스 예:

```text
<local>
*.corp.invalid
intranet.corp.invalid
intranet.corp.invalid:8080
```

대상 URL의 스킴에 적용되는 프록시 항목이 없으면 DIRECT로 판정합니다. 프록시 지시문 자체가 잘못됐다면 DIRECT로 임의 전환하지 않고 `판단 불가`로 처리합니다.

WinHTTP가 반환하는 다중 프록시 목록은 후보 개수와 `DIRECT` fallback 유무만 공개 결과에 남깁니다. 실제 프록시 주소 목록은 같은 Windows 어셈블리 내부의 후속 다운로드 엔진에서만 사용할 수 있고, WPF 화면이나 public 설정 API에는 노출하지 않습니다.

## 내부망·외부망 기대 경로

| 사용 목적 | 기대 경로 | 불일치 의미 |
|---|---|---|
| 내부망 기준 측정 | DIRECT | 프록시가 포함돼 순수 내부 경로 기준으로 사용할 수 없음 |
| 외부망 체감 측정 | PROXY | 실제 브라우저 경로와 프로그램 경로가 다를 가능성 |

경로 불일치는 측정 실패와 다릅니다. 이후 처리량 측정을 실행하기 전에 사용자에게 경고하고 결과 신뢰도를 낮추는 근거로 사용합니다.

## 제한 시간과 UI

WinHTTP 세션에는 5초 기본 제한 시간을 설정하며 허용 범위는 1~30초입니다. WPAD와 PAC를 순서대로 시도하면 전체 체감 시간은 한 단계의 제한 시간보다 길 수 있습니다. 동기 WinHTTP 호출은 WPF의 백그라운드 작업에서 실행해 화면 응답을 막지 않습니다.

일부 Windows·네트워크 환경에서는 자동 프록시 탐색 시간이 WinHTTP의 개별 timeout보다 길 수 있으므로 실제 회사 환경에서 별도 검증해야 합니다. 현 단계에서 강제 스레드 종료나 핸들 강제 폐쇄를 사용하지 않습니다.

## 자동 테스트 경계

GitHub Actions에서는 실제 회사 PAC/WPAD 또는 외부 URL을 조회하지 않습니다. 다음만 자동 검증합니다.

- 수동 프록시와 프로토콜별 선택
- 바이패스 패턴
- 다중 프록시와 DIRECT fallback
- 잘못된 설정의 판단 불가 처리
- 내부·외부 기대 경로 규칙
- public 프록시 설정 값 마스킹
- WinHTTP P/Invoke 컴파일과 self-contained publish

실제 PAC/WPAD 판정은 회사 Windows 11 PC에서 수동 검증해야 합니다.
