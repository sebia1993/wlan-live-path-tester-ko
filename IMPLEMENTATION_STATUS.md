# 구현 상태

기준일: 2026-09-04

| 영역 | 상태 | 비고 |
|---|---|---|
| 저장소 초기 커밋 | 완료 | 빈 저장소에 `main` 생성 |
| M0 문서·솔루션 골격 | 구현 완료·Windows CI 통과 | PR #1에서 구성 |
| Core 측정 모델 | 초기 구현 완료·자체 점검 통과 | 합성 데이터 자체 점검 6개 |
| 현재 사용자 프록시 설정 확인 | 초기 구현 완료·빌드 통과 | 로컬 WinHTTP 설정만 읽고 값은 기본 마스킹 |
| 저장소·통신 경계 감사 | 구현 완료·Windows PowerShell 5.1 통과 | 민감 산출물과 금지 통신 패턴 검사 |
| win-x64 self-contained publish | Smoke test 통과 | 정식 Release 자산은 아직 생성하지 않음 |
| WLAN Native API 수집 | 미착수 | M1, Issue #2 |
| URL별 PAC/WPAD 판정 | 미착수 | M2, Issue #3 |
| 407 프록시 인증 | 미착수 | M2, Issue #4 |
| 내부망 다운로드 측정 | 미착수 | M3, Issue #5 |
| 외부망 다운로드 측정 | 미착수 | M4, Issue #6 |
| 브라우저 다운로드 관찰 | 미착수 | M5, Issue #7 |
| 규칙 기반 보고서 | 최소 모델만 | M6, Issue #8 |
| Windows 배포·무결성 파일 | 미착수 | M7, Issue #9 |

## 자동 검증 기록

PR #1의 Windows CI에서 다음 항목을 확인했습니다.

- .NET 10 솔루션 Restore 성공
- Release 빌드 성공: 경고 0개, 오류 0개
- 결정론적 자체 점검 6개 통과
- 저장소 감사 통과: 금지 산출물·민감 파일 경로 검사
- 네트워크 통신 경계 감사 통과: 금지 메서드·외부 서비스·런타임 PackageReference 검사
- Windows x64 self-contained publish smoke test 통과

초기 감사 실행에서 발견한 Windows PowerShell 5.1의 UTF-8 해석 문제와 예약 변수 `$Host` 충돌은 수정 후 재검증했습니다.

## 현재 검증 범위

- 저장소에는 실제 사내 URL, 프록시 주소, PAC URL, SSID, BSSID, PCAP, 로그가 없어야 한다.
- Core 자체 점검은 실제 네트워크에 연결하지 않는다.
- WPF 애플리케이션 시작만으로 네트워크 요청을 만들지 않는다.
- 현재 사용자 프록시 확인 버튼은 로컬 Windows 설정만 읽으며 PAC 파일을 내려받거나 외부 URL에 연결하지 않는다.
- 빌드와 publish 검증은 GitHub의 Windows Server 2025 runner에서 수행했다.

## 아직 확인되지 않은 범위

- 실제 Windows 11 대화형 UI
- 회사 GPO·EDR 환경
- 사내 고정 프록시, PAC, WPAD
- Negotiate/NTLM 407 인증
- 실제 Aruba WLAN
- 실제 내부·외부 다운로드 성능
- self-contained 실행 파일의 사내 PC 동작
