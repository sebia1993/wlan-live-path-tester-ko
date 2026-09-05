# Changelog

모든 주요 변경 사항은 이 파일에 기록합니다.

## [Unreleased]

### Added

- 프록시 원문·출처 우선순위·비동기 취소·privacy를 검증하는 `ProxyBoundarySmoke` 12개 그룹과 Release 검증 연결
- `docs/PROXY_RAW_INPUT_AND_CANCELLATION.md`에 원문 길이·제어 문자·늦은 반환의 처리 범위 기록
- 공통 Core 작업 조정기에 내부·외부 다운로드, 기존 프록시 경로 판정, 브라우저 관찰 WPF 실행 경로 연결
- 기존 내부 DIRECT–프록시 비교·Windows 프록시 가져오기와 동일한 coordinator 인스턴스 공유
- 활성 작업 외 탭 및 늦게 생성된 탭 잠금, 원래 비활성 상태·상속·binding 복원
- 취소 후 실제 작업 완료까지 busy 유지 및 Dispatcher 비차단 창 종료 대기
- 이전 관찰의 늦은 Progress callback이 새 결과를 덮어쓰지 않는 세션 확인
- 실제 WPF Dispatcher와 합성 delegate를 사용하는 `UiOperationSmoke` 10개 검증 그룹 및 Release 검증 연결
- `docs/APPLICATION_OPERATION_UI_INTEGRATION.md`에 적용 범위와 남은 이행 작업 명시

### Fixed

- 경로 지시문을 Trim한 뒤 검증해 앞뒤 제어 문자 또는 원문 길이 초과를 놓칠 수 있던 문제
- 대상별 DIRECT의 제어 문자 전용 입력이 빈 입력으로 축소되던 출처 선택 경계
- WPF 수동 프록시 입력을 Core 검증 전에 Trim하던 전달 경로
- 분석 콜백이 취소 뒤 정상 반환하면 늦은 payload를 Completed로 게시할 수 있던 문제

### Changed

- 사용자 토큰 취소로 발생한 다운로드 `OperationCanceledException`을 일반 오류가 아닌 취소 완료로 표시
- 탭 상태 복원을 Core lease 해제보다 먼저 수행해 늦은 정리가 새 작업을 해제하지 않도록 처리
- 경로 비교·가져오기·보고서의 기존 수명 및 종료 처리는 단계적 이행 동안 유지

### Security

- 작업 조정 UI와 합성 검증에 런타임 패키지, AI·로컬 AI, 외부 분석 API, 텔레메트리, 업로드 또는 자동 업데이트를 추가하지 않음
- 동기 WinHTTP 실행 중 네이티브 핸들을 강제로 닫지 않으며 실제 호출 반환 전에 완료로 처리하지 않음
- 원문 입력 검증 실패 시 실행 가능한 프록시 지시문을 반환하지 않고 대상별 실패를 수동 프록시 또는 DIRECT로 대체하지 않음

### Planned

- 반복 측정·로컬 경로 확인·보고서 저장·자동 어댑터 새로고침의 공통 작업 수명 연결
- `WINHTTP_FLAG_ASYNC`와 상태 콜백 기반 비동기 WinHTTP 전송 계층
- 목적지별 Windows route·interface metric을 개인정보 노출 없이 요약하는 로컬 경로 판정
- Authenticode 인증서 확보 후 빌드 서명과 서명 검증
- 실제 Windows 11·Aruba WLAN·회사 프록시 검증 결과 기반 호환성 수정
- 절전·재연결·USB Wi-Fi 제거·VPN 전환 장시간 안정성 시험

## [0.1.0-alpha.5] - 2026-09-04

### Added

