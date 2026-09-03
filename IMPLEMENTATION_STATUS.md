# 구현 상태

기준일: 2026-09-04

| 영역 | 상태 | 비고 |
|---|---|---|
| 저장소 초기 커밋 | 완료 | 빈 저장소에 `main` 생성 |
| M0 문서·솔루션 골격 | 구현 완료·Windows CI 통과 | PR #1 병합 |
| Core 측정 모델 | 초기 구현 완료·자체 점검 통과 | WLAN·프록시 순수 규칙 포함 |
| 현재 사용자 프록시 설정 확인 | 구현 완료·public 값 마스킹 | 로컬 WinHTTP 설정만 읽음 |
| 저장소·통신 경계 감사 | 구현 완료·Windows PowerShell 5.1 통과 | 민감 산출물과 금지 통신 패턴 검사 |
| win-x64 self-contained publish | Smoke test 통과 | 정식 Release 자산은 아직 생성하지 않음 |
| WLAN Native API 수집 | 자동 검증 완료·실제 Windows 11 검증 필요 | M1, Issue #2, PR #12 |
| URL별 PAC/WPAD 판정 | 자동 검증 완료·실제 회사 환경 검증 필요 | M2, Issue #3, PR #13 |
| 407 프록시 인증 | 미착수 | M2, Issue #4 |
| 내부망 다운로드 측정 | 미착수 | M3, Issue #5 |
| 외부망 다운로드 측정 | 미착수 | M4, Issue #6 |
| 브라우저 다운로드 관찰 | 미착수 | M5, Issue #7 |
| 규칙 기반 보고서 | 최소 모델만 | M6, Issue #8 |
| Windows 배포·무결성 파일 | 미착수 | M7, Issue #9 |

## M2 Issue #3 구현 범위

- HTTP/HTTPS 절대 URL 검증과 URL userinfo·fragment 거부
- 현재 사용자의 수동 프록시와 바이패스 목록 판정
- HTTP/HTTPS 프로토콜별 프록시 문자열 처리
- `<local>`, 정확한 호스트, 포트, 와일드카드 바이패스
- WPAD 자동 검색과 명시적 PAC URL 처리
- WPAD 실패 후 PAC, 이후 수동 프록시 순서의 제한적 fallback
- PAC/WPAD 취득 중 `ERROR_WINHTTP_LOGIN_FAILURE`에서만 자동 로그온을 한 번 재시도
- 프록시 후보 개수와 DIRECT fallback 표시
- 실제 프록시 주소·PAC URL public API 및 화면 비노출
- 내부망 DIRECT·외부망 PROXY 기대 경로 일치 여부
- PAC/WPAD 호출은 사용자 버튼 실행 시에만 발생

## 자동 검증 기록

PR #1의 Windows CI에서 저장소 기반, 6개 자체 점검, 두 감사, self-contained publish를 확인했습니다.

PR #12의 Windows CI에서 Native WLAN P/Invoke·WPF 연결, 자체 점검 10개, Windows API smoke, 두 감사, self-contained publish를 확인했습니다.

PR #13의 Windows CI run #27에서 다음 항목을 확인했습니다.

- 수동 프록시·바이패스·WPAD·PAC P/Invoke와 WPF 경로 확인 화면 Release 빌드 성공
- 기존 10개와 프록시 파서 8개를 합한 결정론적 자체 점검 18개 통과
- Windows runner에서 현재 사용자 프록시 설정을 로컬로 읽고 public 결과의 마스킹 경계 확인
- Windows WLAN API 반복 smoke test 통과
- 저장소 감사와 네트워크 통신 경계 감사 통과
- win-x64 self-contained publish smoke test 통과

CI에서는 실제 PAC/WPAD 서버나 외부 URL을 조회하지 않습니다. 수동 프록시와 바이패스 판정은 합성 데이터로 검증했고, WinHTTP PAC/WPAD P/Invoke는 컴파일·배포 가능 여부를 검증했습니다.

## 현재 검증 범위

- 프로그램 시작 시 네트워크 연결이 없다.
- 현재 사용자 프록시 설정 확인은 로컬 설정만 읽는다.
- 대상 URL별 PAC/WPAD 판정은 사용자가 별도 버튼을 눌렀을 때만 실행된다.
- public 프록시 설정 결과와 WPF 화면에는 실제 프록시 주소·PAC URL이 표시되지 않는다.
- PAC/WPAD 자동 판정이 실패하고 안전한 fallback도 없으면 DIRECT로 추정하지 않고 `판단 불가`로 처리한다.
- WPAD와 명시적 PAC는 한 WinHTTP 호출에 섞지 않고 순서대로 수행한다.
- PAC/WPAD 로그인 실패에서만 자동 로그온을 한 번 재시도한다.
- 외부 대상 파일 본문 다운로드는 아직 구현하지 않았다.

## 아직 확인되지 않은 범위

- 실제 Windows 11 무선 어댑터의 반환값과 표시값
- 회사의 실제 수동 프록시·바이패스·PAC·WPAD 결과
- PAC 파일 취득 시 실제 Negotiate/NTLM 동작
- PAC 스크립트의 복잡한 JavaScript·DNS 동작
- `WinHttpSetTimeouts`가 회사 PAC/WPAD 환경에서 보장하는 실제 상한 시간
- 실제 HTTP 요청의 `407 Proxy Authentication Required` 처리
- 회사 GPO·EDR 환경
- 실제 Aruba WLAN
- 실제 내부·외부 다운로드 성능
- self-contained 실행 파일의 사내 PC 동작
