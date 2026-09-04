# WLAN Live Path Tester KO

Windows 11에서 현재 무선랜 연결 상태, 내부망 다운로드 성능, 회사 프록시를 경유한 외부 사이트 다운로드 성능을 로컬에서 진단하는 도구입니다.

## 측정하는 경로

```text
무선 단말
  ├─ WLAN 링크: SSID, BSSID, RSSI, 채널, PHY, Rx/Tx 링크 속도
  ├─ 내부망: AP → 컨트롤러/터널 → 사내 유선 서버
  └─ 외부망: AP → 사내망 → 회사 프록시 → 외부 사이트/CDN
```

외부 결과는 인터넷 회선만의 순수 속도가 아니라 **회사 프록시를 포함한 외부 서비스 체감 다운로드 속도**입니다. 이 프로그램은 프록시 서버 내부의 CPU·세션·큐·캐시·정책 로그에 접근하지 않으므로 프록시 장애로 단정하지 않고, 내부망과 복수 외부 대상의 결과를 비교해 장애 범위를 좁힙니다.

## 고정 보안 원칙

- AI, LLM, 로컬 AI와 외부 분석 API를 사용하지 않습니다.
- 텔레메트리, 자동 오류 보고, 자동 업데이트, 공인 IP·ISP·GeoIP 조회를 하지 않습니다.
- PCAP, 로그, 측정 결과, WLAN 정보와 사용자 정보를 외부로 업로드하지 않습니다.
- 외부 통신은 사용자가 명시적으로 시작한 HTTP/HTTPS `HEAD` 또는 `GET` 다운로드 측정에만 허용합니다.
- 외부 `POST`, `PUT`, `PATCH`, `DELETE`와 업로드 속도 측정은 제공하지 않습니다.
- 다운로드 본문은 파일로 저장하지 않고 고정 크기 메모리 버퍼에서 읽은 즉시 폐기합니다.
- 결과는 로컬 JSON, CSV, 외부 리소스 없는 단일 HTML로 저장합니다.
- 근거가 부족하면 `판단 불가`로 처리하며 프록시 내부 상태를 추정하지 않습니다.

자세한 통신·데이터 경계는 [`docs/NETWORK_BOUNDARY.md`](docs/NETWORK_BOUNDARY.md), [`PRIVACY.md`](PRIVACY.md)와 [`SECURITY.md`](SECURITY.md)를 기준으로 합니다.

## 실행 환경

- Windows 11 x64
- C# / .NET 10 / WPF
- Windows Native WLAN API
- Windows WinHTTP
- Windows IP Helper API
- 런타임 NuGet 패키지 0개
- Python 또는 별도 .NET 런타임 설치 불필요
- 관리자 권한 불필요

## 구현된 기능

### WLAN 상태

- 현재 무선 인터페이스와 연결 상태
- SSID와 BSSID
- RSSI와 신호 품질
- 채널, 중심 주파수와 2.4/5/6GHz 밴드
- PHY 유형
- Rx/Tx PHY 링크 속도
- 인증과 암호화 방식
- WLAN AutoConfig 서비스 중지·권한 제한·부분 조회 실패 구분

PHY 링크 속도는 실제 애플리케이션 처리량이 아니므로 내부·외부 다운로드 실측과 별도로 표시합니다.

### 회사 프록시 경로

- 현재 로그인 사용자의 수동 프록시·바이패스·PAC·WPAD 설정 확인
- 대상 URL별 `DIRECT`, `PROXY`, 판단 불가 구분
- HTTP 407에서 Negotiate 우선, NTLM 차선의 Windows 통합 인증
- Basic·Digest·Passport 전용 프록시 거부
- 원격 서버 401과 프록시 407 분리
- 내부 대상은 DIRECT, 외부 대상은 PROXY 기대 경로를 요청 전에 검사
- 실제 프록시 주소, PAC URL과 바이패스 원문 마스킹

### 내부·외부 다운로드 측정

