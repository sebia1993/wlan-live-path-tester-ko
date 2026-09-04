# 라우팅 근거 구조화 보고서

`라우팅 보고서` 탭은 현재 앱 실행에서 확인한 최근 목적지별 Windows 라우팅 근거를 로컬 파일로 저장합니다.

## 저장 파일

```text
WlanRouteEvidence_yyyyMMdd_HHmmss.json
WlanRouteEvidence_yyyyMMdd_HHmmss.csv
WlanRouteEvidence_yyyyMMdd_HHmmss.html
WlanRouteEvidence_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

최근 결과는 메모리에 최대 12건만 보관합니다. 앱을 종료하면 이 이력은 사라지므로 필요한 보고서는 종료 전에 생성해야 합니다.

## 보고서에 포함하는 값

- 라우팅 확인 시각
- 목적 구분
  - 내부 DIRECT 측정 대상
  - 프록시 엔드포인트
  - 외부 사이트 참고 경로
  - 수동 목적지
- DNS 사용 여부
- 확인한 주소 수
- 전체 상태
  - 성공
  - 일부 성공
  - 복수 인터페이스
  - 입력·DNS·경로 오류
  - 취소
- 선택된 인터페이스 범주
- Windows 네이티브 인터페이스 유형
- 인터페이스 Up·Down 상태
- 기본 게이트웨이 유무
- VPN·가상 인터페이스 분류
- 인터페이스 ID의 SHA-256 앞 10자리 지문
- IPv4·IPv6 주소 계열별 상태와 인터페이스 범주
- Windows 네이티브 오류 코드
- 판단 경고와 한계

## 보고서에 포함하지 않는 값

보고서 모델에는 다음 필드가 존재하지 않습니다.

- DNS로 확인한 목적지 IPv4·IPv6 주소
- 기본 게이트웨이 주소
- DNS 서버 주소
- MAC 주소
- 인터페이스 이름과 설명
- 전체 인터페이스 GUID
- 입력한 실제 내부·외부 URL
- 프록시 주소와 PAC URL

인터페이스 ID는 서로 다른 결과가 같은 로컬 NIC를 가리키는지 비교할 수 있도록 짧은 지문만 기록합니다. 지문으로 원래 GUID를 복원하는 기능은 제공하지 않습니다.

## JSON

JSON은 프로그램이나 별도 로컬 분석 도구에서 사용하기 위한 구조화 형식입니다.

```text
results[]
  ├─ purpose
  ├─ dnsWasUsed
  ├─ resolvedAddressCount
  ├─ status
  ├─ selectedInterface
  │    ├─ idFingerprint
  │    ├─ category
  │    ├─ nativeInterfaceType
  │    ├─ operationalState
  │    ├─ hasDefaultGateway
  │    ├─ isVirtual
  │    └─ isVpn
  ├─ addressEvidence[]
  ├─ warnings[]
  └─ message
```

## CSV

CSV는 `section,key,value` 세 열을 사용합니다.

```text
route.1
route.1.selectedInterface
route.1.address.1
route.1.address.1.interface
route.1.warning
```

CSV 값이 `=`, `+`, `-`, `@` 등 스프레드시트 수식 시작 문자로 시작하면 실행되지 않도록 비활성화합니다.

## HTML

HTML은 한 파일 안에 스타일을 포함하며 다음 외부 리소스를 사용하지 않습니다.

- JavaScript
- 외부 CSS
- 웹폰트
- 외부 이미지
- iframe

Content Security Policy를 포함하고 모든 동적 텍스트를 HTML 인코딩합니다.

## SHA-256

`_SHA256SUMS.txt`에는 JSON·CSV·HTML 세 파일의 SHA-256이 기록됩니다. 파일을 전달하거나 보관한 뒤 내용이 바뀌지 않았는지 다음과 같이 확인할 수 있습니다.

```powershell
Get-FileHash .\WlanRouteEvidence_*.json -Algorithm SHA256
Get-FileHash .\WlanRouteEvidence_*.csv -Algorithm SHA256
Get-FileHash .\WlanRouteEvidence_*.html -Algorithm SHA256
```

## 통신 경계

보고서 생성은 이미 메모리에 있는 라우팅 결과와 로컬 파일 시스템만 사용합니다. 보고서를 만드는 동안 다음 통신은 발생하지 않습니다.

- DNS 조회
- HTTP/HTTPS 요청
- 외부 API 호출
- 텔레메트리
- 파일 업로드

라우팅 근거를 처음 확인할 때 호스트 이름이나 URL을 사용했다면 그 시점에는 사용자가 누른 버튼에 의해 DNS 확인이 발생할 수 있습니다. 보고서 생성 시 DNS를 다시 실행하지는 않습니다.

## 해석 주의

회사 프록시 환경에서 `외부 사이트 참고 경로`는 외부 사이트 주소에 대한 Windows 직접 라우팅 가정입니다. 실제 외부 HTTP 측정은 일반적으로 다음 경로입니다.

```text
PC → 회사 프록시 → 외부 사이트 또는 CDN
```

따라서 외부 다운로드의 로컬 인터페이스 근거는 프록시 엔드포인트를 기준으로 확인해야 합니다. 프록시 주소를 알 수 없거나 운영 정책상 공개할 수 없다면 외부 사이트 참고 결과만으로 실제 경로를 확정하지 않습니다.
