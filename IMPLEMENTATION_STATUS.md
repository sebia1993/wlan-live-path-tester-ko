# 구현 상태

기준일: 2026-09-04

## 릴리스 상태

| 항목 | 현재 상태 |
|---|---|
| 실제 공개 최신 릴리스 | `v0.1.0-alpha.4` |
| 다음 검증 후보 | `v0.1.0-alpha.5` |
| alpha.5 소스 상태 | PR #38 병합 및 `main` Windows CI 성공 |
| alpha.5 배포 상태 | 릴리스 준비 PR의 전체 CI·패키지 검사 후 발행 예정 |
| 공개 릴리스 자산 정책 | Portable ZIP, single-file EXE, SHA256SUMS, THIRD_PARTY_NOTICES 정확히 4개 |
| 코드 서명 | 미적용 · SHA-256과 GitHub Release 출처로 확인 |

`v0.1.0-alpha.5`는 GitHub Release가 실제로 게시되고 네 자산과 SHA-256을 확인하기 전까지 공개 최신 버전으로 표기하지 않습니다.

## 전체 기능 상태

| 영역 | 상태 | 자동 검증 | 실제 환경 검증 |
|---|---|---|---|
| 저장소·솔루션·보안 경계 | 완료 | Windows CI·감사 | 불필요 |
| Native WLAN 현재 연결 수집 | 완료 | Native API Smoke | 사용자 담당 |
| 수동 프록시·PAC·WPAD 경로 판정 | 완료 | 합성 경로·파서 | 사용자 담당 |
| HTTP 407 Negotiate/NTLM | 완료 | 루프백 407 Smoke | 사용자 담당 |
| 내부망 DIRECT 다운로드 | 완료 | 루프백 측정 | 사용자 담당 |
| 프록시 경유 외부 다운로드 | 완료 | 합성 프록시 | 사용자 담당 |
| 승인 대상·리다이렉트 정책 | 완료 | 엄격 JSON·Core 경계 | 로컬 설정 배포 확인 |
| 관리자 ProgramData 강제 정책 | 완료 | fail-closed 정책 시험 | ACL·GPO 확인 |
| 반복 측정·대표값 | 완료 | 중앙값·편차·신뢰도 | 실제 경로 반복 측정 |
| 브라우저 다운로드 관찰 | 완료 | 카운터·선택·ID 고정 Smoke | 사용자 담당 |
| 물리 Wi-Fi·VPN·가상 NIC 진단 | 완료 | 분류·모호성 시험 | 사용자 담당 |
| WLAN과 로컬 NIC 대응 | 완료 | GUID·설명 대응 시험 | 사용자 담당 |
| 일반·반복·인터페이스·어댑터 보고서 | 완료 | JSON·CSV·HTML·SHA-256 | 공유 전 사용자 검토 |
| win-x64 self-contained 패키지 | 완료 | ZIP·single-file 검사 | 사용자 실행 확인 |

## 브라우저 관찰의 현재 경계

관찰 시작 시 선택된 물리 Wi-Fi의 카운터 ID를 고정합니다.

```text
Native WLAN 연결
   ↓ identity 보완
물리 Wi-Fi 후보 선택
   ↓ 시작 ID 고정
고정 ID의 카운터만 반복 조회
   ├─ 같은 ID + BSSID 변경: 로밍으로 기록하고 계속
   ├─ Native WLAN ID 변경: AdapterChanged
   ├─ 고정 NIC 사용 불가: AdapterUnavailable
   └─ 공급자 ID 불일치: CounterProviderMismatch
```

후속 샘플에서는 설명이 같거나 다른 활성 Wi-Fi가 존재하더라도 자동 전환하지 않습니다. 중단 전에 수집된 정상 샘플은 요약에 보존할 수 있지만 전용 종료 상태를 일반 성공으로 바꾸지 않습니다.

## 어댑터 환경 진단

다음 로컬 인터페이스 범주를 구분합니다.

- 물리 Wi-Fi와 물리 Ethernet
- Wi-Fi Direct·Hosted Network·SoftAP
- VPN·터널
- Hyper-V·VMware·VirtualBox·WSL·Docker
- Bluetooth PAN, Loopback와 기타 가상 인터페이스

다중 물리 Wi-Fi, 다중 기본 게이트웨이, 유선·무선 동시 기본 경로와 활성 VPN·가상 NIC를 경고합니다. 전체 인터페이스 GUID, IP, MAC, DNS와 게이트웨이 주소 원문은 구조화 보고서에 포함하지 않습니다.

## 자동 검증 범위

