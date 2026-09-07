# 구현 상태

소스 상태 갱신일: 2026-09-07

코드 작성, 정확한 commit의 자동 검증 성공, main 병합, 공개 EXE·ZIP 배포와 사용자 회사 환경 검증을 각각 구분합니다.

## 현재 검증된 main

- PR #129 merge: `6870775fc257c73b43bfdcc26ecc50916d1a0acf`
- 고정 완료 기준: **17/20 = 85%**
- #16 중앙 실행·종료 수명 통합까지 완료
- 미완료 기준: #18 원문 입력, #19 문서/공개 배포, #20 비동기 WinHTTP
- 실제 회사 환경 검증과 Authenticode 인증서·서명은 별도 출시 조건

진행률은 `docs/DEVELOPMENT_PROGRESS.md`의 동일한 20개 기준과 항목당 5%를 사용합니다. 코드량·작업 시간·테스트 통과율이 아닙니다. 부분 구현·PR 생성·일부 CI 성공에는 완료 점수를 더하지 않습니다.

## 작업 중: PR #130 원문 입력 경계

브랜치: `fix/raw-network-input-boundaries`. 기반은 위 #129 main입니다. **초안·미병합 상태를 유지합니다.**

### 최근 개발 및 실제 검증

검증한 소스 head: `af25a677c8400d06fd1bdc5a0708058de9ca8ce7`.
PR 통합 checkout: `0d1ead8089115e38c6717f9f85869e682d3fc148`.
이후 변경 내역·상태 문서 갱신은 이 소스 검증과 구분합니다.

| 항목 | 결과 |
|---|---|
| 일반 라우팅 원문 연결 | LocalRouteEvidenceReader가 선행 Trim 없이 공통 parser에 전달 |
| 라우팅 취소 경계 | 사전 취소 시 DNS/route 미실행, native 조회 뒤 취소 재확인 |
| Windows 가져오기 원문 | URI OriginalString·PAC·수동 설정·bypass 검사 연결 |
| 수동 판정 오류 | SelectManual의 Unknown/invalid를 버린 뒤 원문을 정상 후보로 재선택하지 않음 |
| 실패·출처 보존 | 잘못된 PAC의 자동 조회 차단, 정상 자동 결과에서 미사용 수동 설정 격리 |
| 새 회귀 | InputBoundarySmoke 27그룹 및 importer raw 6그룹 |
| 컴파일 수정 | Ensure의 실제 throw 계약에 DoesNotReturnIf 표시, CS8604 수정 |
| Release 검사 | 독립 suite/audit 실패를 모으되 하나라도 실패하면 throw; 패키지는 계속 차단 |

확인한 CI:

- Windows CI `34086334530`: success
- Observation Guide Package CI `34086334632`: success
- Release Package CI `34086334800`, job `101630983575`: **failure**
- solution Release build: 경고 0, 오류 0
- 전체 프로젝트: **12/13 성공**
- InputBoundarySmoke: **27그룹 중 23성공, 4실패**, raw matrix assertion 1820개
- 새 라우팅 원문/사전 취소 그룹 성공
- 기존 importer 16그룹 및 새 raw importer 6그룹 모두 성공
- 기존 인증·측정·관찰·보고서·WPF 수명 회귀 성공
- 저장소 375 tracked files와 통신 경계 187 source files 감사 성공
- 패키지 생성·검사는 실패 때문에 skipped; 새 공개 배포 없음

실패한 네 그룹은 HTTP authority/port, route bracket, URL-list line boundary, approved redirect host validation입니다. 빈 명시적 포트·중첩 IPv6 대괄호·긴 공백 전용 URL 줄·반복 root-dot/IDN 예외 경계의 공통 helper 보완 요청이 도구 보안 상태 판정 실패로 반영되지 않았습니다. 이를 우회하는 쓰기나 테스트 완화는 하지 않았습니다.

### 앞서 작성·연결한 코드

