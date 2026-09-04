# WLAN Live Path Tester KO

Windows 11에서 현재 무선랜 연결 상태, 내부망 다운로드 성능, 회사 프록시를 포함한 외부 사이트 다운로드 성능을 로컬에서 진단하는 도구입니다.

```text
무선 단말
  ├─ WLAN 링크: SSID, BSSID, RSSI, 채널, PHY, Rx/Tx 링크 속도
  ├─ 내부망: AP → 컨트롤러/터널 → 사내 유선 서버
  └─ 외부망: AP → 사내망 → 회사 프록시 → 외부 사이트/CDN
```

외부 결과는 인터넷 회선만의 순수 속도가 아니라 **회사 프록시와 외부 대상 서버·CDN을 포함한 체감 다운로드 처리량**입니다. 프록시 서버의 CPU·세션·큐·캐시·정책 로그에는 접근하지 않으므로 프록시 장애로 단정하지 않고 내부망, 복수 외부 대상, WLAN 링크와 브라우저 관찰 결과를 비교해 장애 범위를 좁힙니다.

## 고정 제품 경계

- AI, LLM, 로컬 AI와 외부 분석 API를 사용하지 않습니다.
- 텔레메트리, 자동 오류 전송, 자동 업데이트, 공인 IP·ISP·GeoIP 조회를 하지 않습니다.
- PCAP, 로그, WLAN 정보, 사용자 정보와 측정 결과를 외부로 업로드하지 않습니다.
- 외부 통신은 사용자가 명시적으로 시작한 HTTP/HTTPS `HEAD` 또는 `GET` 다운로드 측정에만 허용합니다.
- 외부 `POST`, `PUT`, `PATCH`, `DELETE`, WebSocket과 업로드 속도 측정을 제공하지 않습니다.
- 다운로드 본문은 파일로 저장하지 않고 고정 크기 메모리 버퍼에서 읽은 즉시 폐기합니다.
- 브라우저 쿠키, 저장된 웹 자격 증명과 Authorization 헤더를 읽지 않습니다.
- 결과는 로컬 JSON, CSV, 외부 리소스 없는 단일 HTML과 SHA-256으로 저장합니다.
- 근거가 부족하거나 충돌하면 `판단 불가` 또는 전용 실패 상태로 처리합니다.

자세한 경계는 [`docs/NETWORK_BOUNDARY.md`](docs/NETWORK_BOUNDARY.md), [`PRIVACY.md`](PRIVACY.md), [`SECURITY.md`](SECURITY.md)를 기준으로 합니다.

## 실행 환경

- Windows 11 x64
- C# / .NET 10 / WPF
- Windows Native WLAN API
- Windows WinHTTP
- Windows IP Helper·`NetworkInterface`
- 런타임 NuGet 패키지 0개
- Python 또는 별도 .NET 런타임 설치 불필요
- 관리자 권한 불필요

## 구현 기능

### WLAN 상태

- 현재 무선 인터페이스와 연결 상태
- SSID·BSSID
- RSSI·신호 품질
- 채널·중심 주파수·2.4/5/6GHz 밴드
- PHY 유형
- Rx/Tx PHY 링크 속도
- 인증·암호화 방식
- WLAN AutoConfig 서비스 중지, 권한 제한과 부분 조회 실패 구분

PHY 링크 속도는 단말과 AP 사이 협상값이며 실제 애플리케이션 처리량과 같지 않습니다.

### 로컬 인터페이스 환경과 WLAN NIC 대응

- 물리 Wi-Fi·Ethernet 후보
- Wi-Fi Direct·Hosted Network·SoftAP
- VPN·터널
- Hyper-V·VMware·VirtualBox·WSL·Docker
- Bluetooth PAN·Loopback·기타 가상 인터페이스
- 활성 기본 게이트웨이 보유 인터페이스 개수
- 유선·무선 기본 경로 동시 활성
- Native WLAN GUID와 로컬 Wi-Fi ID 정확 대응
- GUID를 사용할 수 없을 때 설명 완전 일치 보조 판정
- 중복 후보가 있으면 임의 선택하지 않고 모호성 표시

