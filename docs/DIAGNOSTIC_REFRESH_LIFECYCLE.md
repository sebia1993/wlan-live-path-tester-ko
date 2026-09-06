# 진단 조회·자동 갱신·전용 보고서 실행 수명

## 적용 범위

기반 main은 `00bd8c71511514969aca7d23d0147c41beb04f3f`입니다. 기존 MainWindow의 단일 ApplicationOperationCoordinator와 ApplicationOperationUiSession을 재사용합니다.

이번 연결 대상:

- 라우팅 근거 확인 → RouteEvidence
- 어댑터 목록 수동/자동 갱신 → NetworkAdapterDiagnostics
- 로컬 인터페이스 환경 확인 → NetworkEnvironmentCapture
- 라우팅·반복 측정·브라우저 관찰·어댑터·인터페이스 환경 전용 보고서 → DiagnosticReportSave

기존 단일/반복 다운로드, 브라우저 관찰, 프록시 불러오기, 내부 DIRECT–프록시 비교, 통합 보고서와 전용 경로 비교 보고서 기능은 유지합니다. 별도의 두 번째 전역 잠금을 만들지 않습니다.

## 조회 수명

`RunLocalDiagnosticAsync`는 다음 순서로 실행합니다.

```text
UI Dispatcher·종류·인자 검증
  → 동일 UI/Core lease 획득
  → 관련 입력/시작 버튼 잠금
  → 기존 Windows reader 실행
  → 실제 반환 뒤 취소·제한 시간·절전 상태 재확인
  → 유효한 최신 결과만 UI에 적용
  → 화면 정리
  → lease 반환
```

다른 작업이 실행 중이면 reader를 호출하지 않습니다. 대상과 해석 목적은 실행 전에 캡처합니다. 라우팅 확인의 입력·URL 가져오기 버튼은 실행 중 변경할 수 없으며 취소 버튼은 같은 lease의 기존 취소 함수만 요청합니다.

Native WLAN·identity·인터페이스 목록 등 동기 로컬 조회는 Task.Run에서 수행하고 UI에 적용할 문자열·상태만 돌려줍니다. 작업 스레드가 WPF 컨트롤을 직접 변경하지 않습니다. 예외는 유형만 표시하며 원문 URL·사용자 경로·예외 메시지를 오류 문장에 반사하지 않습니다.

### 취소와 시간 제한의 정확한 의미

기본 협력적 제한 시간은 30초입니다. 토큰을 지원하는 단계는 이 토큰으로 중지하고, 토큰을 무시하는 동기 Windows 호출은 실제로 반환될 때까지 실행권을 유지합니다. 따라서 **30초 안에 네이티브 호출을 강제로 종료한다는 보장은 아닙니다.** 미완료 native 호출을 두고 Task.WhenAny/WaitAsync로 실행권만 반환하지 않습니다.

취소·제한 시간·절전이 발생한 뒤 늦게 정상 반환된 값은 새 성공 결과로 적용하지 않습니다. 취소 토큰 소유자는 awaited runner이며 Closed 이벤트에서 살아 있는 reader의 토큰을 Dispose하지 않습니다.

## 자동 어댑터 갱신

기존 MainWindow의 직접 DispatcherTimer와 관찰 버튼 IsEnabledChanged 감시를 제거하고 `DeferredNetworkRefreshController` 하나로 교체합니다. 자동 갱신은 네트워크 요청이 아니라 기존 로컬 Windows 어댑터 조회입니다.

입력 신호:

- NetworkAddressChanged
- NetworkAvailabilityChanged
- 기존 화면 활성화 갱신
- 초기 어댑터 탭 생성
- 절전 복귀
- 전역 coordinator 상태 변경

네트워크 이벤트가 어느 스레드에서 오든 요청 비트 하나와 최대 하나의 대기 Dispatcher 알림으로 합칩니다. 준비됐을 때 기존과 같은 기본 750ms debounce를 적용합니다. 이벤트 수만큼 native reader를 반복 호출하지 않습니다.

실행 조건은 최신 전역 snapshot의 idle·shutdown 미요청, 창/기능별 종료 미진행, 절전 아님, 어댑터 UI 준비, 기존 호환 busy 상태 해제입니다. 이벤트에 포함된 과거 idle snapshot을 그대로 신뢰하지 않습니다.

- 다른 종류의 작업 실행 중: 요청 유지, timer 정지, 조회 0회
- 실제 lease 반환 후: 보류한 요청을 한 번 실행
- 갱신 실행 중 추가 이벤트: 중복 실행 없이 완료 후 한 번 더 갱신
- lease 획득 거부: 요청 유지, 상태 변화가 있을 때 재시도; 무한 timer 재시도 없음
- 조회 실패: 해당 시도 종료, 오류 유형 표시; 새 이벤트나 수동 갱신으로 재시도
- 창 닫힘: Core/NetworkChange 이벤트와 timer 구독 정리, 예약된 알림 무시

자동 갱신 거부는 현재 측정 결과/진행 상태 문구를 덮어쓰지 않습니다. 현재 작업이 끝나기 전 권장 NIC를 다시 선택하지 않습니다.

## 절전·복귀

절전 시 자동 갱신 timer를 멈추고 활성 로컬 진단에 협력적으로 취소를 요청합니다. 복귀는 새 갱신 요청을 남기되 전환 전 reader가 아직 끝나지 않았다면 기다립니다. 복귀했다고 해서 절전 전의 늦은 snapshot을 유효한 값으로 바꾸지 않습니다.