| 영역 | 초안 변경 |
|---|---|
| 공통 입력 | NetworkInputBoundary의 길이·제어/숨김 문자·유니코드·URL escape 검사 |
| 대상 검증 | raw URL, 외부 IPv4-mapped IPv6·root-dot 로컬 DNS, enum·메타데이터 |
| 리다이렉트 | Location 원문과 결합 후 URL 검사, 최초 승인 대상 host/port 제한 유지 |
| 설정 JSON | UTF-8 1MiB·깊이·대상 수 제한, 중복 속성·null 대상·invalid host 거부 |
| 승인 catalog | authority와 path/query 대소문자 의미 구분, 설치 전 검증·호스트 snapshot |
| 프록시 | 별도 endpoint·runtime directive·bypass raw envelope 검사 |
| WinHTTP | 원문 URL·경로 종류·명시 endpoint의 연결 전 검사, Location 원문 유지 |
| 직접 proxy resolver | URL/PAC 원문 검사 추가; importer와 빈 PAC 의미 등의 일관성 점검은 남음 |

이 목록 전체가 main에 반영됐다는 뜻이 아닙니다. 모든 테스트가 성공한 것도 아닙니다.

### #18의 실제 잔여 범위

공통 helper의 네 실패 그룹과 IDN 예외 처리, App 단일/반복/경로 입력의 선행 Trim 제거·URL 목록 처리, InternalProxyRouteComparisonCoordinator의 원문 연결, 실제 화면 설정 파일 읽기의 bounded reader 연결, 직접 resolver와 importer의 PAC 판정 일관성, 전 진입점 조회 0회 검증·전체 회귀·main 반영이 남습니다.

LocalRouteEvidenceReader와 importer를 다시 미구현으로 분류하지 않습니다. 이번에 연결한 범위와 남은 문법/다른 진입점은 구분합니다. importer의 자동 실패 시 수동 fallback 거부를 측정 resolver 전체의 기존 동작 제거로 확대하지 않습니다.

상세: `docs/RAW_NETWORK_INPUT_AUDIT.md`. #18이 완결되지 않았으므로 **85% 유지**입니다.

## 완료된 중앙 UI 수명 (#129)

수동/불러온 판정의 내부 DIRECT–프록시 비교, Windows 프록시 가져오기와 전용 경로 보고서는 같은 MainWindow UI/Core 실행권을 사용합니다. Core-only 획득 함수, 기능별 peer-tab 목록, 독립 Closing continuation을 제거했습니다. 운영 coordinator 하나와 Closing 구독 한 곳에서 실제 호출·저장·취소 callback·화면 정리 완료를 기다립니다.

기본 WLAN·로컬 프록시 설정·WLAN/NIC 대응도 공통 진단 실행 경로를 사용합니다. 자동 어댑터 갱신과 다섯 auxiliary 보고서는 중앙 snapshot을 참조합니다. 취소·절전·시간 제한 뒤 늦은 route/import 결과는 적용하지 않고, import 도중 URL이 바뀌었다 돌아와도 revision으로 폐기합니다.

Windows importer의 명시 동의 false 기본값, authoritative 출처, 동일 URL 및 monotonic 5분 미만 TTL은 유지됩니다. 대상별 실패를 수동 설정 또는 DIRECT로 바꾸지 않습니다. ReportSaveSession은 기존 writer와 CancelAsync callbacks·CTS 정리를 기다리고 이미 게시된 성공 파일세트를 뒤늦은 취소로 뒤집지 않습니다. 종료 중 저장 실패·정리 경고는 검토를 위해 창을 유지합니다.

#129 검증 head: `9b3afc7d74da419704f0e6e50c01726d37bbab1c`.
Windows CI `34067851791`, Guide CI `34067851837`, Release Package CI `34067851876` 성공. package job `101579687988`에서 build 경고/오류 0, 새 RouteUiLifetimeSmoke 28개 그룹 및 기존 전체 회귀 통과. 소스 29개 파일 변경. 새 공개 Release는 게시하지 않았습니다.

## 확인된 이전 변경

- #119: Core coordinator의 단일 lease·취소·shutdown·idle 대기
- #121/#122: 경로 비교·Windows 가져오기·전용 경로 저장의 최초 전역 실행권 연결
- #123: 측정·프록시 판정·브라우저 관찰 WPF 수명
- #125: 지정 프록시 파서 원문 검증과 취소 후 늦은 반환 차단
- #126: 반복 전용 종류·같은 탭 중지·진행 알림 분리·부분 결과 보존
- #127: 통합 보고서 optional 경로 snapshot·schema 1.2·Finding 통합·호환성·저장 수명
- #128: 세 진단·다섯 전용 보고서·자동 갱신의 공통 수명

