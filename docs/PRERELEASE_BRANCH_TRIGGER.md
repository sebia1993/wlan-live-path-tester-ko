# 사전 릴리스 트리거 브랜치

## 목적

`Prerelease Branch Trigger` 워크플로는 GitHub 커넥터나 로컬 Git 환경에서 `workflow_dispatch`를 직접 호출하기 어려울 때 사용합니다. `release-trigger/v…` 형식의 일회성 브랜치에 커밋이 push되면, 검증된 `Manual Prerelease` 워크플로를 `main` 기준으로 호출합니다.

이 브랜치는 릴리스 소스가 아닙니다. 실제 빌드·검증·태그 생성·GitHub Release 게시 작업은 `main`에서 실행되는 `Manual Prerelease` 워크플로가 담당합니다.

## 허용되는 브랜치 형식

사전 릴리스 버전만 허용합니다.

```text
release-trigger/v0.1.0-alpha.1
release-trigger/v0.1.0-beta.1
release-trigger/v0.1.0-rc.1
```

다음과 같은 안정 버전 브랜치는 정규식 검증에서 거부합니다.

```text
release-trigger/v0.1.0
```

## 동작 순서

1. `release-trigger/vX.Y.Z-prerelease` 브랜치에 push 이벤트가 발생합니다.
2. 브랜치 이름에서 실제 태그 이름 `vX.Y.Z-prerelease`를 추출합니다.
3. 브랜치 형식과 prerelease 표기를 검사합니다.
4. GitHub Actions의 `actions: write` 권한으로 `manual-prerelease.yml`을 `main` 기준으로 dispatch합니다.
5. 트리거 워크플로는 릴리스 바이너리나 태그를 직접 만들지 않습니다.
6. `Manual Prerelease`가 전체 소스·패키지 검증을 완료한 뒤 실제 태그와 draft prerelease를 만듭니다.
7. 정확한 네 개의 자산을 확인한 뒤 prerelease를 공개합니다.

## 첫 배포 예시

브랜치:

```text
release-trigger/v0.1.0-alpha.1
```

브랜치에 넣을 수 있는 일회성 마커 예시:

```text
This branch exists only to dispatch the verified manual prerelease workflow for v0.1.0-alpha.1.
```

브랜치를 만드는 것만으로 push 워크플로가 항상 실행된다고 가정하지 않습니다. 트리거를 확실히 발생시키기 위해 브랜치에 마커 파일 커밋을 하나 생성합니다.

## 권한 경계

트리거 워크플로의 권한은 다음 두 가지로 제한합니다.

```yaml
permissions:
  actions: write
  contents: read
```

- `actions: write`: `Manual Prerelease` workflow_dispatch 호출에만 사용
- `contents: read`: 저장소 메타데이터와 현재 ref 확인에 사용
- 태그·Release 생성 권한은 트리거 워크플로에 부여하지 않음

태그와 Release는 별도의 `Manual Prerelease` 워크플로가 자체 전체 검증 후 `contents: write` 권한으로 처리합니다.

## 재실행과 중복 방지

- 같은 트리거 브랜치에 다시 push하면 다시 dispatch될 수 있습니다.
- `Manual Prerelease`는 같은 태그의 Release가 이미 있으면 덮어쓰지 않고 실패합니다.
- 같은 태그가 다른 커밋을 가리키면 이동하거나 덮어쓰지 않습니다.
- 태그만 존재하고 Release가 없으며 태그가 현재 `main`을 가리킬 때만 제한적으로 재시도할 수 있습니다.
- 트리거 워크플로 자체는 기존 태그나 Release를 수정하지 않습니다.

## 완료 후 정리

prerelease가 성공적으로 게시된 뒤 다음을 수행합니다.

1. `release-trigger/v…` 브랜치를 삭제합니다.
2. GitHub Release에서 태그·prerelease 상태·자산 네 개를 확인합니다.
3. `SHA256SUMS.txt`와 다운로드한 파일의 해시를 확인합니다.
4. 실제 Windows 11 회사 환경은 [`RELEASE_VALIDATION.md`](RELEASE_VALIDATION.md)에 따라 사용자가 검증합니다.

실제 릴리스 태그는 삭제하지 않습니다.

## 데이터 경계

이 트리거는 태그 문자열만 `Manual Prerelease`에 전달합니다. 실제 회사 URL, 프록시 주소, PAC URL, SSID, BSSID, 로그, 측정 결과와 사용자 파일을 전달하거나 업로드하지 않습니다.