IP·게이트웨이·DNS·MAC 주소 원문은 인터페이스 구조화 보고서에 포함하지 않습니다. 전체 인터페이스 GUID도 보고서에 저장하지 않습니다.

관련 문서:

- [`docs/NETWORK_INTERFACE_CONTEXT.md`](docs/NETWORK_INTERFACE_CONTEXT.md)
- [`docs/WLAN_INTERFACE_CORRELATION.md`](docs/WLAN_INTERFACE_CORRELATION.md)
- [`docs/NETWORK_ADAPTER_DIAGNOSTICS.md`](docs/NETWORK_ADAPTER_DIAGNOSTICS.md)

### 회사 프록시 경로

- 현재 로그인 사용자의 수동 프록시·바이패스·PAC·WPAD 설정 확인
- 대상 URL별 `DIRECT`, `PROXY`, 판단 불가 구분
- HTTP 407에서 Negotiate 우선·NTLM 차선 Windows 통합 인증
- Basic·Digest·Passport 전용 프록시 거부
- 원격 서버 401과 프록시 407 분리
- 내부 대상은 DIRECT, 외부 대상은 PROXY 기대 경로를 요청 전에 검사
- 실제 프록시 주소·PAC URL·바이패스 원문 마스킹

### 내부·외부 다운로드 측정

- 선택적 HEAD 사전검사와 GET 스트리밍 다운로드
- 1~4개 병렬 스트림
- 대상별 최대 수신량과 제한 시간
- 외부 URL 최대 4개 순차 측정
- 평균 Mbps, 1초 구간 Mbps, TTFB와 전체 소요시간
- HTTP 상태, 완료 스트림, 리다이렉트와 경로 일치 여부
- 제한된 응답 메타데이터: `Age`, `Via`, `Cache-Status`, `X-Cache`, `Content-Length`, `Content-Range`
- 모든 리다이렉트 URL과 기대 프록시 경로 재검사
- 외부 HTTPS→HTTP 다운그레이드 차단
- 외부 로컬·사설 주소와 미승인 교차 호스트 리다이렉트 차단

### 승인 대상 설정

선택적으로 로컬 JSON을 배포해 내부 1개와 외부 1~4개의 승인 URL 및 실행 제한을 고정할 수 있습니다.

- 사용자·Portable `targets.local.json`
- 관리자 `%ProgramData%\WLAN Live Path Tester KO\targets.json`
- 알 수 없는 JSON 속성, 주석과 trailing comma 거부
- 최대 1MiB·엄격 UTF-8·reparse point 거부
- 내부 대상 기본 DIRECT·외부 대상 기본 PROXY
- 관리자 강제 정책에서 미등록 URL과 변경된 실행 제한을 Core 단계에서 차단
- 손상된 관리자 정책의 fail-closed 다운로드 차단
- 최초 호스트 또는 `allowedRedirectHosts`에 정확히 등록한 호스트만 리다이렉트 허용
- 실제 사내 설정 파일은 `.gitignore` 대상

설정 방법은 [`docs/TARGET_CONFIGURATION.md`](docs/TARGET_CONFIGURATION.md)를 참고하십시오.

### 브라우저 다운로드 관찰

이 모드는 프로그램 자체의 외부 요청을 만들지 않습니다. 사용자가 Edge·Chrome 등에서 직접 다운로드하는 동안 선택된 물리 Wi-Fi 인터페이스 전체 Rx/Tx 카운터와 WLAN 상태를 관찰합니다.

