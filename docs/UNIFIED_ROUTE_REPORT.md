# 통합 진단 보고서의 내부 DIRECT–프록시 비교

## 기능 범위

`로컬 보고서` 탭에서 기존 WLAN·프록시 설정·다운로드 결과·브라우저 관찰 결과에 더해, 현재 앱 메모리에 있는 가장 최근 경로 비교를 같은 JSON·CSV·HTML에 포함합니다. 경로 비교를 한 번도 실행하지 않았다면 기존 보고서대로 동작합니다. 별도의 전용 경로 보고서도 그대로 유지됩니다.

보고서 생성으로 DNS·라우팅·PAC/WPAD 또는 다운로드를 다시 실행하지 않습니다. 기존 로컬 WLAN·현재 사용자 프록시 설정 읽기는 유지합니다. 새 외부 분석 API·AI·업로드·텔레메트리·자동 업데이트를 추가하지 않습니다.

## 서로 다른 측정 시각

경로 비교는 다운로드와 별도 실행에서 얻은 결과입니다. 새 보고서 생성 시각을 과거 경로 결과의 측정 시각으로 덮어쓰지 않습니다.

- report.metadata.generatedAt: 통합 보고서 생성 시각
- internalProxyRouteComparison.completedAt: 경로 비교 완료 시각
- internalProxyRouteComparison.comparison.evaluatedAt: 정확 인터페이스 비교 시각

따라서 같은 보고서에 포함돼도 동시에 측정한 증거가 아닙니다. 다운로드와 경로 비교 사이에 VPN·NIC·라우팅·프록시 정책이 바뀌었을 가능성을 배제하지 않습니다. 이 한계를 JSON·CSV·HTML에 명시합니다.

## 스키마 호환성

LocalDiagnosticReport의 기존 9개 positional 인자와 9개 Deconstruct 값을 변경하지 않습니다. 새 `InternalProxyRouteComparison`은 record 본문의 init-only optional 속성입니다.

- 경로 결과 없음: 속성 null, JSON에서 해당 키 생략, 기존 1.1 형식 유지
- 명시적 null 또는 키 없는 이전 JSON: 기존대로 역직렬화 가능
- 경로 결과 있음: 기존 1.0/1.1을 1.2로 표시
- 이미 더 높은 버전인 입력: 임의로 낮추지 않음

새 속성은 기존 전용 보고서의 `InternalProxyRouteComparisonRunReportSnapshot`을 재사용합니다. raw run, InternalRouteEvidence, ProxyExecution, 전체 인터페이스 ID, endpoint label, 임의 warning/message는 보고서에 연결하지 않습니다.

## 안전한 매핑

운영 진입점은 `LocalReportRouteComparison.Attach(report, run)`입니다. 기존 `InternalProxyRouteComparisonRunReportSnapshotMapper.FromResult`로 새 스냅샷을 만들며 검증된 상태·범주·포트·개수·축약 지문과 고정 Finding만 가져옵니다.

공개 보고서 DTO는 애플리케이션이 생성한 안전한 매핑 결과를 받는 계약입니다. 외부의 임의 JSON을 읽어 자동으로 신뢰하는 기능이 아닙니다. HTML 인코딩과 CSV 수식 방지는 출력 단계에서도 유지합니다.

## 포함 내용

JSON의 `internalProxyRouteComparison`:

- 실행 상태 및 완료 시각
- 프록시 출처·선택·계획·실행 상태
- 대상 프로토콜과 DIRECT 우선/대체 여부
- 파싱 오류 존재 여부
- 적용·분석·성공 후보 수
- 실제 내부 경로 읽기 및 프록시 분석 수행 여부
- Ready·Diverged·Ambiguous·Incomplete 결과
- 정확 인터페이스 비교 여부와 범주·축약 지문
- 프록시 후보별 프로토콜·포트·지문·주소 집계·WLAN 상관 상태
- 고정 Finding

CSV는 기존 `section,key,value`를 유지하며 다음 section을 추가합니다.

```text
internalProxyRouteComparison
internalProxyRouteComparison.comparison
internalProxyRouteComparison.proxyEntry.1
internalProxyRouteComparison.proxyEntry.2
...
```

HTML은 기존 CSP와 오프라인 단일 문서 구조에 비교 카드와 후보별 표를 추가합니다. 모든 동적 텍스트를 인코딩합니다. CSV와 HTML은 같은 안전 DTO 필드 projection을 사용해 형식별 필드 누락을 방지합니다.

## Finding 결합

새 비교가 있을 때 다음을 수행합니다.

