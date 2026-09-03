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

**M0 저장소 기반과 M1 Native WLAN 상태 수집은 완료됐고, M2 대상 URL별 프록시 경로 판정을 구현했습니다.**

현재 코드에 포함된 기능:

- 외부 통신 없이 Windows Native WLAN API로 무선 인터페이스 열거
- 현재 SSID, BSSID, RSSI, 신호 품질, 채널, 중심 주파수
- PHY 규격, Rx/Tx PHY 링크 속도, 인증·암호화 방식
- 무선 미연결, 인터페이스 없음, 접근 거부, WLAN 서비스 미실행, 부분 정보 실패 구분
- 현재 사용자 프록시 설정의 존재 여부를 로컬 WinHTTP API로 확인
- 대상 URL별 수동 프록시·바이패스·WPAD·PAC 경로 판정
- WPAD 실패 후 명시적 PAC, 이후 수동 프록시로 제한적인 fallback
- PAC/WPAD 취득 중 Windows 통합 인증이 필요할 때 한 번만 자동 로그온 재시도
- 내부망은 DIRECT, 외부망은 PROXY를 기대하는 경로 일치 판정
- 실제 프록시 주소와 PAC URL을 표시하지 않는 WPF 결과 화면
- 실제 사내 값이 없는 합성 자체 점검과 Windows CI

프록시 경로 확인은 사용자가 버튼을 눌렀을 때만 실행됩니다. 수동 설정만 있으면 로컬 판정으로 끝나지만, PAC/WPAD 환경에서는 회사 내부 PAC 파일 조회·WPAD 탐색·PAC 스크립트가 요구하는 DNS 확인이 발생할 수 있습니다. 이 단계에서는 대상 외부 사이트의 파일 본문을 내려받지 않습니다.

아직 구현되지 않은 기능:

- 실제 HTTP 요청의 `407 Proxy Authentication Required` 처리
- 내부·외부 다운로드 처리량 측정
- 브라우저 다운로드 처리량 관찰
- 완성형 로컬 보고서와 정식 릴리스

현재 WLAN 및 프록시 구현은 GitHub Windows 빌드로 코드 경계를 검증했지만, 실제 Windows 11 무선 어댑터·회사 PAC/WPAD·GPO/EDR 환경의 동작은 별도 수동 검증이 필요합니다. 진행 상황은 [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md)에 기록합니다.

## 저장소 구조

```text
wlan-live-path-tester-ko/
├─ src/
│  ├─ WlanLivePathTester.App/       WPF 사용자 화면
│  ├─ WlanLivePathTester.Core/      측정 모델·검증·판정 규칙
│  └─ WlanLivePathTester.Windows/   WinHTTP·WLAN·IP Helper 경계
├─ tests/
│  ├─ WlanLivePathTester.SelfTest/  외부 패키지 없는 결정론적 자체 점검
│  └─ WlanLivePathTester.WindowsSmoke/ Windows API 로컬 smoke test
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

실제 Windows 11 수동 검증과 핵심 측정 기능이 완료되기 전에는 정식 릴리스를 만들지 않습니다.

## 라이선스

프로젝트 자체 코드는 MIT 라이선스입니다. 제3자 코드나 바이너리는 현재 포함하지 않습니다.
