# 중앙 실행·종료 수명과 경로 기능 통합

기반 main: `0d1705d0c646bf50915c5fcab55a3527a6c0fb5b`. 후속 PR: #129.

## 중앙 실행권 하나, Closing 처리 한 곳

MainWindow의 기존 ApplicationOperationCoordinator 하나를 ApplicationOperationUiSession에 주입합니다. 경로 비교·Windows 프록시 가져오기·전용 경로 보고서는 더 이상 Core lease를 직접 얻거나 개별 peer-tab dictionary와 Closing continuation을 만들지 않습니다.

운영 실행권 획득은 TryBeginUiApplicationOperation을 통합니다. 작업 종류는 고정 enum이며 URL·프록시 원문·파일 경로를 식별자로 사용하지 않습니다. 실제 작업과 UI 정리 전에는 실행권을 반환하지 않습니다. 취소 요청이나 제한 시간 경과만으로 native 호출을 버리지 않습니다.

| 영역 | 운영 진입점/공통 수명 |
|---|---|
| 단일·반복 다운로드 | RunCoordinatedMeasurementAsync와 같은 UI lease |
| 브라우저 관찰 | 관찰 UI lease, 고정 NIC/전원 전환 규칙 유지 |
| 기존 대상 URL 프록시 판정 | 기존 동기 resolver의 실제 반환까지 UI lease 유지 |
| 수동·불러온 판정 경로 비교 | RunRouteUiOperationAsync → RunLocalDiagnosticAsync |
| Windows 프록시 가져오기 | 같은 route/diagnostic 수명, 기존 importer 재사용 |
| 일반 라우팅·어댑터·환경 조회 | 기존 RunLocalDiagnosticAsync |
| 기본 WLAN·로컬 프록시 설정·WLAN/NIC 대응 | RunLocalDiagnosticAsync에 추가 연결 |
| 통합·다섯 전용 보고서 | 기존 공통 UI lease와 writer 완료 대기 |
| 경로 비교 전용 보고서 | 같은 UI lease + 기존 ReportSaveSession |
| 자동 어댑터 갱신 | 기존 DeferredNetworkRefreshController, 최신 중앙 snapshot으로 실행 판단 |

기능별 bool은 해당 화면의 버튼/진행 표시용일 수 있지만 앱 전체 idle을 독립적으로 판정하지 않습니다. 기존 중복 Core 획득 함수, route/import/report peer-tab 목록과 별도 종료 task를 제거했습니다.

## 경로 비교·Windows 판정

수동 경로 비교는 기존 coordinator와 안전 renderer를 그대로 사용합니다. 프록시 입력은 원문으로 전달하여 #125의 길이·제어 문자 검사를 우회하지 않습니다. 내부/외부 URL 등 나머지 원문 정규화 검토는 별도 #18 범위입니다.

Windows 가져오기는 다음 정책을 유지합니다.

- 자동 조회 동의 기본값 false
- 기존 WindowsRouteProxyImporter 사용, 중복 native 선언 없음
- PAC/WPAD 판정 출처와 수동 설정 출처 구분
- 대상별 실패를 수동 프록시 또는 DIRECT로 자동 대체하지 않음
- 동일 URL 및 monotonic 5분 미만 조건
- 불러온 selection을 수동 parser로 재구성하지 않고 그대로 비교 coordinator에 전달
- 네이티브 WLAN 조회 후에도 만료 여부 재확인

입력 대상/동의 값/실행 delegate는 시작 시 캡처합니다. 가져오기 중 외부 URL이 변경됐다 원래 값으로 돌아와도 revision이 다른 결과는 폐기합니다. 취소·절전·협력적 제한 시간 뒤 늦게 반환된 값은 최신 성공 결과로 적용하지 않습니다. 이전에 완료한 경로 비교 결과는 취소/오류로 덮어쓰지 않습니다.

RunLocalDiagnosticAsync의 기본 30초는 협력적 토큰 제한입니다. 동기 Windows 호출의 반환을 강제로 30초 안에 보장하지 않으며, 취소를 무시하는 호출은 실제 반환까지 기다립니다. 비동기 WinHTTP 즉시 취소는 #20으로 남습니다.

기본 프록시 설정 읽기는 로컬 설정만 읽습니다. 같은 작업 종류를 사용한다고 PAC/WPAD 조회를 새로 발생시키지 않습니다.

## 보고서: 취소 요청과 파일 게시 구분

경로 전용 보고서의 ReportSaveSession과 InternalProxyRouteComparisonRunReportWriter는 교체하지 않았습니다. 기존 파일세트 예약·부분 파일 정리·복구 예외·SHA-256 게시 계약을 유지합니다.

1. UI lease 획득 시 취소 요청을 latch에 기록
2. ReportSaveSession.TryStart 전후 latch 확인/재적용
3. 기존 writer 실행
4. writer와 CancelAsync callback, CTS 정리까지 포함한 completion 대기
5. 반환된 파일세트와 표시 내용을 검증한 뒤 성공 경로 반영
6. owner 버튼 복원 → peer 탭/binding 복원 → Core 실행권 반환

