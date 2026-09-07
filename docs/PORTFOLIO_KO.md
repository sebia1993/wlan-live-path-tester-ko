# WLAN 경로 비교: 설계와 검증 읽기 안내

이 프로젝트는 Windows 단말에서 얻을 수 있는 증거로 무선·내부망·프록시·외부 서비스의 조사 범위를 좁힙니다. 성능 개선율, 장애 복구 시간 단축, 실제 사내 배포 규모는 측정된 근거가 없으므로 성과로 제시하지 않습니다.

## 사례: Wi-Fi는 연결됐지만 다운로드가 느릴 때

1. WLAN 연결과 PHY 링크 속도를 확인합니다. PHY 값은 AP와 단말 사이 협상값입니다.
2. 내부 DIRECT 결과를 기준으로 삼기 전에 OS가 선택한 NIC가 WLAN과 일치하는지 확인합니다. 유선·VPN으로 나간 결과라면 무선 기준값의 의미가 달라집니다.
3. 외부 PROXY 기대 경로와 HTTP 결과를 확인합니다. 407 인증 실패는 낮은 처리량과 다른 문제입니다.
4. 복수 대상·반복 회차·브라우저 관찰을 대조하고, 관찰 사실과 다음 점검 위치를 로컬 보고서로 전달합니다.

브라우저 관찰은 선택한 NIC 전체 카운터이므로 브라우저 한 프로세스의 속도로 해석하지 않습니다. 외부 결과에는 프록시·대상 서버·CDN·캐시 영향이 함께 들어 있습니다.

## 설계 판단에서 구현까지

| 질문 | 설계와 코드 | 회귀 증거 |
|---|---|---|
| 측정이 실제로 Wi-Fi를 사용했는가? | [RouteWlanCorrelationEvaluator](../src/WlanLivePathTester.Core/Routing/RouteWlanCorrelationEvaluator.cs): 정확한 GUID와 OS 경로 결과 비교, 식별 불가 상태 분리 | [보고서·라우팅 smoke 구성](../.github/workflows/report-ci.yml) |
| 인증 실패를 회선 속도 문제로 표시하는가? | [DiagnosisEngine](../src/WlanLivePathTester.Core/Rules/DiagnosisEngine.cs), [ProxyAuthenticationPolicy](../src/WlanLivePathTester.Core/Http/ProxyAuthenticationPolicy.cs): 경로·407과 성능 판정 분리 | [ProxyAuthSmoke](../tests/WlanLivePathTester.ProxyAuthSmoke/Program.cs) |
| PAC/WPAD 실패가 임의 DIRECT 우회로 이어지는가? | [프록시 fallback ADR](adr/0006-proxy-resolution-fallback-order.md), [프록시 선택 계층](../src/WlanLivePathTester.Core/Proxy/) | [SelfTest](../tests/WlanLivePathTester.SelfTest/Program.cs), [ProxyBoundarySmoke](../tests/WlanLivePathTester.ProxyBoundarySmoke/Program.cs) |
| 대상 URL이나 리다이렉트가 허용 범위를 벗어나는가? | [TargetValidator](../src/WlanLivePathTester.Core/Security/TargetValidator.cs), [RedirectTargetValidator](../src/WlanLivePathTester.Core/Security/RedirectTargetValidator.cs) | [SelfTest](../tests/WlanLivePathTester.SelfTest/Program.cs), [InputBoundarySmoke](../tests/WlanLivePathTester.InputBoundarySmoke/Program.cs) |
| NIC 전환을 로밍과 혼동하는가? | [관찰 ID 고정 정책](../src/WlanLivePathTester.Core/Observation/ObservationInterfaceBindingPolicy.cs), [관찰 실행 계층](../src/WlanLivePathTester.Windows/Observation/BrowserObservationRunner.cs) | [ObservationSmoke](../tests/WlanLivePathTester.ObservationSmoke/Program.cs) |

Core는 판정·모델, Windows 계층은 Native WLAN·WinHTTP·IP Helper, WPF App은 사용자 흐름을 담당합니다. 전체 소스 진입점은 [src](../src/)입니다.

## 장비 없이 재현하기

.NET 10 SDK를 준비한 뒤 저장소 루트에서 다음을 실행합니다. 이 테스트는 Core만 참조하며 실제 장비나 외부 HTTP 대상에 접속하지 않습니다. 최초 SDK/복원 작업은 개발 도구의 공급망 통신입니다.

```powershell
dotnet run --project .\tests\WlanLivePathTester.SelfTest\WlanLivePathTester.SelfTest.csproj -c Release
```

확인할 출력은 테스트별 `PASS`와 마지막 `실패 0개`입니다. 사설 외부 주소·URL 사용자정보·미승인 리다이렉트 거부, 프록시 선택과 407 분리, 채널 변환을 읽어 보십시오. 이 명령만으로 WPF UI·WinHTTP·패키지가 검증되지는 않습니다.

Windows 전체 자동 검증 경로는 [Windows CI](../.github/workflows/ci.yml), [보고서 CI](../.github/workflows/report-ci.yml), [패키지 CI](../.github/workflows/release-package-ci.yml)입니다. 각 실행의 대상 commit SHA와 성공한 job을 함께 확인합니다.

## 공개 증거와 남은 검증

- [Releases](https://github.com/sebia1993/wlan-live-path-tester-ko/releases): 실행 ZIP, 버전, 배포 안내. 소스 ZIP과 실행 패키지를 구분합니다.
- [배포 검증 방법](PUBLISHED_RELEASE_VERIFICATION.md): 공개 자산·해시 검증의 범위입니다.
- [Windows 수동 매트릭스](WINDOWS_TEST_MATRIX.md): 실제 WLAN 드라이버, PAC/WPAD, Negotiate/NTLM, EDR/GPO 검증은 별도로 필요합니다.
- 동기 WinHTTP의 블로킹 호출은 취소 요청 후에도 설정된 제한 시간까지 기다릴 수 있습니다. [취소 경계](PROXY_RAW_INPUT_AND_CANCELLATION.md)를 운영 검토에 포함합니다.
- 근거가 부족할 때 특정 AP·프록시·회선이 원인이라고 확정하지 않습니다. 실제 장애 사례에서 판단 정확도와 조사 시간은 아직 별도 측정 대상입니다.