- 선택적 HEAD 사전검사와 GET 스트리밍 다운로드
- 1~4개 병렬 스트림
- 대상별 최대 수신량과 제한 시간
- 외부 URL 최대 4개 순차 측정
- 평균 Mbps, 1초 구간 Mbps, TTFB와 전체 소요시간
- HTTP 상태, 완료 스트림, 리다이렉트, 최종 URL과 경로 일치 여부
- `Age`, `Via`, `Cache-Status`, `X-Cache`, `Content-Length`, `Content-Range` 등 제한된 응답 메타데이터
- 모든 리다이렉트 URL과 기대 프록시 경로 재검사
- 외부 HTTPS→HTTP 다운그레이드, 외부 로컬·사설 주소와 미승인 교차 호스트 리다이렉트 차단

### 승인 대상 설정

선택적으로 `targets.local.json`을 배포해 내부 1개와 외부 1~4개의 승인 URL 및 실행 제한을 고정할 수 있습니다.

- 알 수 없는 JSON 속성, 주석과 trailing comma 거부
- 내부 대상 기본 DIRECT, 외부 대상 기본 PROXY
- 승인 목록 활성화 중 미등록 URL과 변경된 수신량·제한 시간·스트림·리다이렉트 제한을 Core 검증 단계에서 차단
- 최초 호스트 또는 `allowedRedirectHosts`에 정확히 등록한 호스트로만 리다이렉트 허용
- 실제 사내 설정 파일은 `.gitignore` 대상

설정 방법은 [`docs/TARGET_CONFIGURATION.md`](docs/TARGET_CONFIGURATION.md)를 참고하십시오.

### 브라우저 다운로드 관찰

- 프로그램 자체 외부 요청 없이 Edge·Chrome 등의 실제 다운로드 중 Wi-Fi 인터페이스 Rx/Tx 카운터 관찰
- 시작 전 3초 백그라운드 기준 수집
- 기준치 제외 수신 처리량
- RSSI·PHY 링크 속도·BSSID 변화 동시 기록
- 일시 정지, 급락, 인터페이스 변경과 카운터 재설정 감지

이 모드는 Wi-Fi 인터페이스 전체 트래픽을 관찰하므로 브라우저 외 다른 프로그램의 통신도 포함될 수 있습니다. 자세한 내용은 [`docs/BROWSER_OBSERVATION.md`](docs/BROWSER_OBSERVATION.md)를 참고하십시오.

### 반복 측정과 대표값

- 선택적 예열 1회
- 본 측정 1~5회
- 대상별 순차 실행과 측정 간 대기
- 예열을 제외한 본 측정 중앙값
- 최소·최대·평균·표준편차·변동계수
- 중앙값에 가장 가까운 대표 회차
- 성공·실패·미완료 횟수 분리
- 수신량·측정 시간·스트림 완료·캐시·반복 편차에 따른 Low/Medium/High 신뢰도
- 대상 하나와 작업 전체의 최대 예상 수신량 각각 2GiB 제한

반복 측정 방법과 해석은 [`docs/REPEATED_MEASUREMENT.md`](docs/REPEATED_MEASUREMENT.md)를 참고하십시오.

### 로컬 보고서

일반 진단 보고서와 반복 측정 전용 보고서를 각각 제공합니다.

```text
일반 보고서
├─ JSON
├─ CSV
├─ 단일 HTML
└─ SHA256SUMS.txt

반복 측정 보고서
├─ 중앙값·편차·신뢰도 구조화 JSON
├─ 요약·회차별 key-value CSV
├─ 외부 리소스 없는 단일 HTML
└─ SHA256SUMS.txt
```

- SSID·BSSID·MAC·IP·이메일·Windows 사용자 경로·URL 호스트와 쿼리 마스킹
- CSV 수식 주입 방지
- HTML 인코딩과 Content Security Policy
- 외부 JavaScript·CSS·웹폰트·이미지·iframe 없음
- 보고서 자동 업로드 없음

마스킹은 보조 수단이므로 회사 밖으로 공유하기 전에는 내용을 직접 다시 확인해야 합니다. 자세한 내용은 [`docs/REPORTING.md`](docs/REPORTING.md)를 참고하십시오.

## 취소 동작의 현재 한계