- 물리 Wi-Fi, 유선, VPN·터널, Wi-Fi Direct와 주요 가상 어댑터의 로컬 분류
- Native WLAN 인터페이스와 Windows `NetworkInterface` ID·설명의 결정론적 대응
- 다중 Wi-Fi, VPN과 가상 NIC 환경을 확인하는 로컬 진단 탭과 전용 JSON·CSV·단일 HTML 보고서
- 브라우저 관찰 시작 시 선택된 물리 Wi-Fi 카운터 ID 고정
- `AdapterChanged`, `AdapterUnavailable`, `CounterProviderMismatch` 브라우저 관찰 종료 상태
- 초기 WLAN·카운터 ID 충돌, 관찰 중 WLAN ID 변경과 카운터 공급자 불일치 정책
- 고정 ID 선택·연속성·보고서 상태 매핑에 대한 ObservationSmoke·ReportSmoke
- 기본 Windows PR CI의 브라우저 관찰 Smoke 단계
- Portable ZIP에 다운로드, 프록시 경로, 관리자 정책, 인터페이스 환경, WLAN NIC 대응과 어댑터 진단 문서 포함

### Changed

- 브라우저 관찰 후속 샘플은 시작 시 고정한 인터페이스 ID만 사용하고 설명 또는 다른 활성 Wi-Fi로 자동 전환하지 않음
- 같은 물리 Wi-Fi에서 BSSID만 바뀌는 로밍과 물리 NIC 변경을 별도로 처리
- 어댑터 진단·WLAN NIC 대응·인터페이스 환경·어댑터 보고서 기능을 실제 WPF 앱 활성화 경로에 연결
- 어댑터 진단도 WLAN identity를 보완한 뒤 Native WLAN 연결 ID 우선순위를 적용
- 인터페이스 ID 정규화를 공용 결정론적 유틸리티로 통합
- Portable ZIP의 필수 운영 문서 목록을 패키지 검사에서 검증

### Fixed

- 현재 런타임 승인 정책이 교체할 새 대상 설정의 정의 검증을 방해하던 상태 의존성
- 어댑터 안정성 코드 일부만 병합돼 기본 `main` Release 빌드가 실패하던 누락 유틸리티와 미연결 partial class 참조
- Bluetooth PAN이 물리 Ethernet으로 분류되던 우선순위
- PANGP·Zscaler·Netskope·WARP·Check Point 등 기업 VPN·터널 식별 규칙 누락
- 관찰 중 Native WLAN이 다른 NIC로 바뀌면 선호 ID를 새 값으로 갱신해 서로 다른 카운터를 이어서 사용할 수 있던 문제
- 카운터 공급자가 고정한 ID와 다른 NIC를 반환해도 샘플 생성까지 진행할 수 있던 방어 경계

### Security

- 정확 ID 강제 모드에서 같은 설명의 다른 Wi-Fi로의 fallback 차단
- IP·MAC·게이트웨이 주소와 전체 인터페이스 GUID를 어댑터 보고서와 외부 전송에서 제외
- 새 진단·검증 기능은 로컬 WLAN·인터페이스 정보만 사용하며 외부 요청, AI, 텔레메트리와 업로드를 추가하지 않음

## [0.1.0-alpha.4] - 2026-09-04

### Added

- 로컬 Wi-Fi·유선·VPN·가상 인터페이스 환경 요약
- 다중 기본 게이트웨이, 유선·무선 동시 경로, VPN·가상 NIC 경고
- IP·게이트웨이·DNS·MAC 원문을 제외한 인터페이스 환경 JSON·CSV·단일 HTML 보고서
- Native WLAN과 로컬 NIC 대응을 위한 기반 모델과 Windows 환경 Smoke

### Security

- 인터페이스 환경 수집은 로컬 `NetworkInterface` 정보만 사용하고 DNS·HTTP·외부 API 요청을 수행하지 않음
- 구조화 보고서에서 인터페이스 이름·설명·GUID·IP·게이트웨이·DNS·MAC 주소 원문 제외

## [0.1.0-alpha.3] - 2026-09-04

### Added

- `%ProgramData%\WLAN Live Path Tester KO\targets.json` 관리자 승인 대상 정책
- 관리자 강제 정책에서 미등록 URL과 변경된 실행 제한의 Core 차단
- 손상되거나 읽을 수 없는 관리자 정책의 fail-closed 다운로드 차단
- 최대 1MiB, 엄격 UTF-8, reparse point 거부를 포함한 로컬 설정 파일 경계
- 관리자 정책 배포·ACL·실환경 검증 문서

