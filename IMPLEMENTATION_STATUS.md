# 구현 상태

소스 상태 갱신일: 2026-09-07

소스 구현, 정확한 commit의 CI 성공, main 병합, 공개 EXE·ZIP 배포, 사용자 회사 환경 검증은 서로 다른 완료 기준입니다. 종료 보고에는 완료 범위·검증 상태·남은 작업·고정 기준 진행률을 함께 기록합니다.

## 이번 변경: 통합 경로 보고서와 저장 수명

기반 main: `6a7903f1f17aeeac4826fbb93a6c0eb327f4a17d`.

| 영역 | 이번 구현 | 검증 대상 |
|---|---|---|
| 통합 모델 | 기존 9개 생성자/Deconstruct 유지, init-only optional 경로 snapshot | 기존 호출·null·이전 JSON·새 JSON 왕복 |
| 안전 매핑 | 기존 전용 보고서 mapper 재사용, raw run을 보고서에 보관하지 않음 | URL·프록시·IP·이메일·GUID·설명 비노출 |
| 형식 | 경로가 있으면 schema 1.2, JSON·CSV·오프라인 HTML에 동일 증거 | 네 비교 상태·전체 실행 상태·필드 누락 |
| Finding | 기존 경로 판정 교체, 일반 무패턴 판정 제거, 다른 판정 보존 | 대소문자·중복·재적용·결정론적 출력 |
| 시각 | 기존 경로 완료 시각 보존, 보고서 생성 시각과 구분 | 별도 수집·동시 측정 추정 금지 안내 |
| 저장 제어 | 같은 UI/Core 조정기에서 DiagnosticReportSave 실행 | 중복/다른 작업 차단·버튼/탭 복원 |
| 창 종료 | 실제 파일 쓰기 완료까지 대기, 종료 중 오류는 창 유지 | 실제 WPF Closing/Closed·오류 검토·재종료 |
| 회귀 | UnifiedReportSmoke 17개 그룹을 전체 Release 검증에 추가 | 보고서 9개·WPF 8개, 기존 테스트 유지 |

자세한 계약은 `docs/UNIFIED_ROUTE_REPORT.md`를 참조합니다. 현재 문서의 코드/테스트 목록은 구현 범위입니다. 최종 성공과 병합은 해당 PR의 정확한 head에 대한 CI 결과와 merge SHA로 확인합니다.

### 이번 변경의 한계

모든 진단과 자동 갱신을 공통 UI 수명에 이행한 것은 아닙니다. 이번에는 통합 보고서의 저장 경로만 추가로 연결했습니다. 다른 전용 보고서·일반 경로 확인·어댑터/환경 수집·자동 갱신은 남아 있습니다.

기존 LocalReportWriter는 파일별 atomic 이동과 순차 충돌 회피를 사용합니다. 전용 경로 보고서의 파일세트 예약·rollback·취소를 모든 writer로 확대한 것은 아닙니다. 통합 보고서 쓰기 중에는 안전한 취소를 제공한다고 주장하지 않고 실제 task 반환까지 기다립니다. 실패 시 이전 성공 경로를 보존하고 부분 파일 확인을 안내합니다.

기존 로컬 WLAN·프록시 설정 capture는 유지합니다. 경로 비교 resolver·DNS·HTTP·PAC/WPAD를 보고서 생성 시 다시 호출하지 않습니다.

## 진행률

`docs/DEVELOPMENT_PROGRESS.md`의 고정 20개 기준, 항목당 5%를 유지합니다. 구현·관련 자동 검증·main 반영을 모두 충족해야 계산합니다.

- 최초 기준: 14/20 = 70%
- #126 반복 측정이 main `6a7903f1...`에 반영된 기준: **15/20 = 75%**
- 이번 #17 통합 경로 보고서가 정확한 최종 head CI 성공 후 main에 반영되면: **16/20 = 80%**
- #16 전체 UI 수명은 일부 보고서 연결만으로 완료 처리하지 않음
- 코드 줄 수·예상 소요 시간·실환경 품질의 백분율이 아님

## 확인된 기반

- #119: Core ApplicationOperationCoordinator와 단일 lease·취소·shutdown·idle 대기
- #121: 내부 DIRECT–프록시 비교와 Windows 프록시 가져오기에서 같은 Core lease
- #122: 전용 경로 보고서의 lease·취소 latch·파일 복구·지연 종료
- #123: 내부·외부 다운로드, 기존 프록시 판정, 브라우저 관찰의 같은 WPF UI adapter
- #125: 지정 프록시 파서·선택기의 원문 길이/제어 문자, WPF 원문 전달, 취소 후 늦은 결과 차단
- #126: 반복 전용 종류·같은 탭 중지·대상/실행별 진행 알림·부분 결과 보존·창 종료

#126 검증 head `fc3ded659f60f3cb76889ba256c3f8ddda63ec26`: Windows CI `34043887817`, Release Package CI `34043887800`, Guide CI `34043887839` 성공. merge `6a7903f1f17aeeac4826fbb93a6c0eb327f4a17d`. RepeatedUiSmoke 11개 그룹과 기존 전체 회귀를 통과했습니다. 중복 구조였던 #124는 병합하지 않고 대체 완료로 종료했습니다.

새 전역 coordinator·timer·ModuleInitializer를 도입하지 않습니다. 기존 동기 WinHTTP는 실제 반환을 기다리는 협력적 취소이며 비동기 즉시 취소 전송 계층은 별도 작업입니다.

