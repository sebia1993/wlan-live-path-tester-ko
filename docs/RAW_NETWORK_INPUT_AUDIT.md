# 네트워크 원문 입력 감사 — 검증 기록

## 최신 검증 및 병합 (2026-09-07)

PR #130 소스 `fd8c047e4c827b11278a2ff40f6bd54a461c5eb7`은 Windows CI `34096879339`, Guide `34096879388`, Package `34096879400`에서 성공했습니다. 패키지 로그의 입력 검사는 27그룹·1820 raw matrix assertions·0실패이며 전체 13/13 suite와 패키지 생성·검사가 성공했습니다. main 병합은 `000ec4a`입니다.

공통 helper의 이전 실패 네 그룹과 App 단일/반복 URL·경로·설정 파일 읽기 연결을 보완했습니다. `scripts/test-raw-network-input-wiring.ps1`이 해당 운영 연결을 검사합니다. 아래는 이전 revision의 실패 이력이며 현재 상태가 아닙니다. 공개 Release 게시 및 회사 실환경 검증은 이 병합에 포함하지 않습니다.

## 이전 revision 감사 이력

기반 main: `6870775fc257c73b43bfdcc26ecc50916d1a0acf`.
작업 브랜치: `fix/raw-network-input-boundaries`, 초안 PR #130.
검증한 소스 head: `af25a677c8400d06fd1bdc5a0708058de9ca8ce7`.

**현재 결과: 솔루션 빌드 성공, 기존 12개 테스트 프로젝트 성공, 새 입력 테스트 27개 그룹 중 4개 실패. PR은 미병합이며 패키지 생성도 차단됐습니다.** 코드 존재·일부 CI 성공을 전체 검증 완료로 계산하지 않습니다.

## 최근 추가한 운영 연결

### 일반 Windows 라우팅 조회

`LocalRouteEvidenceReader.TryExtractHost`는 전달받은 원문을 그대로 공통 `NetworkInputBoundary.TryRouteHost`에 넘깁니다. 어댑터에서 먼저 Trim하거나 Uri를 구성하지 않습니다.

정의되지 않은 조회 목적과 DNS 제한 시간을 거부합니다. 사전 취소이면 DNS·인터페이스 resolver를 시작하지 않고 `Canceled`와 `DnsWasUsed=false`를 반환합니다. 네이티브 경로 조회 뒤에도 취소를 다시 확인합니다. 주소 수 제한과 기존 경로 결과 평가는 유지합니다.

이 연결은 공통 helper의 미완성 문법까지 해결했다는 의미가 아닙니다. 아래 IPv6 괄호 관련 실패는 그대로 남아 있습니다.

### Windows 프록시 가져오기

`WindowsRouteProxyImporter.IsValidTarget`는 URI의 `OriginalString`에 공통 HTTP 검증을 적용하며 기존의 엄격한 대상 identity와 앞뒤 공백 거부를 유지합니다.

- nonempty PAC 원문이 잘못됐으면 자동 resolver 호출 전에 `UnsafeOrUnsupportedResult`
- WPAD도 활성화돼 있다고 손상된 명시 PAC를 무시하거나 수동 설정으로 대체하지 않음
- 수동 설정과 bypass의 원문 길이·문자 검증 후에만 선택
- 제어 문자만 있는 수동 설정을 `NoConfiguredProxy` 또는 DIRECT로 축소하지 않음
- 기존 SelectManual이 Unknown/invalid를 반환하면 원문을 다시 파싱해 유효 후보로 되살리지 않음
- 자동 결과의 proxy hop 원문도 HTTP authority로 검증한 뒤 변환
- rejected/truncated endpoint 결과는 완전한 가져오기 결과로 사용하지 않음
- 유효한 대상별 자동 결과에서는 사용하지 않는 수동 설정·bypass 오류를 원인으로 판정을 폐기하지 않음

