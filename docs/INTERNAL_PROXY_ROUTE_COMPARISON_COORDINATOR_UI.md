# 코디네이터 기반 내부 DIRECT–프록시 경로 비교 UI

`경로 비교` 탭은 사용자가 승인된 내부 DIRECT 대상, 외부 HTTP(S) URL과 해당 URL에 적용되는 프록시 지시문을 입력해 Windows 첫 로컬 인터페이스를 비교하는 화면입니다.

UI는 파서·내부 reader·프록시 분석기·비교기를 개별적으로 호출하지 않고 `InternalProxyRouteComparisonCoordinator` 하나만 실행합니다.

## 입력

### 내부 DIRECT 기준 대상

지원 형식:

```text
https://internal.example/path/file.bin
internal.example
10.20.30.40
2001:db8::10
```

회사 정책상 프록시를 우회하는 승인된 내부 대상만 사용해야 합니다. 프로그램은 입력 문자열만 보고 DIRECT 정책을 자동 증명하지 않습니다.

### 외부 프록시 판정 대상 URL

```text
https://download.example/file.bin
http://test.example/object.bin
```

절대 HTTP 또는 HTTPS URL만 허용합니다.

이 값은 다음 수동 프록시 매핑에서 현재 대상에 적용되는 항목을 정확히 선택하기 위해 필요합니다.

```text
http=proxy-http.example:8080;
https=proxy-https.example:8443
```

HTTPS 외부 URL에는 `https=` 후보만 선택하며 `http=` 후보를 임의 fallback하지 않습니다.

### 프록시 지시문

지원 예:

```text
PROXY proxy-a.example:8080; DIRECT
HTTPS proxy-b.example:8443; PROXY proxy-a.example:8080
http=proxy-http.example:8080;https=proxy-https.example:8443
SOCKS5 [2001:db8::5]:1080; DIRECT
DIRECT; PROXY later.example:8080
```

입력 원문은 텍스트 상자와 실행 중 메모리에만 있으며 자동 파일 저장이나 외부 전송을 하지 않습니다.

## 버튼

```text
내부↔프록시 경로 비교
경로 확인 중지
비교 보고서 생성
보고서 폴더 열기
최신 HTML 열기
```

보고서 버튼은 구조화 실행 결과가 생성된 뒤에만 활성화됩니다.

## 실행 흐름

사용자가 비교 버튼을 누르면 다음 순서로 처리합니다.

1. 기존 다운로드 측정 또는 브라우저 관찰 실행 상태 확인
2. 외부 URL의 절대 URI 형식 확인
3. 현재 Native WLAN 인터페이스 ID 확인
4. 코디네이터 실행
5. 프록시 문자열 선검증
6. 필요할 때만 내부 DNS·Windows 최적 경로 확인
7. 필요할 때만 프록시 후보 DNS·Windows 최적 경로 확인
8. 내부·프록시 인터페이스 비교
9. 안전 스냅샷 기반 텍스트 표시
10. 구조화 실행 결과를 앱 메모리에 유지

UI는 reader와 분석기를 따로 재조합하지 않으므로 코디네이터의 zero-read 정책을 그대로 사용합니다.

## 네트워크 조회 0회 조건

다음 조건에서는 내부·프록시 DNS·라우팅 조회를 시작하지 않습니다.

- 외부 URL에 적용 가능한 안전한 프록시 경로가 없음
- 프록시 입력 오류
- `DIRECT`가 첫 적용 경로
- 실행 전 사용자 취소
- 잘못된 DNS 제한 시간

내부 경로 확인을 시작했지만 비교 가능한 기준을 얻지 못하면 프록시 후보 조회를 추가로 수행하지 않습니다.

## 다른 작업과의 동시 실행

비교 시작 전 기존 `MainWindow`의 다음 상태를 확인합니다.

```text
_measurementRunning
_observationCancellation
```

기존 필드가 현재 버전에 존재하고 측정·관찰이 활성 상태이면 경로 비교를 시작하지 않습니다.

경로 비교가 시작되면:

- 현재 경로 비교 탭을 제외한 다른 탭 잠금
- 세 입력 상자 잠금
- 새 비교 시작 버튼 비활성화
- 중지 버튼 활성화
- 보고서 생성 비활성화

완료·취소·오류 후 각 탭의 이전 활성 상태를 복원합니다.

필드 이름이 향후 변경돼도 reflection 확인이 실패할 뿐 애플리케이션 빌드가 직접 결합되지 않도록 구성했습니다. 장기적으로는 모든 측정 기능이 공통 작업 상태 서비스를 사용하도록 정리해야 합니다.

## 취소

`경로 확인 중지`를 누르면 현재 CancellationToken을 취소합니다.

- 이후 DNS·라우팅 단계 시작 금지
- 완료되지 않은 후보 미조회
- 입력 원문·예외 메시지 미표시
- 기존에 완료된 이전 실행 결과 자동 삭제 안 함
- 새 실행이 구조화 결과를 반환한 경우에만 최신 결과 교체

