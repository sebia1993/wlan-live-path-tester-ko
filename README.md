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

현재는 **M0 저장소 기반 구축 단계**입니다.

- 솔루션 및 프로젝트 골격
- 네트워크·개인정보 경계 문서
- 합성 데이터 기반 자체 점검
- Windows CI
- 현재 사용자 프록시 설정을 로컬에서 확인하는 최소 WinHTTP 경계

실제 WLAN 수집, PAC/WPAD 대상별 경로 확인, 내부·외부 다운로드 속도 측정은 후속 마일스톤에서 구현합니다. 진행 상황은 [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md)에 기록합니다.

## 저장소 구조

```text
wlan-live-path-tester-ko/
├─ src/
│  ├─ WlanLivePathTester.App/       WPF 사용자 화면
│  ├─ WlanLivePathTester.Core/      측정 모델·검증·판정 규칙
│  └─ WlanLivePathTester.Windows/   WinHTTP·WLAN·IP Helper 경계
├─ tests/
│  └─ WlanLivePathTester.SelfTest/  외부 패키지 없는 결정론적 자체 점검
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

초기 검증이 끝나기 전에는 정식 릴리스를 만들지 않습니다.

## 라이선스

프로젝트 자체 코드는 MIT 라이선스입니다. 제3자 코드나 바이너리는 현재 포함하지 않습니다.