명시 동의 false 기본값, 자동 판정 출처 보존, 동일 URL·monotonic 5분 미만 TTL, native 호출이 실제 반환될 때까지 busy 유지, 실패 뒤 재시도는 유지합니다. 자동 판정 실패를 수동 fallback으로 적용하지 않는 규칙은 **importer**의 계약입니다. 기존 측정 resolver의 ManualFallback 동작까지 제거한 것으로 표현하지 않습니다.

### 검증 실행 방식

새 테스트의 nullable 컴파일 오류 CS8604를 `Ensure`의 실제 throw 계약에 맞는 `DoesNotReturnIf(false)` 표시로 수정했습니다. nullable 검사 전체를 끄거나 경고를 무시하지 않습니다.

`verify-release.ps1`은 독립된 모든 smoke 프로젝트와 두 감사를 실행하고 실패 목록을 모읍니다. 하나라도 실패·누락되면 마지막에 throw하여 비정상 종료하므로 **패키지 생성은 허용되지 않습니다**. 빌드·사전 source contract 실패 역시 즉시 중단합니다. 실패 테스트를 삭제하거나 기대값을 약화하지 않았습니다.

## 앞서 작성된 공통 검사

### 원문

NetworkInputBoundary는 DNS·HTTP·파일 조회 없는 순수 검사입니다. 원문 길이, 제어/숨김 문자, 잘못된 Unicode를 먼저 확인한 뒤 일반 앞뒤 공백을 정리합니다. URL은 HTTP(S), 사용자 정보, fragment, 역슬래시, percent escape를 검사합니다.

길이는 .NET UTF-16 code unit 기준으로 URL 2048, host 253, name 128입니다. JSON은 별도로 1MiB UTF-8 바이트 상한을 사용합니다. 정상 한글·Unicode 경로와 %20 등을 무조건 거부하지 않습니다.

### 측정·리다이렉트·catalog

TargetValidator는 Uri 생성 전 원문을 검사하며 IPv4-mapped IPv6에는 대응 IPv4 로컬/사설 정책을 적용합니다. 외부 DNS 이름의 root dot으로 localhost·single-label 정책을 피해 가지 않게 합니다.

RedirectTargetValidator는 Location 원문과 상대 URL 결합 결과를 각각 확인합니다. 원래 승인된 대상의 host/port 정책은 TargetHostPolicy가 별도로 적용하며 최상위 다운로드 runner는 두 검사를 모두 호출합니다. scheme/authority와 path/query의 대소문자 의미를 분리했습니다.

catalog는 새 정의 전체를 검증한 뒤 교체하고 허용 호스트 목록을 복사합니다. 설치 뒤 호출자가 원본 배열을 바꿔도 승인 정책이 바뀌지 않습니다. 로더는 빈/잘못된 허용 호스트를 조용히 삭제하지 않습니다.

### 설정 JSON

직접 문자열 로드에도 UTF-8 1MiB·깊이·대상 수 제한을 적용합니다. 중복 속성은 대소문자 및 JSON escape 해제 후 이름 기준으로 거부하고 unknown member·주석·trailing comma 금지를 유지합니다. null 대상·잘못된 호스트도 거부합니다. 오류 문구에 임의 속성·대상 이름·URL 원문을 반사하지 않습니다.

### 프록시와 최종 전송

별도 ProxyEndpointParser, 실제 전송의 ProxyDirectiveParser/ProxyBypassMatcher에 raw envelope 검사를 추가했습니다. scope·후보 순서·DIRECT fallback·기존 runtime SOCKS 미지원 의미는 유지합니다.

WinHttpRequestExecutor는 proxy 조회 전 요청 원문을 검사하고 명시 endpoint는 native 연결 전에 검증합니다. Location은 Trim하지 않고 상위로 전달합니다. ProxyRouteResolver에도 URL/PAC 원문 검증이 들어가 있으나, 직접 resolver와 importer의 빈 PAC 처리 등 일관성 점검은 아직 남아 있습니다. 이번 변경에서 동기 전송·407 재시도·네이티브 취소 구현을 교체하지 않았습니다.

## 실제 자동 검증

