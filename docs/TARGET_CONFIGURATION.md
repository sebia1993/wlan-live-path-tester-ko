# 승인 측정 대상 설정

`targets.local.json`은 내부·외부 다운로드 측정 대상과 실행 제한을 로컬에서 고정하는 선택 기능입니다. 이 파일을 읽는 동작 자체는 네트워크 요청을 만들지 않습니다.

## 배치 위치

프로그램은 다음 순서로 파일을 찾습니다.

1. `%LOCALAPPDATA%\WLAN Live Path Tester KO\targets.local.json`
2. 실행 파일 폴더의 `config\targets.local.json`

첫 번째로 발견한 파일을 사용합니다. 실제 사내 주소가 포함된 파일은 Git 저장소에 커밋하지 마십시오.

## 기본 형식

저장소와 Portable ZIP의 `config\targets.example.json`을 복사해 `targets.local.json`으로 이름을 바꾼 뒤 실제 승인 URL로 수정합니다.

```json
{
  "schemaVersion": 1,
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

## 안전 규칙

- 내부 대상은 기본적으로 `DIRECT` 경로를 요구합니다.
- 외부 대상은 기본적으로 회사 `PROXY` 경로를 요구합니다.
- 설정에 없는 속성, JSON 주석과 trailing comma는 거부합니다.
- URL은 HTTP 또는 HTTPS만 허용합니다.
- URL 안의 사용자명·비밀번호와 fragment는 거부합니다.
- 외부 대상에 localhost, 사설·링크 로컬 IP 또는 점이 없는 내부 호스트 이름을 사용할 수 없습니다.
- 리다이렉트는 최초 호스트 또는 `allowedRedirectHosts`에 정확히 등록한 호스트로만 허용합니다.
- 승인 목록이 활성화된 동안 미등록 URL과 변경된 수신량·제한 시간·스트림·리다이렉트 제한은 측정 엔진에서 차단합니다.
- 화면의 고급 수동 입력 모드를 명시적으로 선택하면 승인 목록을 비활성화하고 수동 URL을 사용할 수 있습니다.

## 값 범위

| 항목 | 범위 |
|---|---:|
| `maxBytes` | 1MiB~1GiB |
| `timeoutSeconds` | 5~300초 |
| `streams` | 1~4 |
| `maxRedirects` | 0~10 |
| `allowedRedirectHosts` | 대상당 최대 16개 |

`maxBytes`는 각 외부 URL에 개별 적용됩니다. 외부 URL이 세 개이고 각각 100MiB이면 최악의 경우 총 300MiB를 수신할 수 있습니다.

## 리다이렉트 호스트

같은 호스트 안에서 경로만 바뀌는 리다이렉트는 자동 허용됩니다. 다른 CDN 호스트로 이동해야 할 때만 정확한 호스트 이름을 `allowedRedirectHosts`에 추가합니다.

와일드카드는 지원하지 않습니다. 예를 들어 `*.example.com` 대신 실제 필요한 호스트인 `download.example.com`을 등록합니다. 교차 호스트 리다이렉트는 기본 HTTP 80 또는 HTTPS 443 포트만 허용합니다.

## 보안 및 배포

- 파일에 프록시 주소, PAC URL, 계정·비밀번호를 넣지 마십시오.
- 일반 사용자에게 승인 목록만 제공하려면 파일의 Windows ACL을 읽기 전용으로 배포하십시오.
- 설정 파일 오류 시 프로그램은 해당 내용을 표시하고 수동 입력 모드로 전환합니다. 엄격한 중앙 통제가 필요하면 향후 관리자 강제 모드를 사용하십시오.
- 설정 변경 후 앱의 `승인 대상 다시 불러오기`를 누르면 네트워크 요청 없이 다시 읽습니다.