- 시작 전 3초 백그라운드 기준 수집
- 기준치 제외 평균·최고 수신 처리량
- RSSI·PHY 링크 속도·BSSID 변화 동시 기록
- 일시 정지·급락·카운터 재설정 감지
- Wi-Fi Direct·가상·VPN 후보 제외
- 관찰 시작 시 물리 Wi-Fi 카운터 ID 고정
- 후속 샘플에서 설명 또는 다른 활성 Wi-Fi로 fallback 금지
- 같은 NIC의 BSSID 변경은 로밍으로 기록하고 계속 관찰
- 다른 물리 Wi-Fi로 변경되면 `AdapterChanged`
- 고정 NIC 카운터를 읽지 못하면 `AdapterUnavailable`
- 카운터 공급자가 다른 ID를 반환하면 `CounterProviderMismatch`
- 사용자 중지는 별도 `Canceled` 상태

이 값은 브라우저 프로세스 한 개가 아니라 고정된 Wi-Fi 인터페이스 전체 트래픽입니다. 자세한 내용은 [`docs/BROWSER_OBSERVATION.md`](docs/BROWSER_OBSERVATION.md)를 참고하십시오.

### 반복 측정과 대표값

- 선택적 예열 1회
- 본 측정 1~5회
- 대상별 순차 실행과 측정 간 대기
- 예열을 제외한 본 측정 중앙값
- 최소·최대·평균·표준편차·변동계수
- 중앙값에 가장 가까운 대표 회차
- 성공·실패·미완료 횟수 구분
- 수신량·측정 시간·스트림 완료·캐시·편차에 따른 Low/Medium/High 신뢰도
- 대상 하나와 작업 전체 최대 예상 수신량 각각 2GiB 제한

자세한 내용은 [`docs/REPEATED_MEASUREMENT.md`](docs/REPEATED_MEASUREMENT.md)를 참고하십시오.

### 로컬 보고서

다음 로컬 보고서를 제공합니다.

```text
일반 통합 진단 보고서
반복 측정 전용 보고서
인터페이스 환경 보고서
어댑터 선택·VPN·가상 NIC 진단 보고서
```

각 보고서는 가능한 경우 다음 묶음으로 생성합니다.

```text
JSON
CSV
외부 리소스 없는 단일 HTML
SHA256SUMS.txt
```

- SSID·BSSID·MAC·IP·이메일·Windows 사용자 경로·URL 호스트와 쿼리 마스킹
- 전체 인터페이스 GUID 제외 또는 짧은 SHA-256 지문 사용
- CSV 수식 주입 방지
- HTML 출력 인코딩과 Content Security Policy
- 외부 JavaScript·CSS·웹폰트·이미지·iframe 없음
- 자동 업로드 없음

마스킹은 보조 수단이므로 회사 밖으로 공유하기 전에는 내용을 직접 다시 확인해야 합니다. 자세한 내용은 [`docs/REPORTING.md`](docs/REPORTING.md)를 참고하십시오.

## 기본 사용 순서

1. `인터페이스 환경`과 `WLAN NIC 대응`에서 실제 물리 Wi-Fi와 유선·VPN·가상 NIC 혼재를 확인합니다.
2. `WLAN · 프록시`에서 WLAN 링크와 현재 사용자 프록시 설정을 확인합니다.
3. 필요하면 승인 대상 설정에서 내부·외부 URL을 적용합니다.
4. 내부 DIRECT와 외부 PROXY 다운로드를 각각 1회 측정합니다.
5. 반복 측정에서 예열과 본 측정 중앙값·편차를 확인합니다.
6. 브라우저 관찰에서 실제 사용자 다운로드의 고정 Wi-Fi 처리량을 비교합니다.
7. 필요한 로컬 보고서를 생성하고 민감정보를 다시 확인합니다.

각 외부 URL에는 대상별 최대 수신량이 적용됩니다. 외부 URL 수, 예열과 반복 횟수를 고려한 최대 예상 수신량을 확인한 뒤 실행하십시오.

## 결과 해석 예

