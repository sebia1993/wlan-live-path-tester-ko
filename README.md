# WLAN Live Path Tester KO

Windows 11에서 현재 무선랜 연결 상태, 내부망 다운로드 성능, 회사 프록시를 경유한 외부 사이트 다운로드 성능을 로컬에서 진단하는 도구입니다.

## 제품 목표

이 프로젝트는 초급 네트워크 엔지니어가 다음 경로를 분리해서 확인할 수 있도록 설계합니다.

```text
무선 단말
  ├─ WLAN 링크 상태: SSID, BSSID, RSSI, 채널, PHY, Rx/Tx 링크 속도
  ├─ 내부망 경로: AP → 컨트롤러/터널 → 사내 유선 서버
  └─ 외부망 경로: AP → 사내망 → 회사 프록시 → 외부 사이트
```

외부망 결과는 인터넷 회선만의 속도가 아니라 **회사 프록시를 포함한 외부 서비스 체감 다운로드 속도**입니다. 프록시 서버 내부에 접근할 권한이 없으므로 프록시 자체 장애로 단정하지 않고, 내부망 및 복수 외부 대상의 결과를 비교해 장애 범위를 좁힙니다.

## 고정 원칙

- AI, LLM, 외부 분석 API를 사용하지 않습니다.
- 텔레메트리, 자동 업데이트, 공인 IP·ISP·GeoIP 조회를 하지 않습니다.
- PCAP, 로그, 측정 결과, WLAN 정보, 사용자 정보를 외부로 업로드하지 않습니다.
- 외부 통신은 사용자가 명시적으로 시작한 `HEAD` 또는 `GET` 다운로드 측정에만 허용합니다.
- 외부 `POST`, `PUT`, `PATCH`, `DELETE` 및 업로드 속도 측정은 제공하지 않습니다.
- 외부 다운로드 본문은 저장하지 않고 읽은 즉시 폐기합니다.
- 결과는 로컬 JSON, CSV, 단일 HTML로만 저장합니다.
- 근거가 부족하면 `판단 불가`로 처리하며 프록시 내부 상태를 추정하지 않습니다.
- 대상 PC에 별도 런타임이 없어도 실행할 수 있는 Windows x64 self-contained 배포를 목표로 합니다.

자세한 경계는 [`docs/NETWORK_BOUNDARY.md`](docs/NETWORK_BOUNDARY.md)와 [`PRIVACY.md`](PRIVACY.md)를 기준으로 합니다.

## 기술 구성

- Windows 11 x64
- C# / .NET 10
- WPF
- Windows Native WLAN API
- Windows WinHTTP
- Windows IP Helper API
- `System.Text.Json`
- 런타임 NuGet 패키지 0개 원칙

## 현재 상태

**M0 저장소 기반, M1 Native WLAN, M2 프록시 경로·407 인증을 완료했고 M3·M4 내부/외부 다운로드 측정 기능을 구현했습니다.**

현재 코드에 포함된 기능:

- 외부 통신 없이 Windows Native WLAN API로 무선 인터페이스와 현재 연결 정보 수집
- SSID, BSSID, RSSI, 신호 품질, 채널, 중심 주파수, PHY, Rx/Tx 링크 속도, 인증·암호화 표시
- 현재 사용자 프록시 설정의 존재 여부를 로컬 WinHTTP API로 확인
- 대상 URL별 수동 프록시·바이패스·WPAD·PAC 경로 판정
- WPAD → 명시적 PAC → 수동 프록시의 제한적 fallback
- HTTP 407에서 Negotiate 우선, NTLM 차선의 현재 Windows 사용자 통합 인증
- Basic·Digest·Passport 전용 프록시 거부, 원격 서버 401과 프록시 407 분리
- 내부망 대상은 DIRECT, 외부망 대상은 PROXY 경로를 요청 전에 강제
- 선택적 `HEAD` 사전검사와 `GET` 스트리밍 다운로드
- 1~4개 병렬 스트림과 전체 최대 수신량 상한
- 평균 Mbps, 1초 구간 Mbps, TTFB, HTTP 상태, 리다이렉트, 최종 URL 기록
- `Age`, `Via`, `Cache-Status`, `X-Cache`, `Content-Length`, `Content-Range` 등 선택 응답 메타데이터 기록
- 모든 리다이렉트 URL 재검증, 외부 HTTPS→HTTP 다운그레이드와 외부 로컬 주소 차단
- 다운로드 본문 파일 비저장 및 고정 크기 버퍼 즉시 폐기
- 내부 URL 1개와 외부 URL 최대 4개를 실행·취소·비교하는 WPF 화면
- 실제 프록시 주소와 PAC URL을 표시하지 않는 결과 화면
- 실제 사내 값이 없는 결정론적 자체 점검과 루프백 Windows smoke test

