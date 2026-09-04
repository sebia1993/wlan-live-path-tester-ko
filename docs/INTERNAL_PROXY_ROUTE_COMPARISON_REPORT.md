# 내부 DIRECT–프록시 로컬 경로 비교 보고서

이 보고서는 이미 수집한 내부 DIRECT 대상과 프록시 엔드포인트의 Windows 로컬 경로 비교 결과를 JSON·CSV·단일 HTML과 SHA-256으로 저장합니다.

보고서 생성 과정에서는 DNS, 라우팅 API, 프록시 또는 외부 사이트에 다시 접근하지 않습니다.

## 생성 파일

```text
WlanInternalProxyRoute_yyyyMMdd_HHmmss.json
WlanInternalProxyRoute_yyyyMMdd_HHmmss.csv
WlanInternalProxyRoute_yyyyMMdd_HHmmss.html
WlanInternalProxyRoute_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

- JSON: 구조화 상태·인터페이스 지문·Finding
- CSV: `section,key,value` 구조
- HTML: 외부 리소스 없는 사람용 보고서
- SHA-256: JSON·CSV·HTML 무결성 확인

같은 초에 다시 생성하면 suffix를 붙여 기존 파일을 덮어쓰지 않습니다.

## 상태

```text
Ready
Diverged
Ambiguous
Incomplete
```

| 상태 | Finding 코드 | 심각도 |
|---|---|---|
| `Ready` | `INTERNAL_PROXY_LOCAL_ROUTE_ALIGNED` | Information |
| `Diverged` | `INTERNAL_PROXY_LOCAL_ROUTE_DIVERGED` | Warning |
| `Ambiguous` | `INTERNAL_PROXY_LOCAL_ROUTE_AMBIGUOUS` | Warning |
| `Incomplete` | `INTERNAL_PROXY_LOCAL_ROUTE_INCOMPLETE` | Information |

VPN·터널 또는 가상 인터페이스가 있으면 다음 정보성 Finding이 추가될 수 있습니다.

```text
LOCAL_ROUTE_VPN_OR_TUNNEL_PRESENT
LOCAL_ROUTE_VIRTUAL_INTERFACE_PRESENT
```

`Ready`는 두 로컬 인터페이스 지문이 같다는 뜻이며 서비스 성능 정상 판정이 아닙니다.

`Diverged`는 양쪽 근거가 충분하지만 서로 다른 로컬 인터페이스를 사용한다는 뜻입니다.

`Ambiguous`는 여러 인터페이스 또는 충돌하는 메타데이터 때문에 단일 경로를 정하지 않았다는 뜻입니다.

`Incomplete`는 내부·프록시 근거가 부족하거나 외부 대상의 첫 경로가 DIRECT여서 프록시 비교를 수행할 수 없다는 뜻입니다.

## 보고서 필드

```text
status
message
internalInterface
proxyInterface
expectedWlanInterfaceFingerprint
sameLocalInterface
internalEvidencePartial
proxyEvidencePartial
proxyDirectPathSelected
proxyDirectFallbackPresent
proxyCandidateCount
proxySuccessfulCandidateCount
proxyDistinctInterfaceCount
anyVirtualInterface
anyVpnOrTunnelInterface
findings[]
warnings[]
limitation
```

인터페이스 섹션에는 다음 값만 저장합니다.

```text
interfaceFingerprint
category
isVirtual
isVpn
isUp
hasDefaultGateway
matchesExpectedWlan
```

## 개인정보 경계

보고서에는 다음 원문을 포함하지 않습니다.

- 내부 대상 URL·호스트
- 프록시 호스트
- 인터페이스 전체 GUID
- 인터페이스 이름과 설명
- IPv4·IPv6·MAC
- 게이트웨이와 DNS
- SSID·BSSID
- 이메일과 Windows 사용자 경로

허용되는 인터페이스 지문은 SHA-256 기반 10자리 소문자 16진수만입니다.

```text
0123456789
abcdef0123
```

전체 GUID나 잘못된 문자열이 지문 필드에 들어오면 보고서 매퍼가 해당 인터페이스를 제거합니다.

자유형 메시지·경고에는 다음 보호를 적용합니다.

1. GUID 형식 제거
2. URL·이메일·IP·MAC·사용자 경로 마스킹
3. 중복 경고 제거

Finding은 고정 코드·고정 설명으로 생성합니다.

## CSV 안전성

CSV 값이 다음 문자로 시작하면 apostrophe를 붙여 스프레드시트 수식 실행을 방지합니다.

```text
=
+
-
@
tab
carriage return
```

## HTML 안전성

- HTML5 doctype
- 동적 값 HTML 인코딩
- Content Security Policy
- JavaScript 없음
- 외부 stylesheet 없음
- iframe 없음
- 외부 이미지·웹폰트 없음
- form action 없음

HTML은 머신용 Finding 코드보다 사람이 읽을 수 있는 제목·근거·해석·조치·한계를 중심으로 표시합니다. 자동 처리는 JSON 또는 CSV의 코드를 사용합니다.

## SHA-256

`_SHA256SUMS.txt`에는 다음 세 파일의 SHA-256이 들어갑니다.

```text
JSON
CSV
HTML
```

전달 후 파일 변경 여부를 확인할 수 있습니다.

## 통신 경계

보고서 Writer는 `InternalProxyRouteComparisonResult`만 읽고 로컬 파일을 생성합니다.

수행하지 않는 작업:

- DNS 조회
- Windows 라우팅 API 호출
- 프록시 TCP 연결
- HTTP CONNECT·인증·HEAD·GET
- PAC·WPAD
- 프록시 서버 API
- 외부 분석 API·AI·로컬 AI
- 텔레메트리·자동 오류 전송
- 결과 업로드

DNS와 Windows 경로 판정은 사용자가 이전 단계의 로컬 경로 분석을 실행했을 때만 수행됩니다.

## 자동 검증

ReportSmoke는 다음을 확인합니다.

- 네 상태와 주요 Finding 코드·심각도
- VPN·가상 인터페이스 보조 Finding
- JSON 구조화 상태와 Finding 코드
- CSV 코드와 수식 비활성화
- HTML 제목·해석과 CSP
- 외부 script·iframe·stylesheet 부재
- 전체 GUID·이메일·IP·URL 비노출
- 잘못된 인터페이스 지문 제거
- JSON·CSV·HTML·SHA-256 파일 생성
- 실제 SHA-256 재계산 일치

## 실제 사용 순서

1. 내부 DIRECT 대상의 로컬 경로를 확인합니다.
2. 외부 대상에 적용되는 프록시 문자열을 해석합니다.
3. 프록시 엔드포인트 로컬 경로를 확인합니다.
4. 내부–프록시 비교를 실행합니다.
5. 상태, 인터페이스 category와 WLAN 일치 여부를 검토합니다.
6. 비교 보고서를 생성합니다.
7. HTML을 열어 사람이 읽는 Finding을 확인합니다.
8. 공유 전에 JSON·CSV·HTML의 민감정보를 직접 다시 검토합니다.
9. 필요하면 SHA-256을 재계산합니다.

이 보고서만으로 프록시 장애, WLAN 장애 또는 인터넷 회선 장애를 확정하지 않습니다. 내부·외부 처리량, HTTP 상태, 프록시 인증, RSSI·PHY·로밍과 장비 로그를 함께 판단합니다.
