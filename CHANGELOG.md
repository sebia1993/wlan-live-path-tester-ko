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

### Changed

- WLAN AutoConfig 서비스 미실행 오류 1062를 별도 `ServiceNotRunning` 상태와 한국어 조치 문구로 정규화
- GitHub Actions를 Node 24 호환 v5 공식 commit SHA로 고정