## 기존 구현 영역

| 영역 | 소스 구현 | 사용자 실환경 확인 |
|---|---|---|
| Native WLAN | SSID·BSSID·RSSI·채널·PHY·링크 속도 | 드라이버·권한·WLAN 서비스 |
| 내부 DIRECT 다운로드 | 사용자 실행 HEAD/GET·수신량·TTFB·처리량 | 승인 내부 서버·정책 |
| 외부 프록시 다운로드 | 프록시 경유 HEAD/GET·407 통합 인증 | PAC/WPAD·Negotiate/NTLM·TLS 검사 |
| 대상 정책 | 로컬/Portable 설정·ProgramData 관리자 정책 | 파일 ACL·GPO·승인 대상 |
| 반복 측정 | 예열·중앙값·편차·신뢰도·예산·전용 취소·UI 수명 | 실제 내부·외부 반복 |
| 브라우저 관찰 | 물리 Wi-Fi 카운터 고정·연속성·구조화 종료 | 로밍·NIC 제거·절전 |
| 어댑터 진단 | 물리 Wi-Fi·유선·VPN·터널·가상 NIC | 회사 VPN·보안 에이전트 |
| 경로 비교 | 내부 대상·프록시 후보의 정확 인터페이스 비교 | IPv4/IPv6·유선/Wi-Fi/VPN |
| Windows 프록시 가져오기 | 로컬 설정·명시 동의한 PAC/WPAD 판정 | 회사별 자동 프록시 정책 |
| 전용 경로 보고서 | 안전 모델·취소 가능한 저장·복구·지연 종료 | 공유 전 개인정보 확인 |
| 일반·반복·관찰·어댑터 보고서 | JSON·CSV·HTML·SHA-256 | 실제 마스킹 |
| self-contained 배포 | win-x64 Portable ZIP·single-file EXE | 실행·SmartScreen·EDR |

## 자동 검증

Release 검증은 기존 9개 프로젝트에 새 통합 보고서 프로젝트를 추가합니다.

1. Core SelfTest
2. WindowsSmoke
3. ProxyAuthSmoke
4. MeasurementSmoke
5. ObservationSmoke
6. ReportSmoke
7. UiOperationSmoke: 실제 WPF Dispatcher 10개 그룹
8. ProxyBoundarySmoke: 원문·출처·취소·privacy 12개 그룹
9. RepeatedUiSmoke: 반복 측정 UI 11개 그룹
10. UnifiedReportSmoke: 통합 보고서 9개·WPF 저장 8개 그룹

새 UI 테스트는 창을 Show하지 않고 실제 MainWindow·Dispatcher·버튼·종료 이벤트에 합성 capture/writer를 주입합니다. 기존 HTTP 시험은 루프백 서버·프록시를 사용합니다. 회사 환경 검증과 구분합니다.

## 배포와 소스 상태

PR 병합은 기존 Release EXE·ZIP을 변경하지 않습니다. 이번 소스 작업에서 새 공개 Release는 게시하지 않습니다. 공개 버전·자산·해시는 별도 Release 조회와 게시 후 검증이 필요합니다.

과거 `v0.1.0-alpha.5` / `bbdad0b7ed8ffca839a83672ac1e4537bb1e29b7`은 2026-09-04 기록이며 최신 버전 선언이 아닙니다.

배포 계약: Portable ZIP, single-file EXE, SHA256SUMS.txt, THIRD_PARTY_NOTICES.md. 새 공개 배포는 다시 내려받은 파일의 크기·해시·필수 문서·BUILD_INFO·태그를 검증해야 합니다. Authenticode 인증서는 아직 제공되지 않았습니다.

## 남은 개발

### P0 — #16 전체 UI 수명

- 일반 경로 확인·어댑터/환경 수집·다른 전용 보고서·자동 갱신의 공통 실행/종료 처리
- busy Boolean·탭 잠금·Closing handler 중복 정리
- Loaded 이후 탭 생성·취소·창 종료·어댑터 변경 경쟁 회귀
- 단일 다운로드 중지 callback 실패의 상세 표시 등 공통 취소 UI 보강
- 통합 및 다른 legacy writer의 파일세트 단위 취소/복구 필요성 점검

### P1 — #18 원문 입력 감사

- 별도 ProxyEndpointParser 및 내부/외부 URL 입력 계층의 길이·제어 문자·정규화 순서 검증
- 지정 경로에서 완료한 #125 검증을 전체 입력 계층 완료로 오해하지 않음

### P1 — #19 배포와 #20 전송

- 새 운영 문서 Portable ZIP 포함과 패키지 계약 갱신
- 별도 공개 Release 빌드·게시·게시 후 바이트/태그 검증
- WINHTTP_FLAG_ASYNC 기반 취소와 완료/407 재인증 callback·handle 수명 검증
- 절전·재연결·USB NIC 제거·VPN 전환·장시간 측정 경쟁
- 필요 시 개인정보 없는 route/interface metric 확장

### 별도 출시 조건

- 인증서 제공 후 Authenticode 서명·타임스탬프·CI 서명 확인
- 사용자 회사 Windows 11·Aruba·PAC/WPAD·407·TLS 검사·VPN·GPO·EDR 검증 및 피드백 반영
- 개발 완료율과 별도로 실환경/서명 완료 여부 보고
