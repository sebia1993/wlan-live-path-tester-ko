# 구현 상태

기준일: 2026-09-04

| 영역 | 상태 | 비고 |
|---|---|---|
| 저장소 초기 커밋 | 완료 | 빈 저장소에 `main` 생성 |
| M0 문서·솔루션 골격 | 구현 완료·Windows CI 통과 | PR #1 병합 |
| Core 측정 모델 | 초기 구현 완료·자체 점검 통과 | 기존 6개 + WLAN 채널 계산 4개 |
| 현재 사용자 프록시 설정 확인 | 초기 구현 완료·빌드 통과 | 로컬 WinHTTP 설정만 읽고 값은 기본 마스킹 |
| 저장소·통신 경계 감사 | 구현 완료·Windows PowerShell 5.1 통과 | 민감 산출물과 금지 통신 패턴 검사 |
| win-x64 self-contained publish | Smoke test 통과 | 정식 Release 자산은 아직 생성하지 않음 |
| WLAN Native API 수집 | 자동 검증 완료·실제 Windows 11 검증 필요 | M1, Issue #2, PR #12 |
| URL별 PAC/WPAD 판정 | 미착수 | M2, Issue #3 |
| 407 프록시 인증 | 미착수 | M2, Issue #4 |
| 내부망 다운로드 측정 | 미착수 | M3, Issue #5 |
| 외부망 다운로드 측정 | 미착수 | M4, Issue #6 |
| 브라우저 다운로드 관찰 | 미착수 | M5, Issue #7 |
| 규칙 기반 보고서 | 최소 모델만 | M6, Issue #8 |
| Windows 배포·무결성 파일 | 미착수 | M7, Issue #9 |

## M1 구현 범위

- Native WLAN API로 무선 인터페이스 열거
- 현재 연결의 SSID, BSSID, PHY 유형, 신호 품질, Rx/Tx PHY 링크 속도
- 별도 쿼리를 통한 RSSI와 채널
- 현재 BSSID와 BSS 목록을 비교한 중심 주파수
- 2.4 GHz, 5 GHz, 6 GHz 주파수의 채널 계산
- 인증·암호화 방식 표시
- 미연결, 인터페이스 없음, 접근 거부, 서비스 미실행, Native API 오류 및 부분 정보 실패 구분
- WinHTTP·WLAN 버튼 실행 전에는 네트워크 연결을 만들지 않음
- Windows runner에서 WLAN API를 두 번 호출하는 별도 smoke test

## 자동 검증 기록

PR #1의 Windows CI에서 다음 항목을 확인했습니다.

- .NET 10 솔루션 Restore 성공
- Release 빌드 성공: 경고 0개, 오류 0개
- 결정론적 자체 점검 6개 통과
- 저장소 감사 통과
- 네트워크 통신 경계 감사 통과
- Windows x64 self-contained publish smoke test 통과

PR #12의 Windows CI run #19에서 다음 항목을 확인했습니다.

- Native WLAN P/Invoke 선언과 WPF 연결 Release 빌드 성공
- 결정론적 자체 점검 10개 통과
- GitHub Windows Server 2025 runner에서 `wlanapi.dll` 실제 호출 2회 성공적 종료
- runner의 WLAN AutoConfig 서비스 미실행 오류 1062를 `ServiceNotRunning`으로 정규화
- 저장소 감사와 네트워크 통신 경계 감사 통과
- win-x64 self-contained publish smoke test 통과
- `actions/checkout@v5`, `actions/setup-dotnet@v5` 공식 commit SHA 고정

GitHub runner에는 실제 무선 어댑터와 실행 중인 WLAN AutoConfig 서비스가 없으므로, 이 검증은 DLL 로딩·호출 실패 처리·반복 호출의 핸들 정리 경계를 확인합니다. 실제 SSID/BSSID/RSSI/채널/PHY 값의 정확성은 보증하지 않습니다.

## 현재 검증 범위

- 저장소에는 실제 사내 URL, 프록시 주소, PAC URL, SSID, BSSID, PCAP, 로그가 없어야 한다.
- Core 자체 점검은 실제 네트워크에 연결하지 않는다.
- WPF 애플리케이션 시작만으로 네트워크 요청을 만들지 않는다.
- 현재 사용자 프록시 확인 버튼은 로컬 Windows 설정만 읽으며 PAC 파일을 내려받거나 외부 URL에 연결하지 않는다.
- WLAN 확인 버튼은 로컬 `wlanapi.dll`만 사용하고 외부 네트워크 요청을 만들지 않는다.
- WLAN 서비스 미실행 상태는 일반 오류와 분리해 조치 문구를 제공한다.

## 아직 확인되지 않은 범위

- 실제 Windows 11 무선 어댑터의 반환값과 표시값
- 드라이버별 BSS 구조체 마샬링
- Windows 위치 권한 거부 시 실제 반환 코드
- 회사 GPO·EDR 환경
- 사내 고정 프록시, PAC, WPAD
- Negotiate/NTLM 407 인증
- 실제 Aruba WLAN
- 실제 내부·외부 다운로드 성능
- self-contained 실행 파일의 사내 PC 동작
