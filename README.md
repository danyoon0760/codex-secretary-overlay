# Codex 비서 펫 (Secretary Overlay)

Windows 화면에 캐릭터와 말풍선을 띄워 Codex 작업 진행, 완료, 승인 대기를 알려주는 데스크톱 앱입니다. 선택적으로 현재 창을 보고 한마디를 생성할 수 있습니다.

## 설치

**Windows 10 2004 이상 또는 Windows 11의 x64 PC**와 Codex 데스크톱 앱이 필요합니다. AI 한마디와 AI 완료 요약에는 사용 가능한 Codex CLI가 필요합니다. 배포 ZIP에는 .NET 런타임이 포함되어 있어 별도 .NET 설치는 필요하지 않습니다.

1. [Releases](https://github.com/danyoon0760/codex-secretary-overlay/releases)에서 `SecretaryOverlay-win-x64.zip`을 받습니다.
2. ZIP을 풀고 `Install.cmd`를 실행합니다. 설치 파일은 `%LOCALAPPDATA%\Programs\SecretaryOverlay`에 복사되고 바탕화면에 **비서 펫** 바로 가기가 만들어집니다. 관리자 권한은 필요하지 않습니다.
3. Codex **설정 → Hook**에서 새로 등록된 `Secretary pet` 후크 12개를 확인하고 신뢰 처리합니다. 이 승인은 새 PC에서 한 번 필요합니다.

그 뒤 Codex에서 작업을 시작하면 말풍선이 나타납니다. 캐릭터를 우클릭하거나 작업표시줄의 비서 펫 아이콘을 우클릭해 설정을 열 수 있습니다. 설치 후 앱 폴더를 옮기지 마세요. 위치를 바꾸려면 새 위치에 다시 설치하세요.

Windows가 서명되지 않은 앱에 대한 경고를 표시할 수 있습니다. 이 배포본에는 코드 서명 인증서가 없습니다. 다운로드한 ZIP의 SHA-256 값은 릴리스 설명에서 확인할 수 있습니다.

## 주요 동작

- 최근 작업 세 개의 진행 상황을 말풍선에 표시합니다. 승인 대기 작업은 앞으로 오고, 숨겨진 승인 요청은 `다른 작업`에 건수가 표시됩니다.
- 완료 또는 승인 대기 말풍선에서 원래 Codex 채팅을 열 수 있습니다. 채팅 열기는 승인을 자동으로 처리하지 않습니다.
- 말풍선의 `×`는 표시만 닫습니다. Codex 작업은 계속됩니다.
- 캐릭터를 드래그해 이동하고 마우스 휠이나 설정에서 크기를 바꿀 수 있습니다.
- 자동 한마디, 직접 요청하는 한마디, AI 완료 요약은 각각 설정에서 제어합니다. AI 기능은 Codex 사용량을 소모할 수 있습니다.

## 데이터와 권한

설정과 진단 기록은 `%LOCALAPPDATA%\SecretaryOverlay`에 저장됩니다. 후크는 작업 식별자와 상태를 전달하며, 대화 기록의 공개 진행 문구를 로컬에서 읽습니다. 화면 캡처는 한마디 기능을 사용할 때만 생성해 로컬 Codex CLI에 전달합니다. 답변 본문과 화면 내용은 앱 오류 로그에 남기지 않습니다.

설치 스크립트는 사용자 Codex `hooks.json`을 백업한 뒤 이 앱의 후크만 등록합니다. 다른 후크는 유지합니다. 설치 스크립트는 후크를 자동으로 신뢰 처리하지 않습니다.

## 제거

설치 폴더의 `Uninstall.cmd`를 실행합니다. 후크와 바로 가기, 앱 파일이 제거됩니다. 개인 설정을 유지하지 않으려면 제거 후 `%LOCALAPPDATA%\SecretaryOverlay` 폴더도 직접 삭제할 수 있습니다.

## 소스에서 빌드

.NET 10 SDK가 필요합니다.

```powershell
dotnet build -c Release
dotnet run -c Release --no-build -- --self-test
pwsh -File .\HookScriptsVerification.ps1
pwsh -File .\InstallVerification.ps1
```

Windows x64용 자체 포함 배포본:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish\win-x64
```

글꼴은 `assets/fonts`의 SIL Open Font License 파일을 따릅니다. 이 프로젝트는 OpenAI가 제작하거나 보증한 공식 앱이 아닙니다.