주요 merge:
#126 `6a7903f1f17aeeac4826fbb93a6c0eb327f4a17d`,
#127 `00bd8c71511514969aca7d23d0147c41beb04f3f`,
#128 `0d1705d0c646bf50915c5fcab55a3527a6c0fb5b`,
#129 `6870775fc257c73b43bfdcc26ecc50916d1a0acf`.
구형 #124는 대체 완료로 종료됐으며 중복 병합하지 않았습니다.

## 자동 검증 프로젝트

main의 기존 12개: Core SelfTest, WindowsSmoke, ProxyAuthSmoke, MeasurementSmoke, ObservationSmoke, ReportSmoke, UiOperationSmoke(10그룹), ProxyBoundarySmoke(12그룹), RepeatedUiSmoke(11그룹), UnifiedReportSmoke(17그룹), OperationLifecycleSmoke(25그룹), RouteUiLifetimeSmoke(28그룹).

초안 #130은 InputBoundarySmoke(현재 27그룹)를 추가합니다. WindowsSmoke는 기존 importer 16그룹에 raw importer 6그룹을 더 실행합니다. 새 raw 테스트는 주입 함수 또는 native 연결 전 거부/사전 취소만 사용합니다. 성공한 회사 DNS·외부 HTTP·실제 PAC/WPAD 요청은 실행하지 않습니다. 기존 HTTP 회귀는 loopback 합성 서버·프록시를 사용합니다.

## 기존 제품 기능과 실환경 확인

Native WLAN의 SSID·BSSID·RSSI·채널·PHY·링크 속도, 내부 DIRECT 및 외부 PROXY HEAD/GET 처리량·TTFB, 407 통합 인증, 승인 대상 정책, 반복 통계·수신량 예산, Wi-Fi 카운터 관찰·로밍/NIC/절전 연속성, 어댑터 분류, 정확 GUID 경로 비교, Windows 프록시 가져오기와 로컬 JSON·CSV·오프라인 HTML·SHA-256 보고서가 구현되어 있습니다.

회사 드라이버·권한·WLAN 서비스, 승인 서버·GPO, PAC/WPAD·Negotiate/NTLM·TLS 검사, VPN·유선/Wi-Fi 병행·IPv4/IPv6 분리, EDR/SmartScreen 및 실제 보고서 개인정보는 사용자가 실제 PC에서 검증합니다. 합성 자동 검사를 그 검증의 대체물로 표현하지 않습니다.

## 남은 개발와 제약

### #18 원문 입력

위 실패 경계 수정, 실제 App/Windows/config 파일의 잔여 진입점 연결, 전체 회귀·소스 검토·main 반영이 필요합니다.

### #19 배포

최종 운영 문서 Portable ZIP 포함, 새 공개 Release, 게시 후 재다운로드한 바이트·해시·태그·BUILD_INFO 확인이 필요합니다. CI용 패키지 성공이 공개 배포 완료는 아닙니다. 이번 head는 패키지 단계가 skipped이고 기존 자산도 바꾸지 않습니다.

### #20 전송

WINHTTP_FLAG_ASYNC 기반 안전한 취소와 callback/handle 수명, 407 재인증 회귀가 필요합니다. 기존 동기 WinHTTP 전송 계층은 이번 입력 감사에서 교체하지 않았습니다.

### 별도 저장·출시 조건

통합/일부 legacy writer의 파일별 저장은 파일 묶음 전체 rollback을 보장하지 않습니다. 안전한 중간 취소가 없는 writer는 실제 쓰기 완료를 기다리며 실패 시 일부 파일 가능성을 안내합니다. UI 수명 통합 완료가 모든 writer 교체 완료를 뜻하지 않습니다.

서명 인증서 확보 후 Authenticode·타임스탬프·CI 서명 검증, 사용자 회사 환경 검증과 피드백 반영은 별도 출시 조건입니다. 개발 백분율과 실환경/서명 완료 여부를 구분합니다.

## 제품 경계

runtime NuGet, AI/로컬 AI, 외부 분석 API, 텔레메트리, 업로드, 자동 업데이트, 프록시 관리 접근을 추가하지 않습니다. 기존 사용자 실행 DNS/HEAD/GET와 Windows proxy authentication을 유지합니다. 초안 변경으로 공개 Release를 게시하지 않습니다.