| 내부망 | 복수 외부 대상 | 브라우저 관찰 | 우선 확인 |
|---|---|---|---|
| 정상 | 모두 낮음 | 낮음 | 프록시·외부 공통 경로·인터넷 경계 |
| 정상 | 한 대상만 낮음 | 다른 대상 정상 | 해당 사이트·CDN·외부 개별 경로 |
| 낮음 | 모두 낮음 | 낮음 | WLAN 링크·터널·사내 유선 경로 |
| 프로그램 외부 측정 실패 | 브라우저 관찰 정상 | 정상 | 브라우저 전용 SSO·인증 캐시·정책 차이 |
| `AdapterChanged` | 판단 중단 | 판단 중단 | 내장·USB Wi-Fi 전환 및 Windows 라우팅 |
| `CounterProviderMismatch` | 판단 중단 | 판단 중단 | WLAN ID·카운터 공급자·드라이버 상태 |

단일 결과만으로 특정 AP, 컨트롤러, 프록시 또는 회선 장애를 확정하지 않습니다.

## 다운로드 측정 취소의 현재 한계

현재 다운로드 전송 계층은 동기 WinHTTP입니다. 취소 버튼은 요청 전·요청 사이·반복 사이·대상 사이에서 반영되지만, 이미 WinHTTP 연결·응답·본문 읽기 함수 안에서 블로킹된 현재 호출은 설정된 제한 시간이 끝난 뒤 취소 상태가 반영될 수 있습니다.

동기 요청 핸들을 다른 스레드에서 강제로 닫지 않습니다. 블로킹 호출의 안전한 즉시 취소는 향후 `WINHTTP_FLAG_ASYNC`와 상태 콜백 기반 전송 계층에서 구현합니다.

브라우저 관찰은 외부 요청을 만들지 않으며 사용자 취소 또는 인터페이스 전용 종료 상태를 즉시 반영합니다.

## 자동 검증

GitHub Actions는 실제 외부 사이트, 회사 프록시, PAC/WPAD 또는 사내 서버에 접속하지 않습니다. Windows runner와 `127.0.0.1` 합성 서버·프록시로 다음을 확인합니다.

- .NET 10 Release 빌드와 경고의 오류 처리
- Native WLAN API·identity와 오류 경계
- 물리 Wi-Fi·VPN·가상 NIC 분류와 모호성
- 수동 프록시·PAC·WPAD와 407 인증 상태 머신
- 내부 DIRECT·합성 외부 PROXY 다운로드
- 수신량 상한·리다이렉트·시간초과·협력적 취소
- 승인 대상 JSON과 관리자 Core 정책
- 브라우저 관찰 카운터 계산과 고정 ID 경계
- `AdapterChanged`·`AdapterUnavailable`·`CounterProviderMismatch` 보고서 매핑
- 구조화 보고서·마스킹·CSV/HTML 주입 방지
- 반복 측정 중앙값·편차·신뢰도
- Portable ZIP·single-file EXE, 금지 파일과 SHA-256

## 실제 환경 검증

다음은 실제 Windows 11·Aruba WLAN·회사 프록시 환경에서 확인해야 합니다.

- 실제 무선 NIC 선택과 Native WLAN 반환값
- Aruba 환경의 SSID·BSSID·RSSI·채널·PHY·링크 속도
- 내장 Wi-Fi·USB Wi-Fi·유선·VPN·가상 NIC 혼재
- 같은 NIC의 BSSID 로밍과 물리 NIC 전환 구분
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

일반 사용에는 Portable ZIP을 권장합니다. 현재 배포물은 상용 Authenticode 인증서로 서명하지 않았으므로 GitHub Release 출처와 SHA-256을 확인하고 회사 보안 정책을 따르십시오.

[GitHub Releases](https://github.com/sebia1993/wlan-live-path-tester-ko/releases)

## 라이선스

프로젝트 자체 코드는 MIT 라이선스입니다. 제3자 코드나 바이너리는 현재 포함하지 않습니다.
