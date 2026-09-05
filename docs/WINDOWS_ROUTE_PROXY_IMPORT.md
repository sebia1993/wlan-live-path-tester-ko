# Windows 프록시 판정 불러오기

이 기능은 기존 `경로 비교` 탭에 Windows 설정 불러오기와 불러온 판정으로 비교하는 두 동작을 추가합니다. v3 화면의 수동 입력 경로는 그대로 보존합니다. 기존 v3 가이드의 수동 입력 설명에 이 문서의 가져오기 절차를 추가하여 사용하십시오.

## 사용 순서

1. 외부 프록시 판정 대상 URL에 승인된 절대 HTTP(S) URL을 입력합니다.
2. `Windows 프록시 불러오기`를 누릅니다. 기본값은 로컬 Windows 설정 읽기입니다.
3. PAC 또는 WPAD가 설정되어 있다면 조회 동의가 필요하다는 상태를 표시합니다. 이때 수동 프록시 설정으로 자동 대체하지 않습니다.
4. 회사 정책상 허용되는 경우에만 `PAC/WPAD 조회·스크립트 획득 및 필요 시 Windows 통합 인증을 허용합니다`를 체크하고 다시 불러옵니다.
5. 출처·상태·후보 수·DIRECT·통합 인증 재시도 여부를 확인합니다. 실제 주소나 지시문은 화면에 복사하지 않습니다.
6. 내부 DIRECT 대상도 입력한 뒤 `불러온 Windows 판정으로 비교`를 누릅니다.
7. 최근 실행 결과는 기존 `경로 보고서`에서 로컬 JSON·CSV·HTML·SHA256SUMS로 저장할 수 있습니다.

기존 수동 지시문 입력은 덮어쓰지 않습니다. 사용자가 직접 수동 입력을 사용하려면 별도의 `수동 입력으로 비교` 버튼을 누릅니다. 불러오기 실패 시 이 버튼을 자동 실행하지 않습니다.

## 통신 경계

| 동작 | 수행하는 작업 |
|---|---|
| 기본 불러오기 | 현재 사용자의 Windows 프록시 설정을 로컬 API로 읽고 수동 매핑·bypass를 로컬에서 해석 |
| PAC/WPAD 설정 + 동의 없음 | NeedsAutomaticLookupConsent, 자동 resolver 호출 0회 |
| PAC/WPAD 설정 + 명시 동의 | 기존 WinHTTP resolver로 해당 URL 판정. 설정에 따라 WPAD 탐색, PAC 획득·평가, PAC 내부 DNS, 필요 시 Windows 통합 인증 재시도가 가능 |
| 불러온 판정으로 비교 | 기존 코디네이터가 입력/출처/DIRECT를 검증하고 필요한 내부·프록시 DNS와 Windows 첫 로컬 인터페이스만 확인 |
| 보고서 저장 | 이미 수집한 메모리 결과와 로컬 파일 시스템만 사용 |

새 HTTP 다운로드, 프록시 서버 관리 API, 결과 업로드, 텔레메트리, 외부 분석 API, AI 또는 로컬 AI는 추가하지 않습니다. PAC/WPAD를 사용하는 경우까지 모든 외부 통신이 없다고 설명하지 않습니다. 통합 인증 재시도 여부는 성공 여부를 보장하지 않습니다.

## 기존 구현 재사용

- `CurrentUserProxySettingsReader.ReadRaw`
- `ProxyRouteResolver.ResolveDetailed`
- 기존 수동 bypass 해석
- `ProxyDirectiveSourceSnapshotSelectionPolicy`
- `ProxyEndpointParser`
- `InternalProxyRouteComparisonCoordinator.RunAsync`

새 네이티브 API 선언이나 중복 WinHTTP handle 관리 코드는 추가하지 않습니다. 기존 resolver의 메모리·handle 해제 경로를 그대로 사용합니다. 다운로드 측정용 resolver의 동작도 변경하지 않습니다.

## 자동 fallback을 적용하지 않는 경계

기존 측정용 resolver는 자동 판정 실패 뒤 수동 설정 fallback을 반환할 수 있습니다. 가져오기 계층은 `ManualFallback` 결과를 성공한 PAC/WPAD 판정으로 채택하지 않습니다. 자동 판정 중 설정이 Manual 또는 None으로 바뀐 경우도 다시 불러오도록 거부합니다.

이는 기존 resolver 내부에서 수동 후보를 계산하지 않는다는 뜻은 아닙니다. 계산된 fallback을 가져오기 결과로 적용하지 않고, 그 결과로 DNS 경로 비교나 다운로드 측정을 시작하지 않는다는 뜻입니다.

설정 읽기 실패나 프록시 설정 없음도 DIRECT로 추정하지 않습니다. 명시적인 수동 bypass 또는 성공한 대상별 DIRECT 판정만 DIRECT로 사용합니다. 수동 프로토콜 매핑에서 대상 스킴에 적용할 후보가 없으면 가져오기를 거부합니다.

## 대상 바인딩과 유효기간

성공한 선택은 원본 주소를 화면/JSON에 표시하지 않는 메모리 전용 객체에 유지합니다. 사용할 때 다음 조건을 모두 다시 확인합니다.