- .NET 10 Release 전체 빌드와 경고의 오류 처리
- Core 결정론적 SelfTest
- Native WLAN API 및 서비스·권한 오류 경계
- WLAN identity와 로컬 `NetworkInterface` 대응
- 물리 Wi-Fi·VPN·가상 NIC 분류와 다중 후보 모호성
- Bluetooth PAN과 기업 VPN·터널 식별 회귀
- 수동 프록시·PAC·WPAD와 407 인증 상태 머신
- 내부 DIRECT·합성 외부 PROXY HEAD/GET 측정
- 수신량 상한, 다중 스트림, 리다이렉트, HTTP 오류와 시간초과
- 승인 대상 JSON, 관리자 정책과 Core 실행 경계
- 브라우저 관찰 처리량·BSSID 변경·정지·급락 계산
- 정확 ID 강제 선택과 다른 NIC fallback 차단
- 초기 WLAN·카운터 충돌, 실행 중 WLAN ID 변경과 공급자 불일치
- 구조화 종료 상태의 보고서 매핑
- 일반·반복·인터페이스·어댑터 보고서의 마스킹, CSV 수식 방지, HTML CSP와 SHA-256
- 저장소·네트워크 통신 경계 감사
- Portable ZIP·single-file EXE, PE 헤더, 금지 파일, 필수 문서와 경로 순회 검사

자동 HTTP 시험은 `127.0.0.1` 합성 서버와 합성 프록시만 사용합니다. GitHub Actions는 실제 외부 사이트, 회사 프록시, PAC/WPAD 또는 사내 서버에 접속하지 않습니다.

## 사용자가 실제 환경에서 확인할 범위

- 실제 Windows 11 무선 NIC와 Native WLAN 반환값
- Aruba 환경의 SSID·BSSID·RSSI·채널·PHY·링크 속도
- 내장 Wi-Fi와 USB Wi-Fi 동시 활성 시 실제 연결 NIC 선택
- 같은 NIC의 BSSID 로밍과 다른 물리 Wi-Fi 전환 구분
- USB Wi-Fi 제거, 드라이버 재시작과 절전 복귀 시 종료 상태
- 회사 VPN, Hyper-V, WSL, VMware와 Wi-Fi Direct 분류
- 회사 수동 프록시·바이패스·PAC·WPAD 결과
- 실제 HTTP 407 Negotiate/NTLM 처리
- TLS 검사와 회사 루트 인증서
- GPO·EDR·SmartScreen 정책
- 내부 기준 서버의 DIRECT 경로와 충분한 성능
- 외부 승인 URL의 프록시 경유·정책·캐시·다운로드 안정성
- 브라우저 표시 속도와 고정 Wi-Fi 관찰값의 경향
- 생성 보고서에 실제 사내 식별정보가 남지 않는지

체크리스트는 [`docs/RELEASE_VALIDATION.md`](docs/RELEASE_VALIDATION.md)와 [`docs/BROWSER_OBSERVATION.md`](docs/BROWSER_OBSERVATION.md)를 따릅니다.

## 남은 개발 작업

### P0

1. **alpha.5 릴리스 준비 PR 전체 검증과 병합**
   - Windows CI·ObservationSmoke·Local Report CI·Release Package CI
   - Portable ZIP 필수 운영 문서와 네 자산 정책 검증
2. **실제 `v0.1.0-alpha.5` 발행 및 재검증**
   - 병합된 `main`에서 태그와 Release 생성
   - 네 자산 이름·크기·prerelease 상태 확인
   - `SHA256SUMS.txt`와 게시 자산 해시 재확인

### P1

3. **비동기 WinHTTP 전송 계층**
   - `WINHTTP_FLAG_ASYNC`와 상태 콜백 기반으로 전환
   - 연결·응답·본문 수신 중 안전한 즉시 취소
   - 실제 회사 프록시의 407 재인증과 취소 경쟁 검증
4. **목적지별 Windows 라우팅 근거**
   - 실제 대상 또는 프록시 목적지의 route/interface metric을 로컬에서 판정
   - IP·게이트웨이 원문을 보고서에 노출하지 않는 구조화 요약
5. **장시간·상태 전환 안정성**
   - 절전·재연결, USB NIC 제거, VPN 연결·해제와 가상 NIC 생성·삭제
   - 장시간 반복 측정과 브라우저 관찰 중 이벤트 경쟁

### P2

6. **Authenticode 코드 서명**
   - 인증서·타임스탬프 확보 후 빌드 서명과 CI 검증
7. **실환경 결과 기반 호환성 수정**
   - WLAN 드라이버, PAC/WPAD, 407, TLS 검사와 GPO·EDR 차이 반영
8. **정식 `v0.1.0` 전환**
   - 실제 Aruba WLAN·회사 프록시·다중 NIC·보고서 마스킹 검증 완료 후 진행
