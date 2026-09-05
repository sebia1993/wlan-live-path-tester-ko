# 구현 상태

소스 상태 갱신일: 2026-09-06

소스 구현, 특정 commit의 CI 성공, main 병합, 공개 EXE·ZIP 배포, 사용자 회사 환경 검증은 서로 다른 완료 기준입니다. 작업 종료 보고에는 완료한 범위와 남은 개발·배포·실환경 항목을 함께 기록합니다.

## 이번 변경: 프록시 원문·취소 경계

현재 경로 비교의 기존 타입을 유지하면서 다음을 보강했습니다.

| 영역 | 구현 내용 | 검증 |
|---|---|---|
| 원문 파서 | Trim 전에 원문 4096 UTF-16 코드 단위와 제어 문자 검사 | 제어 문자 전체·길이 경계·빈 입력 |
| 출처 선택 | 실제 선택된 출처의 원문을 파싱하고 결과 재사용 | 대상별·수동·snapshot·사용하지 않는 출처 격리 |
| WPF 수동 입력 | 프록시 지시문을 Trim 없이 coordinator에 전달 | 소스 전달 계약 검사 |
| 분석 Executor | 콜백의 실제 반환 후 취소 재확인, 늦은 payload를 성공으로 게시하지 않음 | 비동기 TaskCompletionSource·값 형식·null·사전 취소 |
| Release 검증 | 기존 7개 테스트와 새 ProxyBoundarySmoke 실행 | 12개 그룹, 실패 시 패키지 생성 차단 |

이 변경은 별도 ProxyEndpointParser 구문, 내부/외부 대상 URL 검증, WinHTTP 전송 자체를 대체하지 않습니다. 모든 입력 계층을 한 번에 수정한 것으로 표현하지 않습니다. 상세 계약은 `docs/PROXY_RAW_INPUT_AND_CANCELLATION.md`에 있습니다. 최종 CI·병합 상태는 해당 변경 PR의 정확한 head와 merge commit으로 확인합니다.

## 확인된 기반: 공통 작업 수명의 WPF 연결

- #119: Core ApplicationOperationCoordinator, 단일 lease, 취소·shutdown·idle 대기
- #121: 내부 DIRECT–프록시 비교와 Windows 프록시 가져오기에서 같은 Core lease 사용
- #122: 경로 보고서 저장의 전역 lease·취소 latch·파일 복구·지연 종료
- #123: 내부·외부 다운로드, 기존 프록시 판정, 브라우저 관찰을 동일 coordinator에 연결

#123은 head `4ae15cfba41e95b86a7b612d24a1d0b9691c05ba`에서 Windows CI `33980714941`, Release Package CI `33980714884`, Observation Guide Package CI `33980714883` 성공을 확인했고, merge commit `d0ea7d1f98f8678632f10afd48d9917921da5f72`로 main에 반영했습니다.

| 항목 | 반영 범위 | 한계 |
|---|---|---|
| 내부·외부 다운로드 | 공통 lease 획득 후 실행, 취소 후 실제 완료까지 유지 | 네이티브 동기 호출은 즉시 중단 아님 |
| 반복 측정 | 공용 RunMeasurementOperationAsync 경유로 실행 잠금 적용 | 전용 종류·버튼·늦은 진행 알림 보완 필요 |
| 기존 프록시 판정 | 동기 WinHTTP 반환까지 lease 유지 | 취소 callback 없음 |
| 브라우저 관찰 | 동일 lease, 이전 세션의 늦은 진행 알림 무시 | 실제 드라이버·절전 확인 필요 |
| 탭 상태 | 늦게 추가된 탭 포함 잠금, 기존 비활성·상속·binding 복원 | 모든 기능의 UI adapter 이행 완료는 아님 |
| 창 종료 | UI를 막지 않고 취소와 실제 완료 대기 | 기존 경로/import/보고서 종료 handler 유지 |

기존 경로·프록시 가져오기 코드를 덮어쓰거나 별도 전역 coordinator를 만들지 않습니다. 자세한 동작은 `docs/APPLICATION_OPERATION_UI_INTEGRATION.md`와 `docs/APPLICATION_OPERATION_COORDINATOR.md`를 참조합니다.

## 기존 구현 영역

| 영역 | 소스 구현 | 사용자 실환경 확인 |
|---|---|---|
| Native WLAN | SSID·BSSID·RSSI·채널·PHY·링크 속도 | 드라이버·권한·WLAN 서비스 |
| 내부 DIRECT 다운로드 | 사용자 실행 HEAD/GET·수신량·TTFB·처리량 | 승인 내부 서버·정책 |
| 외부 프록시 다운로드 | 프록시 경유 HEAD/GET·407 통합 인증 | 회사 PAC/WPAD·Negotiate/NTLM·TLS 검사 |
| 대상 정책 | 사용자/Portable 설정·ProgramData 관리자 우선 정책 | 파일 ACL·GPO·승인 대상 |
| 반복 측정 | 예열·중앙값·편차·신뢰도 | 실제 내부·외부 반복 측정 |
| 브라우저 관찰 | 물리 Wi-Fi 카운터 고정·연속성·구조화 종료 | 로밍·NIC 제거·절전 |
| 어댑터 진단 | 물리 Wi-Fi·유선·VPN·터널·가상 NIC 분류 | 회사 VPN·보안 에이전트 |
| 경로 비교 | 내부 대상·프록시 후보의 Windows 인터페이스 비교 | IPv4/IPv6·유선/Wi-Fi/VPN |
| Windows 프록시 가져오기 | 로컬 설정·명시 동의한 PAC/WPAD 판정 연결 | 회사별 자동 프록시 정책 |
| 전용 경로 보고서 | 안전 모델·취소 가능한 저장·파일 복구·지연 종료 | 공유 전 개인정보 확인 |
| 일반·반복·관찰·어댑터 보고서 | JSON·CSV·HTML·SHA-256 | 실제 데이터 마스킹 확인 |
| self-contained 배포 | win-x64 Portable ZIP·single-file EXE 빌드 경로 | 실행·SmartScreen·EDR |

