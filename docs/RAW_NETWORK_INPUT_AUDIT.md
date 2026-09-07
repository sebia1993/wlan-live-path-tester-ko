# 네트워크 원문 입력 감사 — 작업 중

기반 main: `6870775fc257c73b43bfdcc26ecc50916d1a0acf`.
작업 브랜치: `fix/raw-network-input-boundaries`, 초안 PR #130.

이 문서는 작성된 변경과 아직 완료되지 않은 연결을 구분합니다. PR 생성·코드 존재는 CI 성공 또는 main 반영의 증거가 아닙니다.

## 이번에 작성한 변경

### 공통 원문 검사

`NetworkInputBoundary`는 DNS·HTTP·파일 조회 없는 순수 검사입니다. 단일 문자열은 먼저 원문 길이와 제어 문자·숨김 문자·잘못된 유니코드를 확인한 후 일반 앞뒤 공백을 정리합니다. URL은 HTTP(S), authority, 사용자 정보, fragment, 역슬래시, percent escape를 추가 검사합니다.

길이 기준은 .NET 문자열의 UTF-16 code unit입니다. URL 2048, 호스트 253, 이름 128을 사용합니다. 설정 문서는 별도로 UTF-8 byte 수를 제한합니다. 한글·정상적인 유니코드 경로와 `%20` 같은 정상 escape를 무조건 거부하지 않습니다.

이 helper의 모든 공개 진입점이 운영 화면에 연결된 것은 아닙니다. 특히 URL 목록과 라우팅 대상용 helper의 운영 연결은 미완료입니다.

### 측정·리다이렉트·승인 정책

- TargetValidator는 Uri 생성 전에 원문을 검사합니다.
- IPv4-mapped IPv6의 로컬/사설 판정은 대응 IPv4 정책을 적용합니다.
- 외부 DNS 이름의 마지막 root dot으로 localhost·single-label 정책을 피해 가지 않도록 합니다.
- allowedRedirectHosts의 잘못된 원문·빈 항목·중복·개수 초과를 삭제 또는 축소하여 통과시키지 않습니다.
- RedirectTargetValidator는 Location 원문과 상대 URL 결합 후 결과를 각각 검사합니다.
- 리다이렉트 구문 검증과 최초 승인 대상의 호스트/포트 허용 판단을 분리합니다. 최상위 다운로드 runner는 여전히 두 검사를 모두 호출합니다.
- 승인 URL의 scheme/authority 비교와 path/query 비교를 구분합니다. path/query는 대소문자를 보존합니다.
- 새 catalog는 전체 정의를 확인한 뒤 한 번에 교체하고, 호스트 목록을 복사해 원본 배열의 사후 수정이 설치된 정책을 바꾸지 않도록 합니다.

### 설정 JSON

문자열 직접 로드에도 1MiB UTF-8 상한을 적용합니다. JSON 깊이와 대상 수를 제한하고, 각 object의 중복 속성은 대소문자 및 JSON escape 해제 후 이름을 기준으로 거부합니다. unknown member·주석·trailing comma 금지 정책을 유지합니다. null 대상 또는 빈/잘못된 허용 호스트는 무음으로 버리지 않습니다.

설정 오류에는 임의의 property 이름·대상 이름·URL 원문을 복사하지 않는 고정 문구를 사용합니다. 기존 UI 설정 읽기를 bounded file reader로 연결하는 작업은 별도 미완료입니다.

### 프록시·최종 전송

별도 ProxyEndpointParser와 실제 전송의 ProxyDirectiveParser/ProxyBypassMatcher에 원문 길이·문자 검사를 추가했습니다. scope·후보 순서·DIRECT fallback과 기존 런타임 SOCKS 미지원 의미는 유지합니다.

WinHttpRequestExecutor는 실제 프록시 조회 전에 원문 요청을 검사하고, 명시적 proxy authority도 연결 전에 확인합니다. 선택 헤더 중 Location은 원문 그대로 상위 리다이렉트 검증기에 전달합니다. 기존 동기 전송·407 재시도·네이티브 취소 구현은 이번 범위에서 교체하지 않았습니다.

## 자동 검증 구성

`InputBoundarySmoke` 26개 그룹을 Release 검증에 추가했습니다. 기존 12개 프로젝트는 유지합니다.

검사 범위는 C0/C1 원문 행렬, 정상 유니코드/escape/IPv6, 원문 길이, URI OriginalString, 외부 주소 분류, 리다이렉트 승인, 중복 JSON/형식/크기/깊이/대상 수, catalog 대소문자·교체 원자성·배열 snapshot, proxy 순서, 오류/직렬화 비노출과 WinHTTP pre-network 거부입니다.

새 테스트는 실제 외부 서버나 회사 DNS·PAC/WPAD를 호출하지 않습니다. 정상 네트워크 요청은 실행하지 않고, 잘못된 요청이 native 연결 전에 거부되는 경로만 호출합니다. 기존 다운로드·인증 회귀는 원래의 loopback 테스트를 유지합니다.

## 확인된 미완료와 병합 차단 조건

공통 helper의 추가 보완 update 요청이 도구 보안 상태 판정 실패로 반영되지 않았습니다. 다음 항목을 회귀 검사에 그대로 포함하여 성공으로 숨기지 않습니다.

- 빈 명시적 포트(`host:`)를 Uri 기본 포트로 자동 보정하지 않기
- 중첩 IPv6 대괄호의 거부
- 공백만 있는 URL 목록 줄도 원문 길이 제한 적용
- 승인 호스트의 반복 terminal dot 및 IDN 변환 예외 경계

그 밖의 #18 잔여 연결:

- App 단일/반복 다운로드 URL과 URL 목록의 선행 Trim 제거 및 raw helper 적용
- 경로 비교·일반 라우팅 화면의 원문 전달
- Windows ProxyRouteResolver의 URL/PAC URL 검증
- LocalRouteEvidenceReader·InternalProxyRouteComparisonCoordinator·WindowsRouteProxyImporter 연결
- 실제 설정 파일 읽기의 bounded reader 사용과 원문 오류 표시 점검
- 위 연결을 대상으로 한 조회 함수 호출 0회 테스트와 전체 회귀

실패한 검사를 삭제하거나 기대값을 약화하여 병합하지 않습니다. 소스 변경을 다른 쓰기 경로로 우회하지 않습니다. 도구 반영이 확인되지 않은 보완을 완료라고 기록하지 않습니다.

## 진행률·출시 경계

현재 main 기준 고정 장부는 **17/20 = 85%**입니다. 이 초안은 #18의 일부 구현이므로 PR 생성이나 일부 테스트 성공만으로 90%로 올리지 않습니다. #18 전체 운영 연결·관련 자동 검증·main 반영이 모두 충족되어야 추가 5%를 계산합니다.

#19 최종 문서 패키징·새 공개 Release·게시 후 바이트 검증과 #20 비동기 WinHTTP는 별도입니다. 회사 실환경 검증과 코드 서명 인증서/서명도 별도 출시 조건입니다.

새 runtime NuGet·AI/로컬 AI·외부 분석 API·업로드·텔레메트리·자동 업데이트를 추가하지 않습니다. 초안 PR에서 공개 릴리스를 생성하거나 기존 배포 자산을 교체하지 않습니다.
