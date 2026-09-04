# 관리자 강제 승인 정책 검증

이 문서는 `%ProgramData%\WLAN Live Path Tester KO\targets.json`의 관리자 강제 정책을 실제 Windows 11 PC에서 확인하는 절차입니다.

## 준비

1. `config\targets.example.json`을 복사합니다.
2. 실제 승인된 내부 URL 한 개와 외부 URL 1~4개를 입력합니다.
3. `enforceApprovedTargets`를 `true`로 설정합니다.
4. 다음 위치에 저장합니다.

```text
%ProgramData%\WLAN Live Path Tester KO\targets.json
```

5. 일반 사용자는 읽기만 가능하고 SYSTEM·Administrators만 수정할 수 있도록 폴더 ACL을 적용합니다.

## 정상 정책

- [ ] 프로그램 시작 시 상태에 `관리자 강제 승인 정책`이 표시된다.
- [ ] ProgramData 파일 전체 경로는 화면에 노출되지 않는다.
- [ ] 내부 URL과 외부 URL이 승인 설정값으로 채워진다.
- [ ] 고급 수동 URL 입력 체크박스가 비활성화된다.
- [ ] URL·최대 수신량·제한 시간·스트림·리다이렉트 제한을 직접 바꿀 수 없다.
- [ ] HEAD 사전검사가 활성화되고 편집할 수 없다.
- [ ] 내부 측정은 DIRECT가 아니면 요청 전에 차단된다.
- [ ] 외부 측정은 PROXY가 아니면 요청 전에 차단된다.
- [ ] 허용된 URL은 측정 버튼을 누르기 전까지 네트워크 요청을 만들지 않는다.

## 우선순위

ProgramData 정책이 있는 상태에서 사용자와 Portable 설정도 함께 둡니다.

- [ ] ProgramData 설정만 적용된다.
- [ ] 사용자·Portable 설정의 URL은 화면에 적용되지 않는다.
- [ ] ProgramData의 `enforceApprovedTargets: false`에서는 선택형 승인 목록으로 동작한다.
- [ ] 사용자·Portable 설정에서 `enforceApprovedTargets: true`를 사용하면 해당 설정을 거부하고 관리자 강제 정책으로 오인하지 않는다.

## fail-closed 동작

ProgramData 설정을 한 항목씩 고의로 손상한 뒤 `승인 대상 다시 불러오기`를 실행합니다.

- [ ] 잘못된 JSON에서 내부·외부 측정 버튼이 비활성화된다.
- [ ] 알 수 없는 JSON 속성에서 측정이 차단된다.
- [ ] `schemaVersion` 불일치에서 측정이 차단된다.
- [ ] 중복 URL에서 측정이 차단된다.
- [ ] 외부 대상에 사설 IP를 입력하면 측정이 차단된다.
- [ ] 범위를 벗어난 수신량·시간·스트림·리다이렉트 설정에서 측정이 차단된다.
- [ ] 설정 오류 상태에서 브라우저 관찰과 WLAN 조회는 계속 사용할 수 있다.
- [ ] 브라우저 관찰을 끝낸 뒤에도 다운로드 측정 버튼이 다시 활성화되지 않는다.
- [ ] 오류를 수정하고 다시 불러오면 승인된 다운로드 측정만 복구된다.

## 파일 경계

- [ ] UTF-8 또는 UTF-8 BOM 설정 파일을 읽을 수 있다.
- [ ] 잘못된 UTF-8 설정 파일은 거부된다.
- [ ] 빈 파일은 거부된다.
- [ ] 1MiB를 넘는 파일은 거부된다.
- [ ] 심볼릭 링크 또는 reparse point 설정 파일은 거부된다.
- [ ] 일반 사용자가 ProgramData 설정을 수정하거나 삭제할 수 없다.
- [ ] 일반 사용자가 ProgramData 폴더와 파일을 읽을 수 있다.

## Core 우회 방지

UI를 조작하지 않고 단위 시험 또는 별도 호출 경로에서도 확인합니다.

- [ ] 승인 목록에 없는 URL은 `TARGET_VALIDATION_FAILED`로 요청 전에 차단된다.
- [ ] 승인 URL이라도 수신량·시간·스트림·리다이렉트 제한이 다르면 차단된다.
- [ ] 손상된 관리자 정책 상태에서는 임의 URL이 모두 차단된다.
- [ ] 새 관리자 설정을 다시 읽는 정의 검증은 이전 런타임 승인 목록에 방해받지 않는다.

## 결과 기록

실제 URL, 내부 IP, 프록시 주소, PAC URL과 회사 이름은 공개 GitHub Issue에 원문으로 붙이지 않습니다. 재현 정보가 필요하면 다음만 마스킹해 기록합니다.

```text
운영체제 빌드:
배포 형태: Portable ZIP / single-file
정책 소스: ProgramData
정책 상태: 적용 / 차단
내부 경로 판정: DIRECT / Mismatch / Unknown
외부 경로 판정: PROXY / Mismatch / Unknown
오류 코드:
EDR 또는 GPO 영향:
```
