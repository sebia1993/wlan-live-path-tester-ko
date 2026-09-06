# 구현 상태

소스 상태 갱신일: 2026-09-07

코드 작성, 정확한 commit의 CI 성공, main 병합, 공개 EXE·ZIP 게시, 사용자 회사 환경 검증은 서로 다른 완료 기준입니다. 종료 보고에는 실제 완료 범위·검증 결과·남은 작업·고정 기준 진행률을 함께 기록합니다.

## 이번 변경: #129 중앙 경로 실행·종료 수명

기반 main: `0d1705d0c646bf50915c5fcab55a3527a6c0fb5b` (#128).

| 영역 | 구현 | 검증 대상 |
|---|---|---|
| 중앙 실행 | 기존 coordinator/UI session 하나, Core-only route 획득 함수 제거 | 모든 전역 kind와 경로/import/report/기본 조회 배제 |
| 중앙 종료 | Closing 구독 한 곳, snapshot busy 확인, 실제 종료 뒤 Dispatcher에 Close 게시 | 재진입 Close·중복 Close·최종 Close 거절 뒤 재실행 |
| 경로 비교 | 기존 coordinator/renderer를 같은 진단 수명으로 실행 | raw proxy 전달·대상 스냅샷·취소/절전 후 늦은 값 제외 |
| 프록시 가져오기 | 기존 importer·동의·출처·monotonic TTL 유지 | 동의 false 기본값·selection identity·5분 만료·URL revision |
| 경로 전용 보고서 | 같은 UI lease + 기존 ReportSaveSession/writer | TryStart 전후 취소 latch·writer/callback/CTS 완료 대기 |
| 저장 결과 | 이미 게시된 성공 보존, 오류 검토 후 재시도/재종료 | commit 뒤 취소·callback 실패·이전 경로/오류 격리 |
| 기본 조회 | WLAN·로컬 프록시 설정·WLAN-NIC 대응을 같은 진단 runner로 연결 | 실제 handler/button·잠금/복원·오류·종료·Dispatcher |
| 중지 표시 | 단일 측정 callback 실패를 상태에 반영 | 실패 메시지·원문 비노출·실제 완료 전 busy 유지 |
| 자동 갱신 | 기존 scheduler가 중앙 snapshot/종료/절전만 참조 | 기존 25개 OperationLifecycleSmoke 회귀 |
| 새 회귀 | RouteUiLifetimeSmoke 28개 그룹을 12번째 Release 검증에 추가 | 경로 11·보고서 9·기본 7·보고서 오류 격리 1 |

상세: `docs/CENTRAL_ROUTE_UI_LIFETIME.md`. 최종 통과와 main 반영은 PR #129의 정확한 head·CI 로그·실제 merge SHA로 확인합니다. 이 문서의 구현 목록만으로 성공이나 병합을 주장하지 않습니다.

### 유지한 경계

- 새 Core coordinator, native 프록시 구현, 자동 분석 서비스 또는 외부 전송을 만들지 않음
- Windows 프록시 가져오기 실패를 수동 설정/DIRECT로 자동 대체하지 않음
- 동기 Windows/WinHTTP 호출을 취소 토큰이나 시간 제한만으로 버리지 않고 실제 반환까지 기다림
- 기존 전용 경로 writer의 파일세트 예약·rollback·복구 예외·SHA-256 게시와 ReportSaveSession callback join 유지
- 통합/일부 전용 legacy writer는 파일별 저장 방식을 유지. 안전한 중간 취소가 없으면 실제 완료를 기다리며 부분 파일 가능성 안내
- 기본 로컬 프록시 설정 읽기가 PAC/WPAD 조회를 새로 수행하지 않음
- 나머지 parser/URL/입력 정규화 점검은 별도 #18 범위

## 진행률

`docs/DEVELOPMENT_PROGRESS.md`의 고정 20개 기준과 항목당 5%를 유지합니다.

- #126 이전 최초 기준: 14/20 = 70%
- #126 main 반영: 15/20 = 75%
- #127 main 반영: 16/20 = 80%
- #128은 #16 부분 이행이므로 80% 유지
- **이번 #129의 #16 운영 연결·최종 자동 검증·main 반영이 모두 확인되면 17/20 = 85%**
- CI 또는 병합 미완료이면 80% 유지; 시간/코드량/실환경 품질 비율이 아님

#16 완료 뒤 남은 기준은 #18 입력, #19 문서/공개 배포, #20 비동기 전송입니다. 사용자 회사 환경 검증과 코드 서명은 별도 출시 조건입니다.

## 확인된 기반과 검증 기록

- #119: Core ApplicationOperationCoordinator와 단일 lease·취소·shutdown·idle 대기
- #121: 내부 DIRECT–프록시 비교와 Windows 프록시 가져오기의 Core 실행권
- #122: 경로 전용 보고서의 취소 latch·파일 복구·지연 종료
- #123: 다운로드·프록시 판정·브라우저 관찰의 WPF UI adapter
- #125: 지정 프록시 파서·출처 선택기의 원문 경계와 취소 후 늦은 결과 차단
- #126: 반복 측정 전용 중지·진행 알림 격리·부분 결과 보존
- #127: optional 통합 경로 보고서 schema 1.2·Finding·기존 생성자 호환·저장 수명
- #128: 세 진단·다섯 전용 보고서·자동 갱신의 UI 수명

#126: head `fc3ded659f60f3cb76889ba256c3f8ddda63ec26`, Windows `34043887817`, Package `34043887800`, Guide `34043887839` 성공. merge `6a7903f1f17aeeac4826fbb93a6c0eb327f4a17d`. 구형 중복 #124는 병합 없이 종료.

#127: head `b38639b75436bee5a95a334f23e5feb1e0448df1`, Windows `34060775928`, Local Report `34060775942`, Guide `34060775888`, Package `34060775938` 성공. merge `00bd8c71511514969aca7d23d0147c41beb04f3f`.

#128: head `1da20b97b8c03d8b92deae2c956d8ba40c0ab609`, Windows `34063845101`, Guide `34063845159`, Package `34063845094` 성공. merge `0d1705d0c646bf50915c5fcab55a3527a6c0fb5b`. 신규 25개 그룹·기존 회귀·패키지 검사 성공.

위 소스 PR들은 새 공개 Release를 게시하지 않았습니다.

## 기존 구현 영역

| 영역 | 소스 구현 | 사용자 실환경 확인 |
|---|---|---|
| Native WLAN | SSID·BSSID·RSSI·채널·PHY·링크 속도 | 드라이버·권한·WLAN 서비스 |
| 내부 DIRECT 다운로드 | 사용자 실행 HEAD/GET·수신량·TTFB·처리량 | 승인 내부 서버·정책 |
| 외부 프록시 다운로드 | 프록시 경유 HEAD/GET·407 통합 인증 | PAC/WPAD·Negotiate/NTLM·TLS 검사 |
| 대상 정책 | 로컬/Portable 설정·ProgramData 관리자 정책 | 파일 ACL·GPO·승인 대상 |
| 반복 측정 | 예열·중앙값·편차·신뢰도·예산·중지·UI 수명 | 실제 내부·외부 반복 |
| 브라우저 관찰 | 물리 Wi-Fi 카운터 고정·연속성·구조화 종료 | 로밍·NIC 제거·절전 |
| 어댑터/환경 | 물리 Wi-Fi·유선·VPN·가상 NIC·경로 혼재 | 회사 VPN·보안 에이전트 |
| 경로 비교 | 내부 대상·프록시 후보의 정확 인터페이스 비교 | IPv4/IPv6·유선/Wi-Fi/VPN |
| Windows 프록시 가져오기 | 로컬 설정·명시 동의한 PAC/WPAD 판정 | 회사별 자동 프록시 정책 |
| 보고서 | JSON·CSV·오프라인 HTML·SHA-256·통합 경로 섹션 | 실제 마스킹·공유 전 검토 |
| self-contained 배포 | win-x64 Portable ZIP·single-file EXE | 실행·SmartScreen·EDR |

## 자동 검증

Release 검증의 12개 프로젝트:

1. Core SelfTest
2. WindowsSmoke
3. ProxyAuthSmoke
4. MeasurementSmoke
5. ObservationSmoke
6. ReportSmoke
7. UiOperationSmoke
8. ProxyBoundarySmoke
9. RepeatedUiSmoke
10. UnifiedReportSmoke
11. OperationLifecycleSmoke
12. RouteUiLifetimeSmoke

새 프로젝트는 창을 Show하지 않고 실제 WPF 객체·버튼·Closing 이벤트에 합성 collector/import result를 주입합니다. 실제 경로 report writer는 고유 임시 로컬 폴더에서 검증합니다. 회사 WLAN·DNS·HTTP·PAC/WPAD 또는 OS NetworkChange 구독은 새 테스트에서 수행하지 않습니다. 기존 HTTP 시험은 루프백 서버/프록시를 사용합니다.

## 배포 상태

PR 병합은 기존 공개 EXE/ZIP을 바꾸지 않습니다. 이번 작업에서는 새 공개 Release를 게시하지 않습니다. 공개 버전·자산·해시는 별도 조회와 게시 후 검증이 필요합니다.

과거 `v0.1.0-alpha.5` / `bbdad0b7ed8ffca839a83672ac1e4537bb1e29b7`은 2026-09-04 기록이며 최신 버전 선언이 아닙니다. 배포 계약은 Portable ZIP, single-file EXE, SHA256SUMS.txt, THIRD_PARTY_NOTICES.md입니다. 새 게시 후 다시 받은 바이트·해시·문서·태그·BUILD_INFO를 검증합니다. Authenticode 인증서는 아직 제공되지 않았습니다.

## 남은 개발

### #18 — 원문 입력 경계

별도 ProxyEndpointParser, 내부/외부 URL 입력, 구성 입력의 길이·제어 문자·정규화 순서를 점검합니다. #125의 지정 경로를 전체 입력 계층 검증 완료로 확대하지 않습니다.

### #19 — 최종 문서와 공개 배포

새 운영 문서의 Portable ZIP 포함, 패키지 계약 갱신, 새 공개 Release 빌드·게시, 게시 후 파일/해시/태그/BUILD_INFO 일치 확인이 필요합니다. CI용 패키지 성공을 공개 배포 완료로 대체하지 않습니다.

### #20 — 안전한 비동기 WinHTTP

WINHTTP_FLAG_ASYNC 기반 취소, 완료/407 재인증 callback과 handle 수명, 즉시 취소와 실제 cleanup 완료 구분이 필요합니다. 현재는 협력적 취소와 실제 동기 반환 대기 방식입니다.

### 남아 있는 운영 제약·외부 검증

일부 legacy 보고서의 파일세트 단위 rollback/중간 취소는 아직 지원하지 않습니다. 실패 후 일부 파일 가능성은 계속 표시합니다. 사용자 회사 Windows 11·Aruba·PAC/WPAD·407·TLS 검사·VPN·GPO·EDR·절전/장시간 검증과 피드백 반영은 별도입니다. 인증서 제공 후 서명·타임스탬프·CI 확인도 별도 출시 조건으로 보고합니다.
