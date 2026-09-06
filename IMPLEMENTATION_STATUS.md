# 구현 상태

소스 상태 갱신일: 2026-09-07

소스 구현, 정확한 commit의 CI 성공, main 병합, 공개 EXE·ZIP 배포, 사용자 회사 환경 검증은 서로 다른 완료 기준입니다. 종료 보고에는 완료 범위·검증 상태·남은 작업·진행률 산정 기준을 함께 기록합니다.

## 이번 변경: 반복 측정 실행 수명

기반 main: `9000c47d6601e32d1ed8353bad74029f8197239d`.

| 영역 | 구현 | 검증 |
|---|---|---|
| 공통 측정 runner | 기존 단일 다운로드와 반복 측정이 같은 RunCoordinatedMeasurementAsync 사용 | 기존 UiOperationSmoke 재실행 |
| 반복 작업 종류 | RepeatedMeasurement를 같은 MainWindow coordinator에 등록 | 실제 MainWindow 상태·다른 작업 중복 차단 |
| 같은 탭 중지 | 반복 측정 중지 버튼에서 활성 lease 취소, 실제 반환까지 busy 유지 | Button.Click 2회·callback 1회·다음 대상 미시작 |
| 진행 알림 | 실행 lease와 대상별 identity, 캡처한 대상 번호 사용 | 대상 간·실행 간·최종 결과 이후 늦은 알림 |
| 부분 결과 | 취소 또는 다음 대상 오류 시 확보된 결과와 미시작 대상 수 표시 | 지연 반환·앞선 결과 보존·예외 원문 비노출 |
| UI 수명 | 공통 설정 복원 후 lease 해제, 기존 deferred close 재사용, 구독 해제 | 실제 WPF Closing/Closed·중복 등록·닫힌 창 |
| 실행 옵션 | 대상 배열·HEAD 옵션 스냅샷, 진입점 예산 재검사 | 원본 배열 변경·잘못된 계획·총량·오버플로 |

`RepeatedUiSmoke` 11개 그룹을 Release 검증의 아홉 번째 테스트 프로젝트로 추가했습니다. 이는 테스트 작성/연결 상태이며 성공 여부는 최종 PR head의 실제 CI 결과에서 확인합니다. 공개 배포 또는 회사 환경 검증 완료로 대체하지 않습니다. 상세: `docs/REPEATED_MEASUREMENT_LIFECYCLE.md`.

## 진행률

`docs/DEVELOPMENT_PROGRESS.md`의 고정 완료 기준 20개를 사용합니다. 구현·자동 검증·main 반영을 모두 충족한 항목만 각 5%로 계산합니다. 코드 줄 수나 소요 시간의 비율이 아닙니다.

- 기반 main: 14/20 = 70%
- 이번 반복 측정 항목이 최종 CI 성공 후 main에 반영되면: 15/20 = 75%
- PR 생성 또는 CI 미완료 상태에서는 추가 5%를 완료로 계산하지 않음
- 사용자 회사 환경 검증과 제공이 필요한 코드 서명 인증서는 별도 출시 조건으로 계속 표시

## 확인된 기반

- #119: Core ApplicationOperationCoordinator, 단일 lease, 취소·shutdown·idle 대기
- #121: 내부 DIRECT–프록시 비교와 Windows 프록시 가져오기에서 같은 Core lease
- #122: 전용 경로 보고서 저장의 lease·취소 latch·파일 복구·지연 종료
- #123: 내부·외부 다운로드, 기존 프록시 판정, 브라우저 관찰과 WPF UI adapter
- #125: 프록시 원문 4096 UTF-16 길이·제어 문자, 출처 원문 검증, WPF 원문 전달, 취소 후 늦은 분석 결과 차단

#123: head `4ae15cfba41e95b86a7b612d24a1d0b9691c05ba`, Windows CI `33980714941`, Release Package CI `33980714884`, Guide CI `33980714883`, merge `d0ea7d1f98f8678632f10afd48d9917921da5f72`.

#125: head `b91845e9b27768548e16b77bde43916ee79ec197`, Windows CI `33982420812`, Release Package CI `33982420811`, Guide CI `33982420813`, merge `9000c47d6601e32d1ed8353bad74029f8197239d`. ProxyBoundarySmoke 12개 그룹·제어 문자 260개 입력과 기존 회귀 성공.

기존 경로·프록시 가져오기 또는 보고서 코드를 다시 만들지 않습니다. 이번에 새 coordinator·timer·ModuleInitializer를 도입하지 않습니다. 기존 동기 WinHTTP는 실제 반환까지 기다리는 협력적 취소이며 즉시 취소 전송 계층은 별도 작업입니다.

## 기존 구현 영역