창을 닫을 때도 활성 토큰을 취소하고 잠긴 탭 상태를 복원합니다.

## 안전한 결과 표시

UI는 `InternalProxyRouteComparisonRunResult`의 자유형 필드나 원본 경로 객체를 직접 출력하지 않습니다.

```text
RunResult
  → InternalProxyRouteComparisonRunSnapshotMapper
  → InternalProxyRouteComparisonRunTextRenderer
```

표시 필드:

- 완료 시각
- 실행 상태
- 프록시 출처·결정
- HTTP·HTTPS 대상 스킴
- 내부·프록시 구조화 상태
- 각 단계 수행 여부
- 파싱·분석·성공 후보 수
- DIRECT·fallback
- WLAN ID 확인 여부
- 비교 상태와 같은 인터페이스 여부
- 부분 근거·VPN·터널·가상 NIC 여부
- 검증된 인터페이스 범주와 10자리 지문
- 고정 Finding 코드·심각도·근거·해석·조치·한계
- 고정 데이터 처리 선언

의도적으로 출력하지 않는 값:

- 내부 URL·호스트·IP
- 외부 URL
- 프록시 호스트와 지시문
- 전체 WLAN·인터페이스 GUID
- 인터페이스 이름과 설명
- 주소별 원본 경로 근거
- 실행·비교 자유형 Message·Warnings·Limitation
- 예외 메시지

## 결과 색상

```text
Ready       → 녹색
Diverged    → 파란색
Ambiguous   → 주황색
Incomplete  → 빨간색
DIRECT 우선 → 파란색
취소·입력 확인 필요 → 주황색
실행 실패   → 빨간색
```

`Diverged`는 확인할 경로 차이라는 의미이지 자동 장애 확정이 아니므로 오류 빨간색 대신 파란색으로 표시합니다.

## 보고서 생성

구조화 실행 결과가 메모리에 있으면 사용자가 `비교 보고서 생성`을 눌러 다음 파일을 생성할 수 있습니다.

```text
WlanInternalProxyRouteComparison_yyyyMMdd_HHmmss.json
WlanInternalProxyRouteComparison_yyyyMMdd_HHmmss.csv
WlanInternalProxyRouteComparison_yyyyMMdd_HHmmss.html
WlanInternalProxyRouteComparison_yyyyMMdd_HHmmss_SHA256SUMS.txt
```

기본 폴더:

```text
%LOCALAPPDATA%\WlanLivePathTesterKO\Reports\InternalProxyRouteComparison
```

UI 결과에는 전체 사용자 경로를 표시하지 않고 파일 이름만 표시합니다. `보고서 폴더 열기`를 사용해 Windows Explorer에서 확인합니다.

보고서 생성은 다음만 사용합니다.

- 메모리의 구조화 실행 결과
- 안전 스냅샷 mapper
- 로컬 파일 시스템
- SHA-256

추가 DNS·HTTP·프록시 요청이나 외부 업로드를 하지 않습니다.

## 실제 환경 확인

1. Wi-Fi만 연결하고 내부·프록시 경로가 모두 `Wireless`인지 확인합니다.
2. 회사 VPN 연결 전후 프록시 경로가 `Tunnel`로 변경되는지 확인합니다.
3. 유선과 Wi-Fi를 함께 연결해 목적지별 우선순위를 확인합니다.
4. `DIRECT; PROXY ...`에서 모든 DNS·라우팅 단계가 생략되는지 확인합니다.
5. `PROXY ...; DIRECT`에서 프록시 후보만 분석되는지 확인합니다.
6. HTTPS 외부 URL과 `http=` 전용 설정에서 입력 확인 상태가 되는지 확인합니다.
7. 내부 DNS 실패 시 프록시 후보를 추가 조회하지 않는지 확인합니다.
8. 비교 중 중지를 눌러 이후 후보가 조회되지 않는지 확인합니다.
9. 결과 영역에 실제 내부 URL·외부 URL·프록시 호스트·전체 GUID가 없는지 확인합니다.
10. 보고서 JSON·CSV·HTML과 SHA-256을 확인합니다.
11. 경로 비교 중 다른 탭이 잠기고 종료 후 이전 상태로 복원되는지 확인합니다.
12. 창 종료 시 진행 중인 비교가 취소되는지 확인합니다.

## 현재 한계

- Windows 설정 또는 대상별 PAC/WPAD 판정 결과를 자동으로 입력 상자에 불러오는 기능은 아직 연결되지 않았습니다.
- 기존 작업 상태 확인은 현재 `MainWindow`의 비공개 필드 이름을 안전하게 조회하는 임시 호환 방식입니다.
- 모든 네트워크 기능을 통합하는 공통 작업 상태 서비스가 필요합니다.
- 실제 회사 PAC·WPAD, 407 인증, VPN·EDR·GPO와 IPv4·IPv6 분기 동작은 Windows 11 실제 환경에서 확인해야 합니다.
