# 릴리스 프로세스

## 목적

이 문서는 Windows x64 self-contained 배포물을 동일한 절차로 만들고 검증하기 위한 개발자용 기준입니다. 실제 사용자 환경 검증은 [`RELEASE_VALIDATION.md`](RELEASE_VALIDATION.md)를 따릅니다.

## 릴리스 자산

정식 GitHub Release에는 다음 네 파일만 게시합니다.

```text
WlanLivePathTester-win-x64-portable.zip
WlanLivePathTester-win-x64-single-file.exe
SHA256SUMS.txt
THIRD_PARTY_NOTICES.md
```

일반 Pull Request에서는 빌드 자산을 업로드하지 않습니다. 패키지 구조와 해시는 CI 안에서 생성·검증한 뒤 작업 종료 시 폐기합니다.

## 버전 규칙

태그는 Semantic Versioning을 사용합니다.

```text
v0.1.0-alpha.1
v0.1.0-beta.1
v0.1.0-rc.1
v0.1.0
```

- `alpha`, `beta`, `rc`가 포함된 태그는 GitHub Pre-release로 게시합니다.
- 안정 태그는 실제 Windows 11·회사 프록시·WLAN 수동 검증이 끝난 뒤 사용합니다.
- 어셈블리와 파일 버전은 `major.minor.patch.0` 숫자로 기록합니다.
- ProductVersion/InformationalVersion에는 prerelease 문자열을 포함한 전체 버전을 기록합니다.

## 전체 검증

저장소 루트에서 다음을 실행합니다.

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
- 저장소 감사
- 네트워크 통신 경계 감사

자동 테스트는 실제 회사 프록시·WLAN·내부 서버·외부 사이트를 사용하지 않습니다.

## 로컬 배포물 생성

예시:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 `
  -Version 0.1.0-alpha.1 `
  -OutputRoot .\artifacts\release
```

패키지 검증:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-package.ps1 `
  -Version 0.1.0-alpha.1 `
  -OutputRoot .\artifacts\release
```

`build-release.ps1`은 기존 출력 디렉터리를 삭제하고 새 자산 네 개만 생성합니다. 임시 publish·staging 디렉터리는 작업 종료 시 제거합니다.

## Portable ZIP 구성

ZIP 루트에는 실행 파일과 self-contained .NET 런타임 파일이 위치합니다.

필수 항목:

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

다음은 포함하지 않습니다.

- PDB
- 실제 `targets.json` 또는 `*.local.json`
- PCAP/PCAPNG, ETL, EVTX, HAR, 덤프
- 로그, 결과, 보고서와 캡처 디렉터리
- 실제 사내 주소와 사용자 정보

## 단일 EXE

단일 파일은 다음 조건으로 게시합니다.

- `win-x64`
- self-contained
- `PublishSingleFile=true`
- `IncludeNativeLibrariesForSelfExtract=true`
- trimming 비활성화
- PDB 비포함
- WPF 리소스와 네이티브 런타임 포함

단일 파일은 실행 시 네이티브 구성요소를 임시 위치에 추출할 수 있습니다. EDR 호환성 때문에 Portable ZIP을 기본 권장 형태로 유지합니다.

## 패키지 자동 검사

`test-release-package.ps1`은 다음을 검사합니다.

- 정확히 네 개의 최종 자산만 존재
- ZIP과 EXE가 비정상적으로 작지 않음
- EXE의 Windows PE `MZ` 헤더
- ZIP 필수 self-contained 파일
- ZIP 경로 순회·절대 경로·드라이브 경로 없음
- 금지 확장자·데이터 디렉터리·로컬 설정 없음
- 중복 ZIP 엔트리 없음
- `BUILD_INFO.txt` 버전과 `win-x64`, self-contained 표기
- 단일 EXE ProductVersion
- SHA-256 목록의 형식·개수·재계산 일치
- Authenticode 상태 기록

코드 서명이 없다는 이유만으로 패키지 검사를 실패시키지는 않지만 릴리스 노트와 사용자 문서에 미서명 상태를 명확히 표시합니다.

## GitHub Actions

### Release Package CI

Pull Request에서 다음을 수행합니다.

1. 전체 릴리스 검증
2. CI용 버전으로 Portable ZIP과 단일 EXE 생성
3. 패키지 구조와 SHA-256 검증
4. 자산 업로드 없이 작업 종료

### Windows Release

`v*` 태그를 push하면 다음을 수행합니다.

1. 태그 형식과 버전 파싱
2. 전체 릴리스 검증
3. 실제 버전의 배포물 생성
4. 패키지 재검증
5. 같은 태그의 Release가 없는지 확인
6. prerelease 여부 결정
7. GitHub Release 생성과 네 자산 업로드

워크플로는 `contents: write` 외의 불필요한 권한을 사용하지 않습니다. 실행 파일은 외부 빌드 서비스나 서명 서비스로 전송하지 않습니다.

## 태그 생성 전 확인

- [ ] `main`의 Windows CI 성공
- [ ] Release Package CI 성공
- [ ] `CHANGELOG.md`와 릴리스 설명 검토
- [ ] 실제 사내 값이 저장소에 없는지 확인
- [ ] prerelease인지 stable인지 결정
- [ ] stable이라면 실제 Windows 수동 검증 완료

## 실패 처리

- 빌드 또는 테스트가 실패하면 Release를 생성하지 않습니다.
- 패키지 해시·필수 파일·금지 파일 검사 실패 시 Release를 생성하지 않습니다.
- 같은 태그의 Release가 이미 있으면 덮어쓰지 않고 실패합니다.
- 일부 자산만 게시된 상태가 발견되면 새 태그로 재빌드하기 전에 기존 Release를 수동으로 검토합니다.
- 실제 사내 값이 포함된 경우 태그와 Release를 즉시 비공개 또는 삭제하고 노출 범위를 확인합니다.