현재 전송 계층은 **동기 WinHTTP**입니다. 취소 버튼은 다음 반복, 다음 대상과 WinHTTP 호출 사이 단계는 중단하지만, 이미 연결·응답·본문 읽기 함수 안에서 블로킹된 현재 호출은 설정된 제한 시간이 끝나 반환된 뒤 취소 상태가 반영될 수 있습니다.

동기 요청 핸들을 다른 스레드에서 강제로 닫지 않습니다. 블로킹 호출의 즉시 취소는 향후 `WINHTTP_FLAG_ASYNC`와 상태 콜백 기반 비동기 전송 계층에서 구현할 예정입니다.

## 기본 사용 순서

1. `WLAN · 프록시` 탭에서 WLAN과 현재 사용자 프록시 설정을 확인합니다.
2. 필요하면 `승인 대상` 또는 `targets.local.json`에서 내부·외부 대상을 적용합니다.
3. `내부 · 외부 다운로드 측정` 탭에서 1회 기준 측정을 실행합니다.
4. `브라우저 관찰` 탭에서 실제 브라우저 다운로드 중 인터페이스 처리량을 비교합니다.
5. `반복 측정` 탭에서 예열 1회와 본 측정 3회를 실행해 중앙값과 편차를 확인합니다.
6. `로컬 보고서`와 `반복 보고서` 탭에서 필요한 산출물을 저장합니다.

각 외부 URL에는 대상별 최대 수신량이 적용됩니다. 외부 URL 수, 예열과 반복 횟수를 고려한 최대 예상 수신량을 확인한 뒤 실행하십시오.

## 자동 검증

GitHub Actions는 실제 외부 사이트, 회사 프록시, PAC/WPAD 또는 사내 서버에 접속하지 않습니다. Windows runner와 `127.0.0.1` 합성 서버·합성 프록시로 다음을 확인합니다.

- .NET 10 Release 빌드
- Native WLAN API 및 오류 경계
- 수동 프록시·PAC·WPAD와 407 인증 상태 머신
- 내부 DIRECT·합성 외부 PROXY 다운로드
- 수신량 상한, 리다이렉트, 시간초과와 협력적 취소
- 승인 대상 JSON과 Core 실행 경계
- 브라우저 관찰 카운터 계산
- 구조화 보고서·마스킹·CSV/HTML 주입 방지
- 반복 측정 중앙값·편차·신뢰도
- Portable ZIP·single-file EXE 패키지, 금지 파일과 SHA-256

## 실제 환경 검증

다음은 사용자의 실제 Windows 11·Aruba WLAN·회사 프록시 환경에서 확인해야 합니다.

- 실제 무선 NIC 선택과 WLAN 반환값
- Aruba 환경의 SSID·BSSID·RSSI·채널·PHY·링크 속도
- 실제 수동 프록시·바이패스·PAC·WPAD 결과
- 실제 Negotiate/NTLM 407 인증
- TLS 검사와 회사 루트 인증서
- 내부 기준 서버의 DIRECT 경로와 충분한 성능
- 외부 승인 URL의 정책·캐시·다운로드 안정성
- GPO·EDR·SmartScreen 동작
- 생성 보고서의 사내 식별정보 마스킹

체크리스트는 [`docs/RELEASE_VALIDATION.md`](docs/RELEASE_VALIDATION.md)에 있습니다.

## 개발 검증

```powershell
dotnet restore .\WlanLivePathTester.sln
dotnet build .\WlanLivePathTester.sln -c Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-release.ps1 -Configuration Release
```

## 배포물

사전 릴리스에는 다음 네 파일만 게시합니다.

```text
WlanLivePathTester-win-x64-portable.zip
WlanLivePathTester-win-x64-single-file.exe
SHA256SUMS.txt
THIRD_PARTY_NOTICES.md
```

일반 사용에는 Portable ZIP을 권장합니다. 현재 배포물은 상용 Authenticode 인증서로 서명하지 않았으므로 Release 출처와 SHA-256을 확인하고 회사 보안 정책을 따르십시오.

[GitHub Releases](https://github.com/sebia1993/wlan-live-path-tester-ko/releases)

## 라이선스

프로젝트 자체 코드는 MIT 라이선스입니다. 제3자 코드나 바이너리는 현재 포함하지 않습니다.