### Changed

- 관리자 ProgramData 설정을 사용자·Portable 설정보다 우선 적용
- 새 설정 정의 검증과 현재 런타임 정책 집행을 분리해 안전한 정책 교체 허용

## [0.1.0-alpha.2] - 2026-09-04

### Added

- 엄격한 로컬 `targets.local.json` 승인 대상 설정
- 승인 목록 활성화 중 미등록 URL과 변경된 실행 제한을 Core 검증 단계에서 차단
- 최초 호스트 또는 `allowedRedirectHosts`에 등록된 호스트로만 리다이렉트 허용
- 알 수 없는 JSON 속성·주석·trailing comma 거부
- 최근 내부·외부 측정 결과의 구조화된 메모리 이력
- JSON·CSV·단일 HTML에 수신량, Mbps, TTFB, HTTP 상태, 프록시 사용, 스트림, 리다이렉트와 시간축 샘플 저장
- 수신량·측정 시간·스트림 완료·캐시 헤더 기반 개별 측정 신뢰도 판정
- 선택적 예열 1회와 본 측정 1~5회
- 예열 제외 중앙값, 최소·최대·평균·표준편차·변동계수 및 대표 회차 계산
- 성공·실패·미완료 횟수 분리와 캐시 영향이 반영된 반복 측정 신뢰도 판정
- 대상 하나와 작업 전체의 최대 예상 수신량 각각 2GiB 제한
- 반복 측정 전용 JSON·key-value CSV·단일 HTML·SHA-256 보고서
- 반복 측정과 승인 대상 설정 문서

### Changed

- 내부 대상은 기본 DIRECT, 외부 대상은 기본 PROXY로 안전한 기본값 적용
- 외부 교차 호스트 리다이렉트는 승인 목록과 기본 HTTP/HTTPS 포트를 모두 만족해야 진행
- 한 번의 속도값보다 반복 중앙값과 편차를 우선 해석하도록 UI와 문서 개선
- 취소는 요청 전·요청 사이·반복 사이·대상 사이에서 협력적으로 처리
- 동기 WinHTTP 호출 중 다른 스레드에서 핸들을 강제로 닫지 않도록 안전 경계 명시
- Portable ZIP에 승인 대상 및 반복 측정 가이드 포함

### Security

- 외부 HTTPS→HTTP 다운그레이드, 외부 로컬·사설·링크 로컬 주소 및 미승인 리다이렉트 차단
- 실제 URL·프록시·PAC·SSID·BSSID를 반복 측정 보고서에서 제외
- CSV 수식 주입 방지와 외부 리소스 없는 HTML CSP 유지
- 실제 사내 설정 파일과 결과·로그·캡처 파일의 저장소 및 배포물 포함 방지

## [0.1.0-alpha.1] - 2026-09-04

### Added

- .NET 10 WPF 솔루션과 Windows x64 self-contained 배포 기반
- Native WLAN API 기반 SSID, BSSID, RSSI, 채널, PHY, Rx/Tx 링크 속도 수집
- 현재 사용자 수동 프록시·바이패스·PAC·WPAD 경로 판정
- HTTP 407 Negotiate 우선·NTLM 차선 통합 인증
- 내부 DIRECT 및 외부 PROXY 수신 전용 HEAD/GET 다운로드 측정
- 1~4개 스트림, 최대 수신량, TTFB, 평균·구간 Mbps와 리다이렉트 처리
- 브라우저 다운로드 중 Wi-Fi 인터페이스 처리량·RSSI·BSSID 변화 관찰
- 로컬 JSON·CSV·단일 HTML 보고서와 SHA-256
- Windows CI, 저장소·통신 경계 감사와 Release 패키지 검사

### Security

- AI·외부 분석 API·텔레메트리·자동 업데이트·결과 업로드 없음
- 외부 POST·PUT·PATCH·DELETE와 업로드 속도 측정 미제공
- 다운로드 본문 비저장 및 고정 버퍼 즉시 폐기
- 프록시 주소, PAC URL, WLAN·사용자 식별정보 기본 마스킹
