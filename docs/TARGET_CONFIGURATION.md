# 승인 측정 대상 설정

승인 대상 설정은 내부·외부 다운로드 URL과 실행 제한을 로컬 JSON으로 관리하는 기능입니다. 설정 파일을 읽고 화면에 적용하는 과정 자체는 DNS, PAC/WPAD, HTTP 또는 기타 네트워크 요청을 만들지 않습니다.

## 운영 모드

### 선택형 승인 목록

현재 사용자 또는 Portable 폴더의 `targets.local.json`을 사용합니다.

- 승인 목록을 불러와 URL과 실행 제한을 화면에 채웁니다.
- 사용자는 고급 수동 입력 모드로 전환할 수 있습니다.
- 수동 입력으로 전환하면 런타임 승인 카탈로그를 해제합니다.
- 설정 파일이 손상되면 오류를 표시하고 수동 입력 모드로 전환합니다.

### 관리자 강제 승인 정책

관리자가 `%ProgramData%`에 배포한 `targets.json`에서 `enforceApprovedTargets`를 `true`로 지정합니다.

- 사용자·Portable 설정보다 항상 먼저 적용합니다.
- 수동 URL 입력 체크박스를 비활성화합니다.
- URL, 최대 수신량, 제한 시간, 스트림 수와 리다이렉트 제한을 승인값으로 고정합니다.
- UI뿐 아니라 Core 검증 단계에서도 미등록 URL과 변경된 실행 제한을 차단합니다.
- 관리자 설정이 존재하지만 JSON 파싱·스키마·대상 검증에 실패하면 모든 다운로드 측정을 fail-closed 방식으로 차단합니다.
- 오류 상태에서도 WLAN 조회, 프록시 설정 확인, 브라우저 관찰과 로컬 보고서 기능은 사용할 수 있습니다.

`enforceApprovedTargets: true`는 관리자 ProgramData 설정에서만 허용합니다. 사용자 또는 Portable 설정에서 사용하면 해당 설정을 거부합니다.

## 설정 파일 검색 순서

프로그램은 다음 순서로 첫 번째 설정 파일을 선택합니다.

1. `%ProgramData%\WLAN Live Path Tester KO\targets.json`
2. `%LocalAppData%\WLAN Live Path Tester KO\targets.local.json`
3. 실행 파일 폴더의 `config\targets.local.json`

ProgramData 파일이 있으면 다른 두 위치는 확인하지 않습니다. 실제 사내 URL이 포함된 파일은 Git 저장소에 커밋하지 마십시오.

## 기본 형식

저장소와 Portable ZIP의 `config\targets.example.json`을 복사한 뒤 실제 승인 URL로 수정합니다.

```json
{
  "schemaVersion": 1,
  "enforceApprovedTargets": false,
  "defaults": {
    "timeoutSeconds": 30,
    "maxBytes": 104857600,
    "streams": 1,
    "maxRedirects": 5
  },
  "internalTargets": [
    {
      "name": "사내 유선 기준 서버",
      "url": "http://internal-host/network-test/100mb.bin",
      "requireDirect": true,
      "allowedRedirectHosts": []
    }
  ],
  "externalTargets": [
    {
      "name": "승인 외부 다운로드 대상",
      "url": "https://approved-host.example/test.bin",
      "requireProxy": true,
      "allowedRedirectHosts": [
        "approved-cdn.example"
      ]
    }
  ]
}
```

관리자 강제 정책은 같은 문서에서 다음 값만 바꿉니다.

```json
"enforceApprovedTargets": true
```

## 안전 규칙

- 내부 대상은 기본적으로 `DIRECT` 경로를 요구합니다.
- 외부 대상은 기본적으로 회사 `PROXY` 경로를 요구합니다.
- 설정에 없는 속성, JSON 주석과 trailing comma는 거부합니다.
- URL은 HTTP 또는 HTTPS만 허용합니다.
- URL 안의 사용자명·비밀번호와 fragment는 거부합니다.
- 외부 대상에 localhost, 사설·링크 로컬 IP 또는 점이 없는 내부 호스트 이름을 사용할 수 없습니다.
- 같은 경로 종류에서 URL이 중복되면 설정을 거부합니다.
- 리다이렉트는 최초 호스트 또는 `allowedRedirectHosts`에 정확히 등록한 호스트로만 허용합니다.
- 승인 목록이 활성화된 동안 미등록 URL과 변경된 수신량·제한 시간·스트림·리다이렉트 제한은 측정 엔진에서 차단합니다.
- 관리자 강제 정책에서는 고급 수동 입력으로 승인 목록을 해제할 수 없습니다.