- HTTP(S) 절대 URL
- 사용자 정보, fragment, 제어 문자 없음
- 최대 2048자
- 가져오기 당시 URL과 현재 절대 URL이 정확히 같음: 경로와 query의 대소문자 변경도 감지
- monotonic clock 기준 5분 미만

UI에서 URL을 변경하면 이전 선택을 즉시 폐기합니다. 5분은 표시·작업용 만료 정책이지 네트워크 설정이 5분 동안 같다는 보장이 아닙니다. Wi-Fi·VPN·유선 상태나 프록시 정책이 바뀌면 다시 불러와야 합니다. OS 시계 수정으로 유효기간을 연장하지 않습니다.

## 지원 범위와 한계

가져오는 것은 기존 Windows resolver가 해석한 HTTP/HTTPS 후보와 DIRECT 순서입니다. PAC 스크립트 원문 전체나 모든 브라우저의 프록시 설정을 복제하는 기능이 아닙니다.

- PAC/WPAD 판정 일부가 해석되지 않으면 유효한 일부 후보만으로 성공 처리하지 않습니다.
- 자동 결과의 DIRECT-first와 대체 프록시 혼합은 현재 출처 정책에 안전하게 표현하지 못하므로 거부합니다. 원문을 확보했다면 기존 수동 입력 기능으로 별도 비교할 수 있습니다.
- 기존 자동 resolver가 지원하지 않는 SOCKS 등은 일부 성공으로 축소하지 않습니다. 수동 입력/Windows 수동 설정의 지원 범위와 자동 가져오기 범위가 다릅니다.
- 여러 프록시 후보가 있다고 실제 네트워크 요청이 어떤 후보에 연결됐는지 증명하는 것은 아닙니다.
- Firefox 독립 설정, 브라우저 확장, 브라우저별 기업 정책, 프록시 서버 내부 상태는 확인하지 않습니다.
- 가져오기 화면의 후보 수는 전체 지시문 기준일 수 있습니다. 실제 비교 코디네이터는 현재 대상의 범위를 다시 적용합니다.

## 취소·창 닫기

WinHTTP 자동 판정은 동기식입니다. worker thread에서 실행하며 UI thread를 직접 막지 않습니다. 현재 단계의 WinHTTP 제한 시간은 5000ms이고 전체 작업의 절대 완료 상한은 아닙니다. WPAD→PAC 및 인증 재시도가 추가될 수 있습니다.

취소 요청이 들어와도 worker를 버리거나 다른 호출로 교체하지 않습니다. 네이티브 호출이 반환된 후 결과를 폐기하고 실행 잠금을 해제합니다. 그 동안 같은 importer의 재진입은 Busy로 거부합니다.

일반 창 닫기는 가져오기/가져온 판정 비교 작업이 종료될 때까지 보류하고 Dispatcher에서 다시 닫기를 요청합니다. 이미 닫힌 창에는 결과를 갱신하지 않습니다. 강제 종료·OS 종료·전원 차단의 완료 대기는 보장하지 않습니다.

## 안전한 공개 결과

공개 결과에는 상태, 출처, 수집 시각, 자동 resolver 호출 시도 여부, 통합 인증 재시도 여부, bypass 여부와 개수만 포함합니다. 원문 URL·PAC URL·지시문·프록시 호스트·bypass 목록·예외 메시지를 포함하지 않습니다.

기존 보고서는 가져온 선택의 `TargetSpecificAutoProxy` 또는 `ManualProxyConfiguration` 출처를 유지합니다. 가져온 PAC 선택을 수동 입력용 오버로드로 전달하지 않습니다. 원래 PAC URL과 스크립트는 저장하지 않습니다.

## 자동 검증

WindowsSmoke의 Main에서 16개 주입식 시나리오 그룹을 실행합니다. 새 테스트는 실제 외부 DNS, PAC, 프록시 서버를 호출하지 않습니다. invalid input, 사전 취소, 로컬 수동 설정, bypass, 설정 없음/실패, 명시 동의, 자동 출처와 순서, DIRECT, fallback/부분 결과 거부, URL/monotonic 만료, JSON 비노출, 취소 중 재진입과 오류 후 복구를 검사합니다.

`test-windows-proxy-import-contract.ps1`은 UI opt-in, 기존 reader 재사용, 원문 입력 미덮어쓰기, 별도 coordinator 호출, 취소/종료와 이벤트 해제의 소스 계약을 검사합니다. 실제 WPF 버튼 조작을 대체하지 않습니다.

Portable ZIP 필수 문서는 기존 17개를 유지하고 이 문서를 추가해 18개입니다. 독립 합성 ZIP 검사는 기존 8개와 새 가이드 누락 사례를 포함해 9개입니다.

## 참고한 Windows API 계약

- https://learn.microsoft.com/en-us/windows/win32/winhttp/autoproxy-issues-in-winhttp
- https://learn.microsoft.com/en-us/windows/win32/winhttp/autoproxy-cache
- https://learn.microsoft.com/en-us/windows/win32/api/winhttp/nf-winhttp-winhttpgetieproxyconfigforcurrentuser

실제 회사 PAC/WPAD, Windows 통합 인증, VPN/EDR 및 WPF 상호작용 검증은 사용자 환경에서 별도로 수행합니다.
