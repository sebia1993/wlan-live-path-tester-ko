# 첫 사전 릴리스 생성

## 용도

`Manual Prerelease` GitHub Actions 워크플로는 저장소 기본 브랜치의 검증된 현재 커밋에서 새 사전 릴리스 태그와 GitHub Release를 한 번에 만듭니다.

GitHub API 또는 로컬 Git 명령으로 태그를 직접 만들기 어려운 경우에 사용합니다. 안정 버전은 실제 Windows 수동 검증 이후 일반 태그 릴리스 절차를 사용합니다.

## 입력 제한

사전 릴리스 태그만 허용합니다.

```text
v0.1.0-alpha.1
v0.1.0-beta.1
v0.1.0-rc.1
```

`v0.1.0` 같은 안정 태그는 이 워크플로에서 거부합니다.

## 동작 순서

1. 현재 `main`을 체크아웃합니다.
2. 태그 형식, 기존 태그와 기존 Release를 확인합니다.
3. 전체 솔루션·모든 smoke test·보안 감사를 실행합니다.
4. Portable ZIP과 단일 EXE를 생성합니다.
5. 패키지 구조, 경로 순회, 금지 파일, ProductVersion과 SHA-256을 검증합니다.
6. 모든 검사가 끝난 뒤에만 현재 `main` 커밋에 경량 태그를 생성합니다.
7. 네 개 자산을 포함한 draft prerelease를 생성합니다.
8. draft의 상태와 자산 이름을 검증합니다.
9. 검증된 draft를 공개하고 공개 상태를 다시 확인합니다.

## 재실행 경계

- 같은 이름의 Release가 있으면 덮어쓰지 않고 실패합니다.
- 태그가 이미 있고 현재 `main` 커밋을 가리키며 Release가 없다면 실패한 첫 실행의 재시도를 허용합니다.
- 태그가 다른 커밋을 가리키면 이동하거나 덮어쓰지 않습니다.
- 패키지 검증 전에 태그를 만들지 않습니다.
- 자산 검증 전에는 Release를 draft 상태로 유지합니다.

## 자산

```text
WlanLivePathTester-win-x64-portable.zip
WlanLivePathTester-win-x64-single-file.exe
SHA256SUMS.txt
THIRD_PARTY_NOTICES.md
```

이외의 workflow artifact는 보존하지 않습니다.

## 실행

GitHub 저장소의 Actions에서 `Manual Prerelease`를 선택하고 `Run workflow`에서 새 prerelease 태그를 입력합니다.

첫 배포 권장값:

```text
v0.1.0-alpha.1
```

완료 후 Release 페이지에서 prerelease 표시, 네 개의 자산, 파일 크기와 `SHA256SUMS.txt`를 확인합니다.

## 실제 검증

배포물 생성 성공은 실제 회사 환경 동작을 보증하지 않습니다. 내려받은 Portable ZIP 또는 단일 EXE는 [`RELEASE_VALIDATION.md`](RELEASE_VALIDATION.md)에 따라 Windows 11, WLAN, 고정 프록시·PAC·WPAD, Negotiate/NTLM, 내부·외부 승인 URL과 GPO·EDR 환경에서 사용자가 검증합니다.