검증한 head: `af25a677c8400d06fd1bdc5a0708058de9ca8ce7`.
PR 통합 checkout: `0d1ead8089115e38c6717f9f85869e682d3fc148`.

| 검사 | 실행 번호 / 결과 |
|---|---|
| Windows CI | 34086334530 — success |
| Observation Guide Package CI | 34086334632 — success |
| Release Package CI | 34086334800 — failure |
| 전체 로그 확인 job | 101630983575 |
| Release solution build | 경고 0, 오류 0 |
| Smoke 프로젝트 | 12/13 성공; InputBoundarySmoke만 실패 |
| InputBoundarySmoke | 27그룹, 23성공·4실패, raw matrix assertion 1820개 |
| 새 Windows importer raw 검사 | 6그룹 모두 성공 |
| 기존 Windows importer | 16그룹 성공 |
| 저장소 / 통신 감사 | tracked 375개 / source 187개 성공 |
| 이번 head의 패키지 생성·검사 | 테스트 실패로 skipped |

기존 UiOperationSmoke 10, ProxyBoundarySmoke 12, RepeatedUiSmoke 11, UnifiedReportSmoke 17, OperationLifecycleSmoke 25, RouteUiLifetimeSmoke 28개 그룹과 Core/Windows/인증/측정/관찰/보고서 회귀가 통과했습니다.

새 라우팅 검사는 잘못된 원문을 로컬 파싱하거나 사전 취소 경로만 사용합니다. importer의 새 6개 그룹은 config와 resolver delegate를 주입합니다. 실제 회사 DNS·PAC/WPAD·외부 사이트에 접근하지 않습니다. 기존 HTTP 검사는 원래의 loopback 서버·프록시를 유지합니다.

### 실제 실패한 네 그룹

1. `HTTP authority credentials fragments and ports`
2. `route literal bracket boundaries`
3. `URL-list line boundaries and case-sensitive resources`
4. `exact approved redirect hosts are bounded and validated`

코드 검토상 남은 항목은 빈 명시적 포트, 중첩 IPv6 대괄호, 긴 공백 전용 URL 목록 줄, 반복 terminal dot과 IDN 오류 경계입니다. 이들을 보완하는 공통 helper의 update 요청은 도구 보안 상태 판정 실패로 반영되지 않았습니다. 우회 쓰기를 시도하거나 실패 검사를 완화하지 않았습니다. 실패 그룹은 후속 수정의 재현 근거이며 정상으로 기록하지 않습니다.

## #18의 남은 연결

- 공통 helper의 위 실패/IDN 예외 경계 수정과 회귀 재검증
- App 단일·반복 다운로드 URL/목록, 경로 비교·일반 라우팅의 선행 Trim 제거 및 원문 전달
- InternalProxyRouteComparisonCoordinator의 초기 Trim과 URI 검증 연결
- 실제 UI 설정 파일 읽기를 bounded reader로 연결하고 오류 표시 점검
- 직접 ProxyRouteResolver와 importer의 PAC 입력 판정 일관성 및 모든 조회 진입점 검증
- 전체 통합 diff 검토, 모든 자동 검사 성공, 최종 main 반영

## 진행률과 배포

현재 확정 main은 **17/20 = 85%**입니다. 이 초안은 #18의 일부이며 23/27 테스트 통과율이나 새 6개 그룹 성공을 제품 진행률로 계산하지 않습니다. #18 운영 연결·관련 자동 검증·main 반영을 모두 충족해야 추가 5%를 계산합니다.

#19 최종 문서·공개 Release·게시 후 바이트 검증과 #20 비동기 WinHTTP는 별도 미완료입니다. 회사 검증과 서명 인증서/서명도 별도 출시 조건입니다. 이번 실패한 head에서 공개 Release·EXE·ZIP을 생성하거나 기존 자산을 교체하지 않았습니다.

runtime NuGet, AI/로컬 AI, 외부 분석 API, 업로드, 텔레메트리, 자동 업데이트 또는 proxy 관리 접근을 추가하지 않습니다.
