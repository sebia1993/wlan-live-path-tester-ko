# 릴리스 프로세스

## 릴리스 자산

GitHub Release에는 다음 네 파일만 게시합니다.

```text
WlanLivePathTester-win-x64-portable.zip
WlanLivePathTester-win-x64-single-file.exe
SHA256SUMS.txt
THIRD_PARTY_NOTICES.md
```

일반 Pull Request에서는 바이너리 artifact를 업로드하지 않습니다. CI 안에서 패키지를 생성·검증한 뒤 작업 종료 시 폐기합니다.

## 버전 규칙

Semantic Versioning 태그를 사용합니다.

```text
v0.1.0-alpha.1
v0.1.0-beta.1
v0.1.0-rc.1
v0.1.0
```

- 하이픈이 있는 버전은 GitHub Pre-release로 게시합니다.
- 안정 태그는 실제 Windows 11·회사 프록시·WLAN 수동 검증 이후 사용합니다.
- AssemblyVersion·FileVersion은 `major.minor.patch.0`으로 기록합니다.
- ProductVersion에는 prerelease 문자열을 포함한 전체 버전을 기록합니다.

## 전체 검증

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-release.ps1
```

검증 범위:

- 솔루션 Restore 및 Release 빌드
- Core 결정론적 SelfTest
- Native WLAN API smoke test
- WinHTTP 407 인증 smoke test
- 내부·외부 다운로드 측정 smoke test
- 브라우저 관찰 smoke test
- 로컬 보고서 smoke test
- 저장소 및 네트워크 통신 경계 감사

자동 테스트는 실제 회사 프록시·WLAN·내부 서버·외부 사이트를 사용하지 않습니다.

## 로컬 패키지 생성

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 `
  -Version 0.1.0-alpha.1 `
  -OutputRoot .\artifacts\release

powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-package.ps1 `
  -Version 0.1.0-alpha.1 `
  -OutputRoot .\artifacts\release
```

빌드 스크립트는 출력 디렉터리를 새로 만들고 정확히 네 개의 최종 자산만 남깁니다. 임시 publish·staging 디렉터리는 종료 시 제거합니다.

## Portable ZIP

ZIP 루트에는 다음 항목이 포함됩니다.

```text
WlanLivePathTester.exe
WlanLivePathTester.dll
WlanLivePathTester.deps.json
WlanLivePathTester.runtimeconfig.json
coreclr.dll
hostfxr.dll
START_HERE.txt
BUILD_INFO.txt
README.md
LICENSE
THIRD_PARTY_NOTICES.md
docs/
config/targets.example.json  # 존재할 때만 포함
```

포함 금지:

- PDB
- 실제 `targets.json`, `*.local.json`
- PCAP/PCAPNG, ETL, EVTX, HAR, DMP
- 로그·결과·보고서·캡처 디렉터리
- 실제 사내 주소와 사용자 정보

## 단일 EXE

다음 조건으로 게시합니다.

- `win-x64`
- self-contained
- `PublishSingleFile=true`
- `IncludeNativeLibrariesForSelfExtract=true`
- trimming 비활성화
- PDB 비포함

단일 EXE는 네이티브 구성요소를 임시 위치에 추출할 수 있으므로 EDR 호환성 측면에서 Portable ZIP을 기본 권장 형태로 유지합니다.

## 패키지 검사

`test-release-package.ps1`은 다음을 확인합니다.

- 정확히 네 개의 최종 자산
- ZIP·EXE 최소 크기와 Windows PE `MZ` 헤더
- ZIP의 self-contained 필수 파일
- 선행 또는 중간 `..` 경로 순회, 절대 경로, 드라이브 경로와 중복 엔트리 없음
- 금지 확장자·데이터 디렉터리·로컬 설정 없음
- `BUILD_INFO.txt`의 버전·win-x64·self-contained 표기
- 단일 EXE ProductVersion
- SHA-256 형식·개수·재계산 일치
- Authenticode 상태 기록

현재 코드 서명 인증서가 없으므로 미서명 자체를 실패로 처리하지는 않지만 사용자 문서와 릴리스 노트에 명확히 표시합니다.

## GitHub Actions

### Release Package CI

Pull Request에서 전체 검증, Portable ZIP·단일 EXE 생성과 패키지 검사를 수행합니다. 자산은 업로드하지 않습니다.

### Windows Release

`v*` 태그를 push하면 다음 순서로 동작합니다.

1. 태그 형식과 버전 확인
2. 전체 소스 및 smoke test 검증
3. 실제 버전 패키지 생성
4. 패키지 구조와 SHA-256 재검증
5. 기존 Release가 없는지 확인
6. 네 자산을 포함한 draft Release 생성
7. draft 자산 목록 검증
8. 검증된 draft를 공개
9. 공개 Release의 자산 목록 재확인

워크플로는 릴리스 생성에 필요한 `contents: write`만 사용합니다. 외부 빌드·서명·분석 서비스에 실행 파일을 전송하지 않습니다.

## 태그 전 확인

- [ ] `main` Windows CI 성공
- [ ] Release Package CI 성공
- [ ] 실제 사내 값이 저장소에 없는지 확인
- [ ] prerelease 또는 stable 결정
- [ ] stable이라면 `RELEASE_VALIDATION.md` 수동 검증 완료

## 실패 처리

- 빌드·테스트·패키지 검사가 실패하면 Release를 생성하지 않습니다.
- 기존 Release는 자동으로 덮어쓰지 않습니다.
- draft 생성 후 자산 확인이 실패하면 공개하지 않습니다.
- 일부 자산만 있는 draft가 남으면 자동 재실행 전에 먼저 검토합니다.
- 실제 사내 정보가 발견되면 해당 태그·Release를 즉시 비공개 또는 삭제하고 노출 범위를 확인합니다.