1. 기존 `INTERNAL_PROXY_ROUTE_` 계열 Finding을 대소문자 구분 없이 교체합니다.
2. 일반 `NO_CLEAR_FAILURE_PATTERN` 문구를 제거합니다.
3. 현재 snapshot의 고정 Finding을 전역 Findings 목록에 정확히 한 번 추가합니다.
4. 다른 WLAN·관찰·인증·측정 Finding은 보존합니다.
5. 별도 측정 시각과 로컬 경로 한계를 한 번씩 추가합니다.

Attach와 출력 렌더링을 반복해도 같은 경로 Finding과 한계 문구가 누적되지 않습니다. 스냅샷 내부 Finding과 전역 Findings의 동일 코드 표현은 구조상 의도된 참조이며, 전역 Findings 배열에 중복 판정 두 개를 만드는 것은 아닙니다.

## 저장 실행·창 닫기

통합 보고서 저장은 기존 MainWindow의 같은 ApplicationOperationUiSession/Core coordinator에서 `DiagnosticReportSave` lease를 획득합니다.

- 다른 작업 실행 중이면 로컬 수집과 writer를 호출하지 않음
- 저장 중 중복 생성·보고서 열기 차단
- 다른 탭 잠금 및 실제 쓰기 완료 뒤 복원
- 창 닫기는 실제 저장 task와 UI 정리 완료까지 대기
- 성공한 경우에만 최근 보고서 경로 교체
- 실패하면 이전 성공 경로 보존, 오류 유형만 표시
- 종료 대기 중 실패하면 창을 유지하고 shutdown 상태 해제; 확인 후 다시 닫기 가능

새 전역 coordinator나 polling timer를 만들지 않습니다. 기존 측정·관찰의 종료 흐름과 전용 경로 보고서의 파일 복구 흐름을 교체하지 않습니다.

### 아직 남은 저장 한계

기존 LocalReportWriter는 파일별 임시 파일 이동과 순차 이름 충돌 회피를 사용합니다. 전용 경로 보고서의 전체 파일세트 예약·취소·rollback을 이 변경으로 모든 보고서에 이식한 것은 아닙니다.

통합 저장에는 안전한 중간 취소 callback을 등록하지 않습니다. 파일 쓰기가 진행 중인데 완료나 취소로 오표시하지 않고 실제 반환을 기다립니다. 일부 파일이 기록된 뒤 실패할 수 있어 오류 안내는 출력 폴더 확인을 요구합니다. 이 경우 실패한 저장을 자동으로 정리 완료라고 표현하지 않습니다.

## 자동 검증

`UnifiedReportSmoke`는 보고서 9개 그룹과 실제 WPF 8개 그룹, 총 17개 그룹을 수행합니다.

보고서 검증:

- 기존 생성자·Deconstruct·null 입력 유지
- 네 비교 상태와 전체 실행 상태·unknown
- Finding 대소문자 중복·교체·다른 판정 보존
- 원문 URL·프록시·IP·이메일·전체 GUID·adapter description 비노출
- HTML 인코딩·CSP·CSV 수식/따옴표/줄바꿈 보호
- 구형/신형/명시적 null JSON 왕복
- 안전 필드 projection 누락·별도 측정 시각 확인
- 같은 시각 순차 두 번 저장 시 독립 8개 파일과 SHA-256 재계산

WPF 검증:

- 실제 저장 경로의 안전 snapshot 전달
- 중앙 lease 획득 후에만 capture/writer 실행
- 중복·다른 작업·보고서 열기 차단
- 실패 후 이전 경로 보존과 재시도
- 성공 시 실제 Closed까지 대기
- 종료 중 저장 실패 시 창 유지 및 재종료
- 수집 실패 시 writer 0회
- 탭 단일 등록·닫힌 창·Dispatcher 소유권

테스트는 창을 Show하지 않고 합성 수집·저장 함수를 주입하므로 회사망·외부 HTTP·PAC·WLAN 수집을 수행하지 않습니다. 파일 무결성 테스트만 임시 폴더에 합성 보고서를 저장합니다. 최종 통과 여부는 정확한 PR head의 CI 로그로 확인해야 합니다.

## 남은 개발

고정 진행률 기준 #17은 통합 경로 보고서와 자동 검증의 main 반영 후 완료됩니다. #16의 전체 진단·보고서·자동 갱신 수명은 통합 보고서 경로만 추가됐으므로 여전히 미완료입니다. 나머지 입력 계층 감사(#18), 최종 문서/공개 배포 검증(#19), 안전한 비동기 WinHTTP(#20)도 별도 작업으로 남깁니다.
