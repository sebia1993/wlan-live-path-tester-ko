# 게시된 GitHub Release 재검증

## 목적

빌드 직후의 로컬 패키지를 검사하는 것만으로는 GitHub Release에 실제로 게시된 파일의 바이트까지 다시 확인했다고 볼 수 없습니다.

`Published Release Verification`은 공개된 사전 릴리스의 네 자산을 GitHub에서 다시 내려받아 다음 경계를 확인합니다.

```text
검증된 main
   ↓ 빌드·패키지 검사
GitHub Release 업로드
   ↓ 다시 다운로드
GitHub digest + SHA256SUMS + ZIP + BUILD_INFO 재검증
```

## 자동 실행

워크플로 파일:

```text
.github/workflows/published-release-verify.yml
```

다음 경우 실행됩니다.

- GitHub Release가 `published` 상태가 됐을 때
- Actions 화면에서 `workflow_dispatch`로 태그를 직접 입력했을 때
- 검증 스크립트나 관련 워크플로가 변경된 Pull Request에서 기준 사전 릴리스로 회귀 검증할 때

GitHub 커넥터나 로컬 환경에서 `workflow_dispatch`를 직접 호출하기 어려운 경우 다음 형식의 일회성 브랜치를 사용할 수 있습니다.

```text
verify-trigger/v0.1.0-alpha.5
```

브랜치 push는 `published-release-verify-branch-trigger.yml`을 실행하고, 실제 검증 워크플로는 항상 `main`의 스크립트를 사용합니다.

## 검증 항목

### Release 상태

- 입력한 태그와 Release 태그 일치
- `draft=false`
- `prerelease=true`
- 정확히 네 개의 승인 자산만 존재

```text
WlanLivePathTester-win-x64-portable.zip
WlanLivePathTester-win-x64-single-file.exe
SHA256SUMS.txt
THIRD_PARTY_NOTICES.md
```

### 게시 자산 바이트

- `gh release download`로 네 자산을 새 임시 폴더에 다시 다운로드
- 다운로드된 파일 목록이 Release 자산 목록과 완전히 일치
- 각 파일 크기가 GitHub Release API의 `size`와 일치
- 각 파일의 로컬 SHA-256이 GitHub Release API의 `digest`와 일치
- `SHA256SUMS.txt`가 정확히 ZIP·EXE·제3자 고지 세 파일을 포함
- `SHA256SUMS.txt`의 선언값과 다시 다운로드한 파일의 SHA-256 일치

### 태그와 빌드 원본

- 태그가 경량 태그이며 직접 commit을 가리킴
- 태그 commit SHA가 40자리 Git SHA 형식
- Portable ZIP의 `BUILD_INFO.txt` 버전이 태그에서 `v`를 제거한 버전과 일치
- `BUILD_INFO.txt`의 `SourceRevision`이 태그 commit SHA와 일치
- `RuntimeIdentifier=win-x64`
- `SelfContained=true`

### 실행 파일

- Portable EXE와 single-file EXE에 Windows PE `MZ` 헤더 존재
- single-file EXE `ProductVersion`이 태그 버전으로 시작
- Authenticode 상태를 로그에 기록

현재 상용 코드 서명 인증서가 없으므로 `NotSigned` 자체는 실패 조건이 아닙니다. 인증서가 확보되면 서명 상태를 필수 조건으로 강화합니다.

### Portable ZIP

- 중복 entry 없음
- 절대 경로 없음
- 드라이브 경로 없음
- `..` 경로 순회 없음
- PDB·PCAP·ETL·EVTX·HAR·DMP 없음
- 실제 결과·로그·캡처 디렉터리 없음
- 실제 `targets.json` 또는 `*.local.json` 없음
- 필수 EXE·DLL·README·LICENSE·START_HERE·운영 문서 포함

## 저장과 개인정보 경계

- 다시 내려받은 파일은 임시 폴더에서만 검증하고 작업 종료 시 삭제합니다.
- workflow artifact로 다시 업로드하지 않습니다.
- 실제 사내 URL, 프록시 주소, PAC URL, SSID, BSSID, 로그와 측정 결과를 사용하지 않습니다.
- 외부 통신은 GitHub Release 자산 다운로드와 GitHub API 메타데이터 확인에만 한정됩니다.
- 프로그램 자체의 제품 통신 경계에는 영향을 주지 않습니다.

## 실패 시 처리

다음 상황이면 해당 Release를 신뢰 가능한 배포물로 안내하지 않습니다.

- 자산 이름 또는 개수 불일치
- GitHub digest와 다운로드 파일 해시 불일치
- `SHA256SUMS.txt` 불일치
- 태그 commit과 `BUILD_INFO.txt` SourceRevision 불일치
- ZIP 경로 순회 또는 금지 파일
- 버전·PE 헤더·ProductVersion 불일치

이미 공개된 Release에서 실패하면 자동으로 파일을 교체하거나 태그를 이동하지 않습니다. 원인을 수정한 새 버전을 발행합니다.

## 로컬 실행

GitHub CLI 인증이 가능한 Windows PowerShell에서 다음처럼 실행할 수 있습니다.

```powershell
$env:GH_TOKEN = '<read-only token or GitHub Actions token>'
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\verify-published-release.ps1 `
  -Tag v0.1.0-alpha.5 `
  -Repository sebia1993/wlan-live-path-tester-ko
```

검증에 내려받은 파일을 보존해야 하는 경우 `-DownloadRoot`를 명시합니다. 기본값은 임시 폴더이며 성공·실패 후 정리됩니다.
