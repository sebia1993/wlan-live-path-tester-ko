# Changelog

모든 주요 변경 사항은 이 파일에 기록합니다.

## [Unreleased]

### Planned

- `WINHTTP_FLAG_ASYNC`와 상태 콜백 기반 비동기 WinHTTP 전송 계층
- 관리자 강제 승인 대상 정책과 ProgramData 배포 우선순위
- 외부 DIRECT 모드가 추가될 경우 A/AAAA 주소 및 DNS 리바인딩 검증
- Authenticode 인증서 확보 후 빌드 서명과 서명 검증
- 실제 Windows 11·Aruba WLAN·회사 프록시 검증 결과 기반 호환성 수정

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