프로그램 시작과 WLAN·로컬 프록시 설정 확인만으로는 네트워크 요청을 만들지 않습니다. PAC/WPAD 경로 확인은 사용자가 경로 확인 버튼을 누른 경우에만 실행되고, 실제 다운로드는 사용자가 내부 또는 외부 측정 시작 버튼을 누른 경우에만 실행됩니다.

아직 구현되지 않은 기능:

- 브라우저 다운로드 중 Wi-Fi 인터페이스 처리량 관찰
- 완성형 규칙 기반 로컬 JSON·CSV·단일 HTML 보고서
- 정식 Portable ZIP·single-file 실행 파일·SHA-256 릴리스 파이프라인

현재 구현은 GitHub Windows runner와 `127.0.0.1` 합성 HTTP 서버·프록시로 코드 경계를 자동 검증합니다. 실제 Windows 11 무선 어댑터, 회사 PAC/WPAD, Negotiate/NTLM, TLS 검사, GPO/EDR 및 실제 내부·외부 경로는 사용자가 로컬 환경에서 별도 검증합니다. 진행 상황은 [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md)에 기록합니다.

## 측정 화면 사용 방식

1. `WLAN · 프록시` 탭에서 현재 WLAN과 프록시 설정 또는 특정 URL의 예상 경로를 확인합니다.
2. `내부 · 외부 다운로드 측정` 탭에서 최대 수신량, 제한 시간, 스트림 수, 리다이렉트 상한을 지정합니다.
3. 내부망 기준 서버 URL은 한 개 입력하고 `내부망 측정 시작`을 누릅니다.
4. 외부 승인 URL은 한 줄에 하나씩 최대 네 개 입력하고 `외부망 대상 순차 측정`을 누릅니다.
5. 진행 중 취소를 누르면 현재 WinHTTP 호출이 반환된 뒤 남은 단계와 대상을 중단합니다.

각 외부 URL에는 공통 설정의 최대 수신량이 개별 적용됩니다. 예를 들어 최대 100MB, 외부 URL 3개이면 최악의 경우 총 300MB를 수신할 수 있습니다.

자세한 측정 경계와 결과 해석은 [`docs/DOWNLOAD_MEASUREMENT.md`](docs/DOWNLOAD_MEASUREMENT.md)를 참고하십시오.

## 저장소 구조

```text
wlan-live-path-tester-ko/
├─ src/
│  ├─ WlanLivePathTester.App/       WPF 사용자 화면
│  ├─ WlanLivePathTester.Core/      측정 모델·검증·판정 규칙
│  └─ WlanLivePathTester.Windows/   WinHTTP·WLAN·IP Helper 경계
├─ tests/
│  ├─ WlanLivePathTester.SelfTest/         결정론적 Core 자체 점검
│  ├─ WlanLivePathTester.WindowsSmoke/     Windows API 로컬 smoke test
│  ├─ WlanLivePathTester.ProxyAuthSmoke/   루프백 WinHTTP·407 smoke test
│  └─ WlanLivePathTester.MeasurementSmoke/ 루프백 다운로드 측정 smoke test
├─ config/
│  └─ targets.example.json          커밋 가능한 합성 예시
├─ resources/
│  └─ rules/                        버전 고정 진단 규칙
├─ docs/
│  ├─ adr/                          설계 결정 기록
│  └─ 보안·측정·프록시 문서
└─ scripts/                         저장소·통신 경계 감사
```

## 개발 명령

```powershell
dotnet restore .\WlanLivePathTester.sln
dotnet build .\WlanLivePathTester.sln -c Release --no-restore
dotnet run --project .\tests\WlanLivePathTester.SelfTest\WlanLivePathTester.SelfTest.csproj -c Release --no-build
dotnet run --project .\tests\WlanLivePathTester.WindowsSmoke\WlanLivePathTester.WindowsSmoke.csproj -c Release --no-build
dotnet run --project .\tests\WlanLivePathTester.ProxyAuthSmoke\WlanLivePathTester.ProxyAuthSmoke.csproj -c Release --no-build
dotnet run --project .\tests\WlanLivePathTester.MeasurementSmoke\WlanLivePathTester.MeasurementSmoke.csproj -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\audit-repository.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\audit-network-boundary.ps1
```

## 배포 목표

```text
WlanLivePathTester-win-x64-portable.zip
WlanLivePathTester-win-x64-single-file.exe
SHA256SUMS.txt
THIRD_PARTY_NOTICES.md
```

정식 릴리스 파이프라인이 완료되기 전에는 Release 자산을 만들지 않습니다. 실제 환경 검증 결과와 사내 식별자는 공개 저장소에 원문으로 커밋하지 않습니다.

## 라이선스

프로젝트 자체 코드는 MIT 라이선스입니다. 제3자 코드나 바이너리는 현재 포함하지 않습니다.