| 영역 | 소스 구현 | 사용자 실환경 확인 |
|---|---|---|
| Native WLAN | SSID·BSSID·RSSI·채널·PHY·링크 속도 | 드라이버·권한·WLAN 서비스 |
| 내부 DIRECT 다운로드 | 사용자 실행 HEAD/GET·수신량·TTFB·처리량 | 승인 내부 서버·정책 |
| 외부 프록시 다운로드 | 프록시 경유 HEAD/GET·407 통합 인증 | PAC/WPAD·Negotiate/NTLM·TLS 검사 |
| 대상 정책 | 로컬/Portable 설정·ProgramData 관리자 정책 | 파일 ACL·GPO·승인 대상 |
| 반복 측정 | 예열·중앙값·편차·신뢰도·수신량 예산 | 실제 내부·외부 반복 |
| 브라우저 관찰 | 물리 Wi-Fi 카운터 고정·연속성·구조화 종료 | 로밍·NIC 제거·절전 |
| 어댑터 진단 | 물리 Wi-Fi·유선·VPN·터널·가상 NIC | 회사 VPN·보안 에이전트 |
| 경로 비교 | 내부 대상·프록시 후보의 정확 인터페이스 비교 | IPv4/IPv6·유선/Wi-Fi/VPN |
| Windows 프록시 가져오기 | 로컬 설정·명시 동의한 PAC/WPAD 판정 | 회사별 자동 프록시 정책 |
| 전용 경로 보고서 | 안전 모델·취소 가능한 저장·복구·지연 종료 | 공유 전 개인정보 확인 |
| 일반·반복·관찰·어댑터 보고서 | JSON·CSV·HTML·SHA-256 | 실제 마스킹 |
| self-contained 배포 | win-x64 Portable ZIP·single-file EXE | 실행·SmartScreen·EDR |

Windows 프록시 가져오기와 전용 경로 보고서는 이미 구현됐습니다. 기존 통합 진단 보고서에 경로 비교를 포함하는 작업은 별도 미완료 항목입니다.

## 자동 검증

1. Core SelfTest
2. WindowsSmoke
3. ProxyAuthSmoke
4. MeasurementSmoke
5. ObservationSmoke
6. ReportSmoke
7. UiOperationSmoke: 실제 WPF Dispatcher 10개 그룹
8. ProxyBoundarySmoke: 원문·출처·취소·privacy 12개 그룹
9. RepeatedUiSmoke: 반복 측정 UI 11개 그룹

새 반복 테스트는 창을 Show하지 않고 실제 MainWindow·WPF Dispatcher·버튼 이벤트에 합성 runner를 주입합니다. 새로운 실제 WLAN·DNS·HTTP/PAC 시험이 아닙니다. 기존 HTTP 시험은 루프백 합성 서버·프록시를 사용합니다. 실제 회사 테스트와 구분합니다.

## 배포와 소스 상태

PR 병합은 기존 Release EXE·ZIP을 변경하지 않습니다. 이번 작업은 새 공개 Release를 게시하지 않습니다. 공개 버전·자산·해시는 별도 Release 조회와 게시 후 검증이 필요합니다.

과거 `v0.1.0-alpha.5` / `bbdad0b7ed8ffca839a83672ac1e4537bb1e29b7`은 2026-09-04 기록이며 현재 최신 버전 선언이 아닙니다.

배포 계약: Portable ZIP, single-file EXE, SHA256SUMS.txt, THIRD_PARTY_NOTICES.md. 새 배포 시 다시 내려받은 파일의 크기·해시·필수 문서·BUILD_INFO·태그를 검증해야 합니다. CI 패키지 검사와 공개 게시 검증은 다릅니다. Authenticode 인증서는 아직 제공되지 않았습니다.

## 남은 개발

### P0 — 전역 UI 수명 이행

- 일반 경로 확인·어댑터/환경 수집·기타 보고서 저장·자동 갱신의 공통 실행/종료 처리
- busy Boolean·탭 잠금·Closing handler 중복 정리
- Loaded 이후 탭 생성·취소·창 종료·어댑터 변경 경쟁 회귀
- #124는 #123과 중복 적용하지 않고 이번 반복 종류·중지·safe exception 변경 반영 후 superseded 여부 판단

### P1 — 통합 보고서 및 입력

- 내부 DIRECT–프록시 결과를 LocalDiagnosticReport의 optional 섹션으로 연결
- Finding 중복·누락 및 일반 무패턴 판정 충돌 방지
- 별도 ProxyEndpointParser 및 내부/외부 URL 입력 계층 원문 경계 감사

### P1 — 배포 및 비동기 전송

- 새 운영 문서 Portable ZIP 포함과 패키지 계약 갱신
- 별도 공개 Release 빌드·게시·게시 후 바이트 검증
- WINHTTP_FLAG_ASYNC 기반 취소와 완료/407 재인증의 callback·handle 수명 검증
- 절전·재연결·USB NIC 제거·VPN 전환·장시간 측정 경쟁
- 필요 시 개인정보 없는 route/interface metric 확장

### 별도 출시 조건

- 인증서 제공 후 Authenticode 서명·타임스탬프·CI 서명 검증
- 사용자 회사 Windows 11·Aruba·PAC/WPAD·407·TLS 검사·VPN·GPO·EDR 검증 및 피드백 반영
- 개발 완료율과 별도로 실환경/서명 완료 여부 보고
