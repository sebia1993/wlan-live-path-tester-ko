# Changelog

모든 주요 변경 사항은 이 파일에 기록합니다.

## [Unreleased]

### Added

- 저장소 보안 및 네트워크 통신 경계
- .NET 10 WPF 솔루션 골격
- Core 측정 모델과 결정론적 자체 점검
- 현재 사용자 WinHTTP 프록시 설정을 로컬에서 확인하는 초기 경계
- Windows CI와 저장소 감사 스크립트
- Native WLAN API 기반 무선 인터페이스 및 현재 연결 정보 수집
- SSID, BSSID, RSSI, 신호 품질, 채널, 중심 주파수, PHY, Rx/Tx 링크 속도 표시
- 무선 인증·암호화 방식과 부분 권한 실패 상태 표시
- 2.4 GHz, 5 GHz, 6 GHz 중심 주파수의 채널 변환 자체 점검
- Windows runner에서 Native WLAN API를 실제 호출하는 반복 smoke test
- 대상 URL별 수동 프록시·바이패스·WPAD·PAC 경로 판정
- 프로토콜별 프록시, `<local>`, 와일드카드, 포트 바이패스 파서
- 다중 프록시와 DIRECT fallback 표시
- 내부망 DIRECT·외부망 PROXY 기대 경로 비교
- 프록시 주소와 PAC URL을 표시하지 않는 WPF 경로 확인 화면
- 합성 프록시 설정을 사용하는 결정론적 자체 점검 8개
- WinHTTP `HEAD`·`GET` 수신 전용 요청 계층
- HTTP 407 Negotiate 우선·NTLM 차선의 Windows 통합 인증 처리
- Basic·Digest·Passport 전용 프록시 거부와 401/407 분리
- 반복 407 재시도 상한, 자동 리다이렉트 차단, 최대 수신 바이트 적용
- SafeHandle 기반 WinHTTP 세션·연결·요청 리소스 정리
- 루프백 DIRECT·합성 Basic 407·GET 바이트 상한 smoke test
- 내부 DIRECT 및 외부 PROXY 다운로드 측정 실행 계층
- 선택적 HEAD 사전검사와 HEAD 405/501 GET fallback
- 전체 MaxBytes를 1~4개 스트림에 분배하는 다중 스트림 처리
- 평균 Mbps, 1초 구간 Mbps, TTFB, 소요시간, 부분 성공 모델
- 수동 리다이렉트 추적과 매 단계 URL·프록시 경로 재검증
- 외부 HTTPS 다운그레이드와 외부 로컬 주소 리다이렉트 차단
- Age, Via, Cache-Status, X-Cache, Content-Length, Content-Range 선택 헤더 수집
- 내부 URL 한 개와 외부 URL 최대 네 개를 실행·취소·비교하는 WPF 측정 화면
- 루프백 내부 서버와 합성 외부 프록시 기반 다운로드 측정 smoke test

### Changed

- WLAN AutoConfig 서비스 미실행 오류 1062를 별도 `ServiceNotRunning` 상태와 한국어 조치 문구로 정규화
- GitHub Actions를 Node 24 호환 v5 공식 commit SHA로 고정
- 현재 사용자 프록시 설정의 public API를 마스킹 결과 전용으로 축소
- WPAD → 명시적 PAC → 수동 프록시 순서의 제한적인 fallback 정책 적용
- PAC/WPAD 로그인 실패에서만 Windows 자동 로그온을 한 번 재시도하도록 제한
- 프록시 인증용 public 전송 API가 요청 본문을 받을 수 없도록 제한
- 각 다운로드 대상에 전체 최대 수신량을 적용하고 대상 내부 스트림끼리만 분할
- 외부 여러 대상은 동시에 몰아 보내지 않고 입력 순서대로 측정
- Via 응답 헤더는 WPF 화면에서 원문 대신 설정 여부만 표시
- 실제 WLAN·프록시·측정 검증은 사용자 로컬 검증 범위로 분리
