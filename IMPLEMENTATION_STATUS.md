# 구현 상태

기준일: 2026-09-04

| 영역 | 상태 | 비고 |
|---|---|---|
| 저장소 초기 커밋 | 완료 | 빈 저장소에 `main` 생성 |
| M0 문서·솔루션 골격 | 구현 완료·Windows CI 통과 | PR #1 병합 |
| Core 측정 모델 | 구현 진행 | WLAN·프록시·인증·다운로드 모델과 순수 규칙 포함 |
| 현재 사용자 프록시 설정 확인 | 구현 완료·public 값 마스킹 | 로컬 WinHTTP 설정만 읽음 |
| 저장소·통신 경계 감사 | 구현 완료·Windows PowerShell 5.1 통과 | 민감 산출물과 금지 통신 패턴 검사 |
| win-x64 self-contained publish | Smoke test 통과 | 정식 Release 자산은 아직 생성하지 않음 |
| WLAN Native API 수집 | 자동 검증 완료·실환경 검증은 사용자 범위 | M1, Issue #2, PR #12 |
| URL별 PAC/WPAD 판정 | 자동 검증 완료·실환경 검증은 사용자 범위 | M2, Issue #3, PR #13 |
| 407 프록시 인증 | 자동 검증 완료·실환경 검증은 사용자 범위 | M2, Issue #4, PR #14 |
| 내부망 다운로드 측정 | 구현 완료·자동 검증 중 | M3, Issue #5, PR #15 |
| 외부망 다운로드 측정 | 구현 완료·자동 검증 중 | M4, Issue #6, PR #15 |
| 브라우저 다운로드 관찰 | 미착수 | M5, Issue #7 |
| 규칙 기반 보고서 | 최소 모델만 | M6, Issue #8 |
| Windows 배포·무결성 파일 | 미착수 | M7, Issue #9 |

## M3·M4 PR #15 구현 범위

- 내부망은 DIRECT, 외부망은 PROXY 기대 경로를 요청 전에 강제
- 선택적 HEAD 사전검사와 HEAD 미지원 405/501의 GET fallback
- WinHTTP GET 스트리밍과 수신 본문 즉시 폐기
- 전체 최대 수신량을 1~4개 스트림에 분배해 총량 상한 유지
- 평균 Mbps, 1초 구간 Mbps, TTFB, 전체 소요시간
- HTTP 상태, 프록시 사용 여부, 완료 스트림 수, 부분 성공
- 자동 리다이렉트를 끄고 상위 측정 계층에서 매 Location 재검증
- 외부 HTTPS→HTTP 다운그레이드 차단
- 외부 대상의 localhost·루프백·사설·링크 로컬·점 없는 내부 이름 차단
- URL userinfo·fragment 차단과 query 기본 마스킹
- Age, Via, Cache-Status, X-Cache, Content-Length, Content-Range 등 선택 헤더 기록
- 내부 URL 1개, 외부 URL 최대 4개, 공통 제한값 및 취소 버튼 WPF 화면
- Via 값은 화면에서 원문 대신 설정 여부만 표시
- 외부 여러 대상은 입력 순서대로 측정해 불필요한 동시 부하를 제한

## 자동 검증 기록

PR #1의 Windows CI에서 저장소 기반, 6개 자체 점검, 두 감사, self-contained publish를 확인했습니다.

PR #12의 Windows CI에서 Native WLAN P/Invoke·WPF 연결, 자체 점검 10개, Windows API smoke, 두 감사, self-contained publish를 확인했습니다.

PR #13의 Windows CI에서 수동 프록시·바이패스·WPAD·PAC P/Invoke, 자체 점검 18개, 마스킹 경계, Windows API smoke, 두 감사, self-contained publish를 확인했습니다.

PR #14의 Windows CI에서 WinHTTP HEAD/GET, 407 Negotiate/NTLM 상태 머신, 루프백 DIRECT·Basic 407·수신 상한, 두 감사와 self-contained publish를 확인했습니다.

PR #15의 Windows CI는 다음 항목을 검증하도록 구성했습니다.

- .NET 10 Release 빌드와 기존 전체 자체 점검
- 안전한 상대 리다이렉트 허용
- 외부 HTTPS→HTTP 다운그레이드 차단
- 외부 로컬 주소 리다이렉트 차단
- 내부 DIRECT HEAD 리다이렉트, GET, 실제 수신량, TTFB, 평균·구간 처리량
- Age·X-Cache·Cache-Status 등 선택 응답 헤더
- 합성 외부 프록시 경유 2스트림과 전체 MaxBytes 상한
- 사전 취소와 잘못된 내부 프록시 설정의 요청 전 차단
- WinHttpReadData 본문 수신 시간초과
- 저장소·네트워크 경계 감사와 win-x64 self-contained publish

자동 HTTP 테스트는 `127.0.0.1` 합성 서버만 사용합니다. `example.invalid` 외부 대상은 루프백 합성 프록시에만 전달하며 실제 DNS나 외부 사이트에 연결하지 않습니다.

## 현재 자동 검증 경계

- 프로그램 시작 시 네트워크 연결이 없다.
- WLAN 확인은 로컬 `wlanapi.dll`만 호출한다.
- 로컬 프록시 설정 확인은 PAC 파일이나 대상 URL에 접속하지 않는다.
- PAC/WPAD 판정은 사용자가 별도 경로 확인을 실행한 경우에만 발생한다.
- 다운로드는 사용자가 내부 또는 외부 측정 시작 버튼을 누른 경우에만 실행된다.
- WinHTTP 전송 API는 HEAD와 GET만 제공하며 요청 본문 입력 경로가 없다.
- 401, 403, 407, 429, 시간초과, 경로 불일치와 정책 차단은 낮은 Mbps로 변환하지 않는다.
- 407 통합 인증은 Negotiate와 NTLM에만 제한하고 1회 재시도 뒤 중단한다.
- 응답 본문은 메모리 버퍼에서 읽은 뒤 폐기하고 파일로 저장하지 않는다.
- 모든 리다이렉트는 새 URL과 기대 프록시 경로를 다시 검사한다.
- 프록시 주소와 PAC URL은 public 결과와 WPF 화면에 노출하지 않는다.
- 실제 프록시 내부 상태를 확인한 것처럼 판정하지 않는다.

## 사용자가 실제 환경에서 확인할 범위

- 실제 Windows 11 무선 어댑터의 반환값과 표시값
- 실제 Aruba WLAN의 RSSI·BSSID·채널·링크 속도
- 회사의 수동 프록시·바이패스·PAC·WPAD 결과
- 실제 HTTP 407 Negotiate/NTLM 처리
- TLS 검사 환경과 회사 루트 인증서
- 회사 GPO·EDR 환경
- 내부 기준 서버의 적정 크기·성능과 DIRECT 경로
- 외부 승인 URL의 안정성·요청 정책·캐시 특성
- 실제 내부·외부 다운로드 처리량
- self-contained 실행 파일의 사내 PC 동작
