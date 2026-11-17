# Codex 비서 펫 (Secretary Overlay)

[![CI](https://github.com/danyoon0760/codex-secretary-overlay/actions/workflows/ci.yml/badge.svg)](https://github.com/danyoon0760/codex-secretary-overlay/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/danyoon0760/codex-secretary-overlay)](https://github.com/danyoon0760/codex-secretary-overlay/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Windows 화면에 캐릭터와 말풍선을 띄워 Codex 작업이 진행 중인지, 끝났는지, 승인을 기다리는지 알려주는 데스크톱 앱입니다. 원하면 지금 보고 있는 창을 보고 캐릭터가 한마디 하게 할 수도 있습니다.

> A Windows desktop pet that shows Codex task progress, completion and approval requests as speech bubbles. The UI is in Korean.

## 설치

**Windows 10 2004 이상 또는 Windows 11의 x64 PC**와 Codex 데스크톱 앱이 필요합니다. AI 한마디와 AI 완료 요약에는 Codex CLI가 필요합니다. 배포 ZIP에 .NET 런타임이 들어 있어서 .NET을 따로 설치하지 않아도 됩니다.

1. [Releases](https://github.com/danyoon0760/codex-secretary-overlay/releases/latest)에서 `SecretaryOverlay-win-x64.zip`을 받습니다.
2. ZIP을 풀고 `Install.cmd`를 실행합니다. 앱은 `%LOCALAPPDATA%\Programs\SecretaryOverlay`에 설치되고 바탕화면에 **비서 펫** 바로 가기가 생깁니다. 관리자 권한은 필요 없습니다.
3. Codex **설정 → Hook**에서 새로 등록된 `Secretary pet` 후크 12개를 확인하고 신뢰 처리합니다. 새 PC에서 한 번만 하면 됩니다.

이제 Codex에서 작업을 시작하면 말풍선이 나타납니다. 캐릭터나 작업 표시줄 트레이의 비서 펫 아이콘을 우클릭하면 설정이 열립니다. 설치한 뒤에는 앱 폴더를 옮기지 마세요. 위치를 바꾸려면 새 위치에 다시 설치하면 됩니다.

배포본에는 코드 서명이 없어서 Windows가 경고를 띄울 수 있습니다. 받은 ZIP의 SHA-256 값은 릴리스 설명과 `.sha256` 파일에서 확인할 수 있습니다.

### 업데이트

새 버전 ZIP을 받아 `Install.cmd`를 다시 실행하면 됩니다. 실행 중인 앱을 먼저 종료하고 덮어쓰며, 설정은 그대로 유지됩니다.

## 기능

- **작업 진행 말풍선**: 최근 작업 세 개의 진행 상황을 보여줍니다. 공개된 진행 문구와 지금 하는 일 한 줄(`명령 실행 중 · dotnet build…`, `편집함 · App.cs +2 -1`)이 표시됩니다.
- **승인 대기 우선**: 승인이 필요한 작업은 맨 위로 올라옵니다. 화면 밖으로 밀린 승인 요청은 `다른 작업`에 건수로 표시됩니다.
- **채팅 열기**: 완료나 승인 대기 말풍선에서 원래 Codex 채팅으로 바로 갈 수 있습니다. 승인을 대신 처리하지는 않습니다.
- **완료 메시지**: 최종 답변에서 결과 문장을 간추려 보여주거나(B안, AI 호출 없음), AI로 짧게 요약합니다(D안).
- **화면 보고 말하기**: 지금 쓰는 창을 보고 캐릭터가 한마디 합니다. 자동 실행(기본 20분 간격)과 직접 요청 모두 같은 모델 설정을 씁니다.
- **꾸미기**: 드래그로 이동, 마우스 휠로 크기 조절, 말풍선 폭과 글자 크기, 캐릭터 이름 같은 개인 설정을 바꿀 수 있습니다.

말풍선의 `×`는 화면에서만 닫고, Codex 작업은 계속됩니다. AI 기능은 Codex 사용량을 씁니다.

## 데이터와 권한

- 설정과 진단 기록은 `%LOCALAPPDATA%\SecretaryOverlay`에 저장됩니다.
- 후크는 작업 식별자와 상태만 전달합니다. 진행 문구는 이 PC의 대화 기록에서 공개된 부분만 읽습니다.
- 화면 캡처는 한마디 기능을 쓸 때만 만들어 로컬 Codex CLI에 넘기고, 요청이 끝나면 지웁니다. 답변 본문과 화면 내용은 로그에 남기지 않습니다.
- 설치 스크립트는 Codex `hooks.json`을 백업한 뒤 이 앱의 후크만 추가합니다. 다른 후크는 그대로 두고, 신뢰 처리도 자동으로 하지 않습니다.

## 제거

설치 폴더의 `Uninstall.cmd`를 실행하면 후크, 바로 가기, 앱 파일이 지워집니다. 개인 설정까지 지우려면 `%LOCALAPPDATA%\SecretaryOverlay` 폴더도 삭제하세요.

## 개발

.NET 10 SDK가 필요합니다. 코드 구조와 지켜야 할 동작은 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)에 정리돼 있습니다.

```powershell
dotnet build -c Release
.\installer\Run-SelfTest.ps1                 # 앱 내장 자체 테스트
.\installer\HookScriptsVerification.ps1      # 후크 등록·해제
.\installer\InstallVerification.ps1          # 설치 스크립트
.\installer\Build-Release.ps1                # dist\SecretaryOverlay-win-x64.zip
```

```
src/
  App/          시작, 설정 저장, 상태 기계
  Pet/          캐릭터 창
  Hooks/        Codex 후크 수신
  Progress/     대화 기록 읽기, 작업별 상태
  Codex/        Codex CLI 호출, 모델 목록
  Commentary/   화면 보고 말하기
  Completion/   완료 메시지 요약
  Bubbles/      말풍선
  Settings/     설정 화면
  Verification/ 자체 테스트와 개발용 명령
installer/      설치·제거·배포 스크립트
assets/         캐릭터 이미지와 글꼴
```

### 릴리스

1. `SecretaryOverlay.csproj`의 `<Version>`을 올리고 `CHANGELOG.md`에 같은 버전 항목을 추가합니다.
2. `main`에 push한 뒤 `v<버전>` 태그를 push합니다. GitHub Actions가 테스트를 돌리고 ZIP을 만들어 릴리스에 올립니다.

## 기여

이슈와 PR을 환영합니다. PR을 열면 CI가 빌드, 자체 테스트, 설치 스크립트 검사를 실행합니다. 동작을 바꾸면 `src/Verification`에 검사를 함께 추가해 주세요.

## 라이선스

[MIT](LICENSE). 글꼴(`assets/fonts`)은 SIL Open Font License 1.1을 따릅니다.

이 프로젝트는 OpenAI가 만들거나 보증한 공식 앱이 아닙니다.
