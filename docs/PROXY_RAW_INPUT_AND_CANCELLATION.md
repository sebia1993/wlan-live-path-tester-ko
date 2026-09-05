# 프록시 원문 검증과 늦게 반환된 분석 결과

## 적용 범위

이 변경은 현재 경로 비교에 사용하는 `ProxyRouteDirectiveParser`, `ProxyDirectiveSourceSelectionPolicy`, `ProxyDirectiveRouteAnalysisExecutor`와 WPF 수동 입력 전달 경로를 보강합니다. 기존 타입을 교체하거나 새 병렬 coordinator를 만들지 않습니다.

## 원문을 먼저 검증

검증 순서는 다음과 같습니다.

```text
원문(null은 빈 문자열)
→ 원문 Length 상한 4096 확인
→ char.IsControl 제어 문자 확인
→ Trim으로 일반 앞뒤 공백 정리
→ 빈 입력과 지시문 구문 해석
```

상한은 .NET 문자열의 UTF-16 코드 단위 수이며 UTF-8 바이트 수가 아닙니다. 공백을 붙여 원문이 상한을 넘으면 정리된 내용이 짧더라도 `INPUT_TOO_LONG`입니다. 상한 안의 일반 공백은 계속 허용합니다.

탭·CR·LF·NUL·DEL·C1 제어 문자는 위치와 관계없이 `CONTROL_CHARACTER`로 거부합니다. 특히 입력 마지막의 탭·줄바꿈이나 제어 문자만 있는 입력이 Trim으로 없어져 정상 프록시 또는 DIRECT가 되는 경로를 막습니다.

전역 입력 오류는 지시문 0개와 고정 Issue code만 반환합니다. 오류 문장에 원문·호스트·자격 증명을 반사하지 않습니다.

## 출처 선택

대상별 판정이 수행됐으면 그 출처만 원문으로 파싱합니다. 성공한 대상별 판정에서 사용하지 않는 수동 설정이 잘못됐다는 이유로 정상 판정을 폐기하지 않습니다. 대상별 판정 미시도이면 설정된 수동 출처를 검증합니다.

```text
대상별 DIRECT + null/빈 문자열/일반 공백
→ 명시된 DIRECT 유지

대상별 DIRECT + 탭/CR/LF 또는 과도한 공백
→ TargetDecisionInvalid
→ 수동 프록시 fallback 없음

선택된 수동 설정에 제어 문자/길이 초과
→ ManualConfigurationInvalid
→ DIRECT 추정 없음
```

원문 파싱 결과를 재사용하므로 정리된 문자열을 다시 파싱해 검증 오류가 사라지는 경로를 만들지 않습니다. `ftp=DIRECT` 같은 적용 범위와 기존 부분 파싱 상태는 유지합니다.

## 화면 전달

경로 비교 화면의 프록시 입력은 `.Text` 그대로 coordinator에 전달합니다. 화면에서 먼저 `.Trim()`을 호출하지 않습니다. 내부/외부 대상 URL의 기존 처리는 이번 변경 범위가 아닙니다.

여러 지시문은 세미콜론으로 구분합니다. 줄바꿈이 포함된 붙여넣기는 자동 보정하지 않고 거부합니다. 원문은 기존과 같이 메모리에서만 사용합니다.

## 취소와 늦은 정상 반환

CancellationToken은 협력적 취소입니다. 분석 어댑터가 취소 요청을 받아도 예외 대신 정상 반환할 수 있습니다. Executor는 `await analyzer(...)` 직후 토큰을 다시 확인하고, 이미 취소됐으면 반환값을 `Completed`로 게시하지 않습니다.

```text
분석 콜백 실행
→ 사용자 취소
→ 콜백은 아직 진행 중: Executor도 완료하지 않음
→ 콜백 실제 반환
→ 토큰 재확인
→ Canceled, HasCompletedAnalysis=false, 분석 payload 미게시
```

취소된 값 형식 결과도 성공으로 게시하지 않습니다. 취소 뒤 null 반환은 `Canceled`이고, 취소 없는 null 반환은 기존 `Failed`를 유지합니다. 완료 결과가 이미 반환된 뒤의 취소로 과거 불변 결과를 바꾸지는 않습니다.

이는 콜백이 실제 반환된 뒤 결과를 확정하기 직전의 취소 확인입니다. 네이티브 호출의 강제 중단이나 모든 명령어 사이 취소와 게시의 원자적 직렬화를 보장하는 기능은 아닙니다. 즉시 취소 가능한 WinHTTP 전송은 별도 개발 작업입니다.

## 자동 검증

`tests/WlanLivePathTester.ProxyBoundarySmoke`는 runtime NuGet 없이 Core만 참조하는 실행형 테스트입니다. 비동기 검증을 ModuleInitializer 안에서 동기 대기하지 않으며 각 그룹에 제한 시간을 적용합니다.

12개 그룹:

1. char.IsControl에 해당하는 문자 전체의 앞·뒤·단독·DIRECT 뒤 입력
2. 원문 길이 상한과 일반 공백·빈 문자열 호환
3. 대상별·수동·snapshot 출처 원문 검증
4. 선택하지 않는 출처의 오류 격리
5. 명시 DIRECT의 빈 입력과 제어 문자 입력 구분
6. Blocked·DirectOnly·Unavailable의 콜백 0회
7. 사전 취소의 콜백 0회
8. 실제 비동기 콜백이 반환할 때까지 대기하고 늦은 결과를 Canceled 처리
9. 값 형식·null의 취소 후 반환
10. 이미 완료된 결과의 불변성
11. 예외·원문·분석 payload 비노출
12. WPF 입력 코드의 원문 전달 계약

마지막 항목은 소스 검사이며 실제 붙여넣기 상호작용 테스트가 아닙니다. 기존 UiOperationSmoke는 별도로 유지합니다. 테스트는 합성 문자열·TaskCompletionSource만 사용하며 실제 DNS·프록시·HTTP를 호출하지 않습니다.

`verify-release.ps1`에 연결해 실패하면 Release 패키지 생성 전에 중단합니다. 테스트 코드 작성, CI 실행 성공, main 병합, 공개 배포는 별개 상태로 확인합니다.

## 변경하지 않는 영역과 남은 작업

별도 `ProxyEndpointParser`의 구문 정책·수신 전송 경로는 변경하지 않았습니다. 전체 입력 계층의 추가 감사, 통합 진단 보고서 연결, 반복 측정 전용 UI 상태와 취소, 네이티브 즉시 취소, 운영 문서 패키징은 후속 범위입니다.

이 변경은 외부 API·AI·로컬 AI·텔레메트리·업로드·자동 업데이트·프록시 관리 요청을 추가하지 않습니다. 회사 환경의 PAC/WPAD·407·VPN·EDR 검증은 사용자 실환경 시험으로 남습니다.