브라우저 관찰의 기존 SystemSuspend 우선순위와 전원 전후 카운터 분리 정책은 그대로 유지합니다. 관찰 전용 전원 상태의 재평가 플래그는 실제 읽기 완료 표시가 아니라 공유 대기 큐로의 요청 이전 시점에 소비합니다.

## 다섯 전용 보고서의 공통 UI

`AuxiliaryReportSection`이 각 보고서의 버튼·최근 성공 경로·화면 정리·구독 해제를 관리합니다. 각 기존 보고서 파일은 고유 설명과 기존 CreateDocument/WriteAll 호출만 연결합니다. 보고서 DTO·통계·JSON/CSV/HTML·SHA-256 생성기를 교체하지 않습니다.

- 동일 DiagnosticReportSave lease를 얻기 전에는 메모리 snapshot/로컬 수집/writer를 호출하지 않음
- 저장 중 같은 버튼·다른 보고서·다른 진단 실행 차단
- 저장 중 파일 열기 차단
- 실제 writer 반환 이후에만 최근 성공 export 경로 변경
- 데이터가 없거나 실패하면 이전 성공 경로 유지
- UI 정리 후 전역 lease 반환
- 일반 창 닫기는 실제 쓰기와 UI 정리 완료를 기다림
- 종료 대기 중 저장 오류면 창 유지, shutdown 상태 해제, 검토 후 재종료 허용
- 완료 메시지에는 전체 사용자 디렉터리 대신 파일명과 로컬 폴더 열기 기능 제공

### 남아 있는 파일 저장 한계

기존 전용 writer들의 저장 방식은 이 변경으로 동일한 원자적 파일세트 구현이 되지 않습니다. 전체 파일 묶음 rollback을 지원하지 않는 writer에는 취소 callback을 등록하지 않습니다. 쓰는 중인데 취소/완료로 오표시하거나 thread를 강제 종료하지 않습니다. 일부 파일을 쓴 뒤 실패할 수 있으므로 출력 폴더 확인을 안내합니다. 기존 전용 경로 비교 보고서의 ReportSaveSession/rollback 계약은 별도로 유지합니다.

## 자동 테스트

새 `OperationLifecycleSmoke`는 실제 WPF STA Dispatcher와 실제 MainWindow·Button.Click·Closing/Closed를 사용합니다. 창을 Show하지 않고 reader/writer를 합성 delegate로 교체하며 OS NetworkChange 구독을 시작하지 않습니다. 실제 회사 WLAN·DNS·HTTP·PAC/WPAD 테스트가 아닙니다.

25개 그룹:

- 모든 전역 종류 × 세 진단/다섯 보고서의 중복 실행 거부
- 세 진단의 종류·peer tab·입력 잠금·복원
- 라우팅 대상/목적 스냅샷, 실제 취소 버튼 두 번/취소 callback 한 번
- 진단 실패의 오류 유형만 표시·lease 반환
- 협력적 제한 시간 이후 늦은 결과 제외·실제 반환 대기
- 절전 전 값 폐기·복귀 후 새 조회
- 세 진단의 실제 창 닫기 대기
- Dispatcher 소유권·닫힌 창/예약 알림 정리
- 모든 전역 종류 뒤 자동 갱신, 오래된 idle 이벤트 무시
- 작업 스레드 이벤트 1,000회 합치기
- 갱신 중 이벤트 1,000회의 후속 한 번 처리
- 거부/오류 시 무한 재시도 방지
- 준비 지연·shutdown 해제 후 보류 요청 재개
- scheduler 폐기 중 실행 작업의 실제 완료 보존
- 다섯 보고서의 실제 버튼·저장·파일 열기 잠금
- 다섯 보고서의 오류 후 이전 성공 경로·재시도
- 다섯 보고서의 성공/실패 시 창 종료 경계
- 빈 데이터·늦은 탭·중복 탭·Closed 구독 해제
- 운영 소스가 공통 entry point와 기존 writer를 연결하는지 확인

각 그룹 15초, 전체 테스트 120초 상한을 둡니다. 이는 테스트 대기 상한이며 제품의 강제 native timeout이 아닙니다. verify-release.ps1에서 기존 열 개 프로젝트를 유지하고 열한 번째로 실행합니다. 최종 CI 통과 여부는 해당 PR의 정확한 head와 로그로 확인해야 합니다.

## 제품 경계

새 런타임 NuGet·AI/로컬 AI·외부 분석 API·텔레메트리·업로드·자동 업데이트·프록시 관리 접근을 추가하지 않습니다. 어댑터/환경 조회는 로컬 API만 사용합니다. 사용자가 명시한 라우팅 확인의 기존 DNS와 다운로드 HEAD/GET·프록시 인증 경계는 변경하지 않습니다.

## 아직 남은 작업과 진행률

이번 변경은 고정 기준 #16의 큰 부분을 연결하지만, 기존 수동 경로 비교·Windows 프록시 가져오기·전용 경로 비교 보고서의 Core-only lease와 기능별 Closing/peer-tab 처리까지 모두 중앙화한 것은 아닙니다. WLAN identity 대응 등 나머지 조회 경로와 짧은 기본 조회의 경쟁도 전체 점검해야 합니다.

따라서 #16을 부분 구현만으로 완료 계산하지 않습니다. 기존 16/20=80%를 유지하며 #18 입력 원문 경계, #19 최종 문서/공개 릴리스 검증, #20 안전한 비동기 WinHTTP도 남아 있습니다. 서명 인증서와 사용자 회사 환경 검증은 별도 출시 조건입니다.
