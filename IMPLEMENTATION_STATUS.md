# 구현 상태

소스 상태 갱신일: 2026-09-07

소스 구현, 정확한 commit의 CI 성공, main 병합, 공개 EXE·ZIP 배포, 사용자 회사 환경 검증은 서로 다른 완료 기준입니다. 종료 보고에는 완료 범위·검증 상태·남은 작업·고정 기준 진행률을 함께 기록합니다.

## 이번 변경: 진단 조회·자동 갱신·다섯 전용 보고서

기반 main: `00bd8c71511514969aca7d23d0147c41beb04f3f` (#127).

| 영역 | 이번 구현 | 검증 대상 |
|---|---|---|
| 공통 진단 실행 | RouteEvidence·NetworkAdapterDiagnostics·NetworkEnvironmentCapture가 같은 UI/Core lease 사용 | 모든 전역 종류와 중복 실행 거부·reader 0회 |
| 라우팅 확인 | 대상/목적 캡처·관련 입력 잠금·동일 lease 취소 | 실제 취소 버튼·취소 한 번·실제 반환 대기 |
| 로컬 수집 | Native WLAN/identity/NIC 조회를 작업 스레드에서 수행, UI는 Dispatcher에서 적용 | 세 진단의 상태·버튼·탭 복원 |
| 늦은 결과 | 취소·협력적 제한 시간·절전 이후 늦은 반환값 미적용 | token을 무시하는 합성 reader와 실제 완료 대기 |
| 자동 갱신 | 750ms debounce, 요청 합치기·전역 idle 재개·in-flight 후속 1회 | 1,000개 이벤트·stale idle·모든 전역 종류 |
| 전원/종료 | 절전 중 자동 갱신 정지·활성 진단 취소, 복귀 요청 보존, 닫힌 창 알림 무시 | 전환 전 결과 폐기·후속 새 조회·구독 정리 |
| 다섯 전용 보고서 | 공통 AuxiliaryReportSection과 DiagnosticReportSave 실행 | 라우팅·반복·관찰·어댑터·환경 각 유형의 저장/열기/상태 |
| 저장 실패/창 종료 | 이전 성공 경로 유지, 실제 쓰기 종료 후 반환, 종료 중 실패 시 창 유지 | 다섯 유형의 성공·실패·재시도·deferred close |
| 회귀 | OperationLifecycleSmoke 25개 그룹을 11번째 Release 검증 프로젝트로 추가 | 기존 10개 프로젝트와 source 계약 유지 |

상세는 `docs/DIAGNOSTIC_REFRESH_LIFECYCLE.md`를 참조합니다. 이 문서의 구현·테스트 목록 자체는 실행 성공의 증거가 아닙니다. 최종 head의 실제 Windows/Release Package/Guide CI와 로그, merge SHA를 PR에 별도로 기록하고 검증 전에는 병합하지 않습니다.

### 이번 변경의 한계

- 기본 30초 제한은 협력적 취소입니다. 무시하는 동기 Windows 호출을 강제 종료하거나 미완료 상태에서 실행권을 반환하지 않습니다.
- 다섯 전용 보고서의 UI/실행 수명을 통일했지만 기존 writer들의 파일 묶음 rollback을 새로 구현한 것은 아닙니다. 안전한 중간 취소가 없는 저장은 실제 쓰기 완료를 기다리고 실패 시 부분 파일 확인을 안내합니다.
- 기존 수동 내부 DIRECT–프록시 비교, Windows 프록시 가져오기, 전용 경로 비교 보고서는 같은 Core coordinator를 사용하되 일부 독립 Closing·peer-tab 수명 처리가 남습니다. 이를 모두 공통 UI adapter로 중앙화한 것은 아닙니다.
- WLAN identity 대응 및 짧은 기본 WLAN/프록시 조회 등 나머지 경로, 일부 공통 취소 실패 표시를 더 점검해야 합니다.
- 기존 Core 보고서 모델·통계·다운로드·인증·경로 분석기를 교체하지 않습니다. 새 외부 통신을 추가하지 않습니다.

## 진행률

`docs/DEVELOPMENT_PROGRESS.md`의 고정 20개 기준, 항목당 5%를 유지합니다. 구현·관련 자동 검증·main 반영을 모두 충족해야 계산합니다.

- 최초 기준: 14/20 = 70%
- #126 반복 측정 main 반영: 15/20 = 75%
- #127 통합 경로 보고서 main 반영: **16/20 = 80%**
- 이번 변경은 #16의 일부 이행이므로 완료 후에도 **80% 유지**. 부분 점수나 분모 변경 없음.
- 아직 미완료 기준: #16, #18, #19, #20
- 코드 줄 수·예상 소요 시간·실환경 품질의 백분율이 아님

## 확인된 기반

- #119: Core ApplicationOperationCoordinator와 단일 lease·취소·shutdown·idle 대기
- #121: 내부 DIRECT–프록시 비교와 Windows 프록시 가져오기에서 같은 Core lease
- #122: 전용 경로 비교 보고서의 lease·취소 latch·파일 복구·지연 종료
- #123: 내부·외부 다운로드, 기존 프록시 판정, 브라우저 관찰의 같은 WPF UI adapter
- #125: 지정 프록시 파서·선택기의 원문 길이/제어 문자, WPF 원문 전달, 취소 후 늦은 결과 차단
- #126: 반복 전용 종류·같은 탭 중지·대상/실행별 진행 알림·부분 결과 보존·창 종료
- #127: 통합 보고서 optional 경로 snapshot·schema 1.2·Finding 통합·기존 생성자 호환·저장 수명

#126 검증 head `fc3ded659f60f3cb76889ba256c3f8ddda63ec26`: Windows CI `34043887817`, Release Package CI `34043887800`, Guide CI `34043887839` 성공. merge `6a7903f1f17aeeac4826fbb93a6c0eb327f4a17d`. RepeatedUiSmoke 11개 그룹과 기존 전체 회귀 통과. 중복 구조 #124는 병합하지 않고 대체 완료로 종료했습니다.

#127 검증 head `b38639b75436bee5a95a334f23e5feb1e0448df1`: Windows CI `34060775928`, Local Report CI `34060775942`, Guide CI `34060775888`, Release Package CI `34060775938` 성공. merge `00bd8c71511514969aca7d23d0147c41beb04f3f`. UnifiedReportSmoke 17개 그룹, 기존 회귀와 패키지 검사 통과. 공개 Release는 게시하지 않았습니다.

## 기존 구현 영역

| 영역 | 소스 구현 | 사용자 실환경 확인 |
|---|---|---|
| Native WLAN | SSID·BSSID·RSSI·채널·PHY·링크 속도 | 드라이버·권한·WLAN 서비스 |
| 내부 DIRECT 다운로드 | 사용자 실행 HEAD/GET·수신량·TTFB·처리량 | 승인 내부 서버·정책 |
| 외부 프록시 다운로드 | 프록시 경유 HEAD/GET·407 통합 인증 | PAC/WPAD·Negotiate/NTLM·TLS 검사 |
| 대상 정책 | 로컬/Portable 설정·ProgramData 관리자 정책 | 파일 ACL·GPO·승인 대상 |
| 반복 측정 | 예열·중앙값·편차·신뢰도·예산·전용 취소·UI 수명 | 실제 내부·외부 반복 |
| 브라우저 관찰 | 물리 Wi-Fi 카운터 고정·연속성·구조화 종료 | 로밍·NIC 제거·절전 |
| 어댑터/환경 진단 | 물리 Wi-Fi·유선·VPN·터널·가상 NIC·기본 경로 혼재 | 회사 VPN·보안 에이전트 |
| 경로 비교 | 내부 대상·프록시 후보의 정확 인터페이스 비교 | IPv4/IPv6·유선/Wi-Fi/VPN |
| Windows 프록시 가져오기 | 로컬 설정·명시 동의한 PAC/WPAD 판정 | 회사별 자동 프록시 정책 |
| 전용 경로 비교 보고서 | 안전 모델·취소 가능한 저장·복구·지연 종료 | 공유 전 개인정보 확인 |
| 일반·반복·관찰·라우팅·어댑터 보고서 | JSON·CSV·HTML·SHA-256·통합 optional 경로 결과 | 실제 마스킹 |
| self-contained 배포 | win-x64 Portable ZIP·single-file EXE | 실행·SmartScreen·EDR |

## 자동 검증

Release 검증은 기존 10개 프로젝트에 새 수명 검사 프로젝트를 추가합니다.

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
11. OperationLifecycleSmoke: 진단·자동 갱신·다섯 전용 보고서 25개 그룹

새 테스트는 창을 Show하지 않고 실제 MainWindow·Dispatcher·버튼·종료 이벤트에 합성 collector/writer를 주입합니다. 새 프로젝트는 OS NetworkChange 구독도 시작하지 않습니다. 기존 HTTP 시험은 루프백 서버·프록시를 사용합니다. 회사 환경 검증과 구분합니다.

## 배포와 소스 상태

PR 병합은 기존 Release EXE·ZIP을 변경하지 않습니다. 이번 소스 작업에서 새 공개 Release는 게시하지 않습니다. 공개 버전·자산·해시는 별도 Release 조회와 게시 후 검증이 필요합니다.

과거 `v0.1.0-alpha.5` / `bbdad0b7ed8ffca839a83672ac1e4537bb1e29b7`은 2026-09-04 기록이며 최신 버전 선언이 아닙니다.

배포 계약: Portable ZIP, single-file EXE, SHA256SUMS.txt, THIRD_PARTY_NOTICES.md. 새 공개 배포는 다시 내려받은 파일의 크기·해시·필수 문서·BUILD_INFO·태그를 검증해야 합니다. Authenticode 인증서는 아직 제공되지 않았습니다.

## 남은 개발

### P0 — #16 전체 UI 수명 마무리

- Core-only 경로 비교·Windows 프록시 가져오기·전용 경로 비교 보고서의 공통 UI adapter 이행
- 해당 기능들의 busy Boolean·탭 잠금·Closing handler 중복 정리와 통합 회귀
- WLAN identity 대응 및 나머지 기본 조회의 실행 경계 점검
- 단일 다운로드 중지 callback 실패의 상세 표시 등 공통 취소 UI 보강
- legacy writer 파일세트 취소/복구 필요성 점검. 실제 파일 쓰기를 버리고 완료 처리하지 않음

이번에 연결한 라우팅 확인·어댑터/환경 수집·다섯 전용 보고서·자동 갱신을 다시 미구현 기능으로 분류하지 않습니다. 남은 것은 위 잔여 경로와 전체 일관성 검증입니다.

### P1 — #18 원문 입력 감사

- 별도 ProxyEndpointParser 및 내부/외부 URL 입력 계층의 길이·제어 문자·정규화 순서 검증
- 지정 경로에서 완료한 #125 검증을 전체 입력 계층 완료로 오해하지 않음

### P1 — #19 배포와 #20 전송

- 새 운영 문서 Portable ZIP 포함과 패키지 계약 갱신
- 별도 공개 Release 빌드·게시·게시 후 바이트/태그 검증
- WINHTTP_FLAG_ASYNC 기반 취소와 완료/407 재인증 callback·handle 수명 검증
- 실제 절전·재연결·USB NIC 제거·VPN 전환·장시간 측정 피드백 반영

### 별도 출시 조건

- 인증서 제공 후 Authenticode 서명·타임스탬프·CI 서명 확인
- 사용자 회사 Windows 11·Aruba·PAC/WPAD·407·TLS 검사·VPN·GPO·EDR 검증 및 피드백 반영
- 개발 완료율과 별도로 실환경/서명 완료 여부 보고