Windows 프록시 가져오기와 전용 경로 보고서는 이미 구현된 기능입니다. 통합 진단 보고서에 경로 비교를 포함하는 일은 별도의 남은 작업입니다.

## 자동 검증

Release 검증 실행 목록:

1. Core SelfTest
2. WindowsSmoke
3. ProxyAuthSmoke
4. MeasurementSmoke
5. ObservationSmoke
6. ReportSmoke
7. UiOperationSmoke: 실제 WPF Dispatcher의 10개 그룹
8. ProxyBoundarySmoke: 프록시 원문·출처·취소·privacy 12개 그룹

UiOperationSmoke는 실제 창을 표시하지 않고 합성 작업을 주입합니다. ProxyBoundarySmoke는 Core와 TaskCompletionSource만 사용하며, 마지막 WPF 항목은 소스 계약 검사이지 상호작용 테스트가 아닙니다. 기존 HTTP 시험은 루프백 합성 서버·프록시를 사용합니다. 실제 회사 프록시·외부 다운로드 사이트에 대한 시험과 구분합니다.

## 배포와 소스 상태

PR 병합은 기존 Release EXE·ZIP을 변경하지 않습니다. 이번 소스 작업에서는 새 Release를 게시하지 않습니다. 현재 공개 버전·자산·해시는 Release 메타데이터와 해당 게시 후 검증에서 별도 확인합니다.

이 문서의 과거 2026-09-04 기록인 `v0.1.0-alpha.5`, tag commit `bbdad0b7ed8ffca839a83672ac1e4537bb1e29b7`은 과거 기록이며 현재 최신 버전 선언이 아닙니다.

유지할 배포 계약은 Portable ZIP, single-file EXE, SHA256SUMS.txt, THIRD_PARTY_NOTICES.md입니다. 새 배포는 전체 검증 후 게시하고, 다시 내려받은 자산의 크기·SHA-256·필수 문서·BUILD_INFO SourceRevision·태그 대상을 확인해야 합니다. 제공되지 않은 Authenticode 인증서를 적용한 것으로 표시하지 않습니다.

## 남은 개발 작업

### P0 — 중복 변경 정리와 실행 수명

- #124의 기존 측정 gate 변경을 #123과 중복 병합하지 않고 필요한 차이만 검토
- 반복 측정의 고유 작업 종류·전용 중지 버튼·늦은 Progress 처리 통합
- 일반 로컬 경로 확인, 어댑터/환경 수집, 기타 보고서 저장을 같은 UI 수명에 연결
- 기존 busy Boolean·peer-tab·Closing handler 중복 정리
- Loaded 이후 탭 생성·취소·창 종료·어댑터 변경이 겹치는 회귀 검사

### P1 — 입력·보고서·패키징

- 별도 ProxyEndpointParser와 내부/외부 URL 입력 계층의 원문 검증 추가 감사
- 내부 DIRECT–프록시 결과를 LocalDiagnosticReport의 optional 구조화 섹션으로 연결
- Finding 중복·누락 및 일반 무패턴 판정 충돌 방지
- 새 운영 문서의 Portable ZIP 포함과 패키지 계약 갱신
- 별도 Release 빌드·게시 및 게시 후 바이트 재검증

### P1 — 비동기 WinHTTP와 상태 변화

- WINHTTP_FLAG_ASYNC와 callback 기반 안전한 즉시 취소
- 완료·취소·407 재인증 시 네이티브 handle 수명 검증
- 절전·재연결·USB NIC 제거·VPN 전환·장시간 측정 중 이벤트 경쟁
- 필요 시 개인정보 없는 주소별 route/interface metric 확장

### P2 — 서명 및 실환경 피드백

- Authenticode 인증서·타임스탬프 확보 후 서명과 CI 확인
- 사용자 회사 Windows 11·Aruba·PAC/WPAD·407·TLS 검사·GPO·EDR 결과 반영
- 고우선 작업과 실환경 검증 후 정식 v0.1.0 전환 판단

## 사용자 실환경 검증

내장·USB Wi-Fi 동시 사용, 같은 NIC의 BSSID 로밍, NIC 제거·드라이버 재시작·절전 복귀, 회사 VPN·IPv4/IPv6 분리, 프록시·PAC·WPAD·407, TLS 검사, GPO·EDR·SmartScreen과 실제 보고서 개인정보를 확인합니다.

다운로드·관찰 중 취소와 창 닫기, 빠른 재시작, 프록시 판정의 지연 반환 시 새 작업 차단과 UI 복원을 확인합니다. 합성 CI 성공을 회사 환경 검증 완료로 대체하지 않습니다.
