# 구현 상태

기준일: 2026-09-04

| 영역 | 상태 | 비고 |
|---|---|---|
| 저장소 초기 커밋 | 완료 | 빈 저장소에 `main` 생성 |
| M0 문서·솔루션 골격 | 구현 완료·Windows CI 통과 | PR #1 병합 |
| Core 측정 모델 | 초기 구현 완료·자체 점검 통과 | WLAN·프록시·인증 순수 규칙 포함 |
| 현재 사용자 프록시 설정 확인 | 구현 완료·public 값 마스킹 | 로컬 WinHTTP 설정만 읽음 |
| 저장소·통신 경계 감사 | 구현 완료·Windows PowerShell 5.1 통과 | 민감 산출물과 금지 통신 패턴 검사 |
| win-x64 self-contained publish | Smoke test 통과 | 정식 Release 자산은 아직 생성하지 않음 |
| WLAN Native API 수집 | 자동 검증 완료·실제 Windows 11 검증 필요 | M1, Issue #2, PR #12 |
| URL별 PAC/WPAD 판정 | 자동 검증 완료·실제 회사 환경 검증 필요 | M2, Issue #3, PR #13 |
| 407 프록시 인증 | 자동 검증 완료·실제 회사 환경 검증 필요 | M2, Issue #4, PR #14 |
| 내부망 다운로드 측정 | 미착수 | M3, Issue #5 |
| 외부망 다운로드 측정 | 전송 기반만 구현 | M4, Issue #6에서 처리량·리다이렉트·헤더·UI 추가 필요 |
| 브라우저 다운로드 관찰 | 미착수 | M5, Issue #7 |
| 규칙 기반 보고서 | 최소 모델만 | M6, Issue #8 |
| Windows 배포·무결성 파일 | 미착수 | M7, Issue #9 |

## M2 Issue #4 구현 범위

- 외부 런타임 패키지 없는 WinHTTP `HEAD`·`GET` 수신 전용 요청 계층
- request body를 받지 않는 public API로 업로드 경로 차단
- 현재 사용자 프록시·PAC·WPAD 경로 판정 결과 적용
- 407 응답에서 `WinHttpQueryAuthSchemes`로 인증 방식 확인
- Negotiate 우선, NTLM 차선 선택
- 사용자명·비밀번호 문자열 없이 현재 Windows 사용자 기본 자격 증명 사용
- Basic·Digest·Passport 전용 프록시 거부
- 원격 사이트 401과 프록시 407 분리
- 프록시 인증 1회 상한 및 반복 407 중단
- 자동 리다이렉트 차단
- GET 본문 고정 버퍼 처리, 최대 수신 바이트 적용, 파일 저장 없음
- SafeHandle 기반 세션·연결·요청 핸들 정리
- 루프백 합성 HTTP 서버·프록시로 DIRECT, Basic 407 거부, 바이트 상한 점검

## 자동 검증 기록

PR #1의 Windows CI에서 저장소 기반, 6개 자체 점검, 두 감사, self-contained publish를 확인했습니다.

PR #12의 Windows CI에서 Native WLAN P/Invoke·WPF 연결, 자체 점검 10개, Windows API smoke, 두 감사, self-contained publish를 확인했습니다.

PR #13의 Windows CI에서 수동 프록시·바이패스·WPAD·PAC P/Invoke, 자체 점검 18개, 마스킹 경계, Windows API smoke, 두 감사, self-contained publish를 확인했습니다.

PR #14의 Windows CI에서 다음 항목을 확인했습니다.

- 누락됐던 WinHTTP 요청·인증 P/Invoke 선언을 포함한 .NET 10 Release 빌드 성공
- 기존 결정론적 SelfTest와 Windows WLAN API smoke test 통과
- 루프백 DIRECT `HEAD` 요청 성공
- Basic 전용 합성 프록시의 407을 자격 증명 전송 없이 거부
- Negotiate 우선·NTLM 차선·서버 인증 대상 거부 규칙 통과
- 반복 407 인증 재시도 상한 점검 통과
- GET 응답의 최대 수신 바이트에서 읽기 중단 및 본문 비저장 확인
- 저장소 감사와 네트워크 통신 경계 감사 통과
- win-x64 self-contained publish smoke test 통과

자동 테스트는 `127.0.0.1` 합성 서버만 사용하며 실제 외부 사이트, 실제 회사 프록시 또는 PAC/WPAD 서버를 호출하지 않습니다.

## 현재 검증 범위

- 프로그램 시작 시 네트워크 연결이 없다.
- 현재 사용자 프록시 설정 확인은 로컬 설정만 읽는다.
- 대상 URL별 PAC/WPAD 판정은 사용자가 별도 버튼을 눌렀을 때만 실행된다.
- public 프록시 설정 결과와 WPF 화면에는 실제 프록시 주소·PAC URL이 표시되지 않는다.
- PAC/WPAD 자동 판정이 실패하고 안전한 fallback도 없으면 DIRECT로 추정하지 않고 `판단 불가`로 처리한다.
- WinHTTP 요청 계층은 `HEAD`와 `GET`만 제공하며 요청 본문 입력 경로가 없다.
- 407은 낮은 속도로 변환하지 않고 인증 호환·실패 상태로 분리한다.
- 현재 Windows 사용자 통합 인증은 Negotiate와 NTLM에만 허용한다.
- 외부 대상 파일 본문은 메모리 버퍼에서 읽은 뒤 폐기하고 파일로 저장하지 않는다.
- 자동 리다이렉트는 현재 차단하며 다음 측정 단계에서 URL 재검증 후 수동 추적한다.

## 사용자가 실제 환경에서 확인할 범위

- 실제 Windows 11 무선 어댑터의 반환값과 표시값
- 회사의 실제 수동 프록시·바이패스·PAC·WPAD 결과
- PAC 파일 취득 시 실제 Negotiate/NTLM 동작
- 실제 HTTP 407 프록시에서 현재 Windows 사용자 기본 자격 증명 처리
- TLS 검사 환경과 회사 루트 인증서
- 회사 GPO·EDR 환경
- 실제 Aruba WLAN
- 실제 내부·외부 다운로드 성능
- self-contained 실행 파일의 사내 PC 동작