SHA256SUMS까지 게시된 성공 결과는 뒤늦은 중지 요청 때문에 취소로 바꾸지 않습니다. 취소된 쓰기가 반환되지 않았는데 완료됐다고 표시하지 않습니다. callback 자체가 늦게 끝나는 경우에도 실행권을 유지합니다.

저장 실패는 이전 성공 경로를 보존합니다. 종료 대기 중 실패·정리 미완료·취소 callback 오류가 확인되면 중앙 종료 처리가 창을 유지하고 shutdown 상태를 해제합니다. 검토 후 다시 저장하거나 닫을 수 있습니다. 예외 원문은 표시하지 않습니다.

이전 저장의 callback 실패는 다음 저장이 TryStart 전에 취소된 경우 새 작업 오류로 복사하지 않습니다. 잘못된 document/export가 반환된 경우도 기존 성공 경로를 교체하기 전에 검사합니다.

### 기존 다른 writer의 한계 검토

통합/일부 전용 legacy writer는 파일별 저장 방식입니다. 이번 UI 중앙화가 모든 writer의 파일 묶음 rollback 구현 완료를 의미하지 않습니다. 중간 취소를 안전하게 지원하지 않는 writer에는 취소 가능하다고 표시하지 않고 실제 쓰기 완료를 기다립니다. 일부 파일이 남을 수 있는 오류는 출력 폴더 확인을 계속 안내합니다.

#16은 장시간 진단·보고서·갱신의 실행권/화면/종료 수명 기준입니다. 기존 저장 한계를 감추거나 모든 writer를 교체했다고 주장하지 않습니다.

## 최종 Close와 재진입

Closing 구독은 MainWindow.ApplicationOperations.cs 한 곳에 있습니다.

- 활성 snapshot이면 첫 Close 보류
- shutdown으로 새 실행 차단, 지원되는 취소 요청
- 실제 실행/파일 정리/UI lease 완료 대기
- 최종 Close를 Dispatcher에 반드시 게시
- 저장 오류 검토가 필요하면 창 유지 및 shutdown 해제
- 다른 Closing handler가 최종 Close를 거절해도 shutdown 해제

Core.TryBegin 상태 알림 중 Close가 재진입할 수 있으므로 UI lease 필드 존재만 검사하지 않고 Snapshot.IsBusy를 검사합니다. 취소 callback이 동기적으로 작업을 완료해도 첫 Closing 안에서 Close를 재귀 호출하지 않습니다.

## 자동 검증

RouteUiLifetimeSmoke는 **28개 그룹**입니다.

- 경로/import 11개: 전역 kind 배제 행렬, raw 입력 스냅샷, 실제 취소 버튼 한 번, 동의/selection identity, URL revision, 5분 만료, 실제 종료 대기, binding/늦은 탭, 절전, 획득 중 Close 재진입, 안전한 오류
- 보고서 9개: 실제 버튼과 기존 writer, TryStart 전 취소, 실제 쓰기 대기, commit 뒤 취소, callback 완료 대기, 오류 후 이전 경로/재시도, callback 오류 검토, 성공 종료, binding/늦은 탭
- 기본/종료 7개: 기본 WLAN/설정 handler, 실제 NIC 대응 버튼, 기본 조회 종료 대기, 최종 Close 거절 뒤 재시작, 단일 측정 취소 callback 실패, Dispatcher 소유권, 닫힌 창 거부
- 오류 격리 1개: 이전 callback 오류 이후 새 pre-start 취소와 잘못된 export에서 이전 성공 경로 보존

창을 Show하지 않고 실제 WPF 객체·이벤트·Dispatcher에 합성 collector/import result를 주입합니다. 보고서 파일 테스트는 고유 임시 디렉터리에서 기존 writer를 호출합니다. 회사 WLAN·실제 DNS·HTTP·PAC/WPAD 시험이 아닙니다. 그룹당 15초, 전체 120초는 테스트 대기 상한이며 native 강제 종료 보장이 아닙니다.

기존 11개 프로젝트와 보안/통신 감사는 유지합니다. source 계약은 구형 필드 대신 한 coordinator/한 Closing, 안전 renderer, 동의·출처·TTL, 취소 latch와 callback 정리의 실제 운영 경로를 검사합니다. 작성된 테스트와 CI 성공은 다르며 최종 head의 로그와 merge SHA를 별도로 기록합니다.

## 제품·진행률 경계

런타임 NuGet·AI/로컬 AI·외부 분석 API·텔레메트리·업로드·자동 업데이트·프록시 관리 접근을 추가하지 않습니다. 기존 사용자 실행 HEAD/GET·DNS·Windows proxy auth는 유지합니다.

기반은 16/20=80%입니다. #16 최종 자동 검증과 main 반영이 모두 확인되면 17/20=85%입니다. #18 원문 입력, #19 문서/새 공개 릴리스 게시 후 검증, #20 비동기 WinHTTP는 미완료입니다. 회사 검증과 코드 서명은 별도 출시 조건입니다. 이번 소스 PR은 새 공개 Release를 게시하지 않습니다.