## 값 범위

| 항목 | 범위 |
|---|---:|
| `maxBytes` | 1MiB~1GiB |
| `timeoutSeconds` | 5~300초 |
| `streams` | 1~4 |
| `maxRedirects` | 0~10 |
| `allowedRedirectHosts` | 대상당 최대 16개 |

`maxBytes`는 각 외부 URL에 개별 적용됩니다. 외부 URL이 세 개이고 각각 100MiB이면 1회 측정의 최대 예상 수신량은 총 300MiB입니다. 반복 측정에서는 예열과 본 측정 횟수만큼 실제 다운로드가 추가됩니다.

## 리다이렉트 호스트

같은 호스트 안에서 경로만 바뀌는 리다이렉트는 자동 허용됩니다. 다른 CDN 호스트로 이동해야 할 때만 정확한 호스트 이름을 `allowedRedirectHosts`에 추가합니다.

와일드카드는 지원하지 않습니다. 예를 들어 `*.example.com` 대신 실제 필요한 호스트인 `download.example.com`을 등록합니다. 교차 호스트 리다이렉트는 기본 HTTP 80 또는 HTTPS 443 포트만 허용합니다.

## 관리자 배포 예시

관리자 PowerShell에서 다음과 같이 폴더와 설정 파일을 만들 수 있습니다.

```powershell
$folder = Join-Path $env:ProgramData 'WLAN Live Path Tester KO'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
Copy-Item .\targets.json (Join-Path $folder 'targets.json') -Force
```

일반 사용자는 읽기만 가능하고 SYSTEM·Administrators만 수정할 수 있도록 ACL을 적용합니다. 아래 그룹 이름은 한국어 Windows 또는 회사 도메인 정책에 맞게 조정해야 합니다.

```powershell
$folder = Join-Path $env:ProgramData 'WLAN Live Path Tester KO'
icacls $folder /inheritance:r
icacls $folder /grant:r `
  'SYSTEM:(OI)(CI)F' `
  'Administrators:(OI)(CI)F' `
  'Users:(OI)(CI)RX'
```

설정 파일을 읽을 권한이 없는 상태는 정상 배포가 아닙니다. 일반 사용자에게 폴더 탐색과 파일 읽기 권한을 부여하되 수정·삭제 권한은 주지 마십시오.

## 사용자·Portable 배포

현재 사용자에게만 적용하려면 다음 위치에 둡니다.

```text
%LocalAppData%\WLAN Live Path Tester KO\targets.local.json
```

Portable ZIP과 함께 배포하려면 다음 위치에 둡니다.

```text
<프로그램 폴더>\config\targets.local.json
```

선택형 설정에서는 `enforceApprovedTargets`를 `false`로 유지합니다.

## 오류 처리

### 관리자 설정 오류

다음 상황에서는 모든 다운로드 측정을 차단합니다.

- JSON 형식 오류
- 지원하지 않는 `schemaVersion`
- 알 수 없는 속성
- 중복 URL
- 잘못된 URL 또는 실행 제한
- 내부·외부 대상 개수 오류
- 대상별 실행 제한이 현재 UI 정책과 맞지 않음
- 관리자 파일에서 강제 정책을 읽지 못함

오류 메시지에는 실제 프록시 주소나 PAC URL을 포함하지 않습니다. 설정을 수정한 뒤 앱에서 `승인 대상 다시 불러오기`를 누릅니다.

### 선택형 설정 오류

사용자·Portable 설정 오류는 해당 승인 목록을 사용하지 않고 고급 수동 입력 모드로 전환합니다. 프로그램은 오류가 있는 값을 부분적으로 적용하지 않습니다.

## 보안 경계

- 설정 파일에 프록시 주소, PAC URL, 계정·비밀번호를 넣지 마십시오.
- 승인 정책은 허용할 측정 URL과 전송 상한을 통제하지만 프록시 서버의 내부 상태를 확인하지는 않습니다.
- 관리자 강제 정책의 실제 수정 방지는 Windows 파일 ACL에 의존합니다.
- 설정 변경 감시나 원격 중앙 관리는 수행하지 않습니다.
- 설정을 다시 읽는 동작은 로컬 파일만 읽으며 네트워크 요청을 만들지 않습니다.
- 실제 측정은 사용자가 내부 또는 외부 측정 시작 버튼을 눌러야 수행됩니다.
