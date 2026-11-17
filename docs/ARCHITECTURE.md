# 코드 구조

기능을 바꿀 때 어디를 보면 되는지, 어떤 경계를 지켜야 하는지 정리합니다. 모든 코드는 `SecretaryOverlay` 네임스페이스 하나를 쓰고, 폴더는 찾기 쉽게 나눈 것입니다.

## 전체 흐름

```
Codex 후크 ──(SecretaryOverlay.exe --hook)──▶ 이름 있는 파이프 ──▶ PetWindow
                                                                      │
       Codex 대화 기록(.jsonl) ◀── ProgressTranscript (0.15초 간격으로 이어 읽기)
                                                                      │
                                         ProgressBoard ──▶ SpeechBubble / BubbleCard
                                                                      │
              화면 캡처 + codex app-server (선택) ──▶ 한마디 / AI 완료 요약
```

1. Codex가 이벤트(작업 시작, 도구 사용, 승인 요청, 완료 등)마다 `SecretaryOverlay.exe --hook`을 실행합니다. 후크 프로세스는 받은 정보를 파이프로 실행 중인 앱에 넘기고 바로 끝납니다.
2. 앱은 이벤트에 들어 있는 `transcript_path`를 따라가 대화 기록에서 **공개된** 진행 문구와 최종 답변만 읽습니다.
3. 질문 하나마다 `TaskProgress`가 생기고, `ProgressBoard`가 최대 3개까지 말풍선으로 보여줍니다.
4. 사용자가 켜 둔 경우에만 Codex CLI(`codex app-server`)를 임시 세션으로 불러 한마디나 완료 요약을 만듭니다.

## 폴더

| 폴더 | 주요 파일 | 하는 일 |
| --- | --- | --- |
| `src/App` | `Program`, `AppStorage`, `AtomicJsonFile`, `StateEngine`, `TemporaryFiles` | 시작과 단일 실행 확인, 설정 저장, 캐릭터 자세 상태 기계, 임시 파일 정리 |
| `src/Pet` | `PetWindow.*`, `PoseAssets`, `PoseRegistration`, `NativePopupMenu` | 캐릭터 창(partial 클래스로 나눔), 자세 이미지와 위치 맞춤, 우클릭 메뉴 |
| `src/Hooks` | `HookTransport`, `HookEventParser`, `ActivityDescription` | 파이프 송수신, 이벤트 해석, "명령 실행 중 · …" 같은 활동 한 줄 |
| `src/Progress` | `ProgressBoard.*`, `ProgressTranscript`, `ProgressParser`, `TaskProgress` | 대화 기록 이어 읽기, 공개 메시지 해석, 질문별 상태와 보관 |
| `src/Codex` | `CodexStreamingClient`, `CodexGenerationSession`, `ModelCatalog`, `ModelProfiles` | Codex CLI 프로세스 입출력, 임시 세션, 로컬 모델 목록과 선택 값 |
| `src/Commentary` | `CommentaryPolicy`, `CommentaryConversation`, `DesktopActivity`, `WindowCapture` | 한마디 실행 조건, 최근 한마디 기억, 사용자 활동과 영상 감지, 창 캡처 |
| `src/Completion` | `CompletionExcerpt`, `CompletionSummaryClient`, `CompletionSummaryCoordinator` | 완료 메시지 B안(원문 간추리기)과 D안(AI 요약) |
| `src/Bubbles` | `SpeechBubble.*`, `BubbleCard.*`, `OverflowBubble` | 말풍선 배치, 재배치 애니메이션, 자동 닫기, 채팅 열기, `다른 작업` 묶음 |
| `src/Settings` | `SettingsNavigation`, `ModelSettings`, `NumberSetting`, `PersonalizationSettings` | 설정 창의 각 컨트롤과 개인 설정 저장 |
| `src/Verification` | `SelfTest`, `*Verification`, `*Preview`, `DevelopmentCommands` | `--self-test` 자동 검사, 렌더링 미리보기 같은 개발용 명령 |
| `installer` | `Install.ps1`, `Install-Hooks.ps1`, `Build-Release.ps1` | 설치·제거, Codex 후크 등록·해제, 배포 ZIP 만들기 |

## 지켜야 할 동작

- 작업 식별자는 질문별 `TaskProgress.Key`입니다. 실제 채팅 열기에는 `Session`을 씁니다.
- 새 질문은 맨 위에 추가하지만, 내용이 갱신돼도 기존 순서는 바꾸지 않습니다. 단, 승인 요청은 맨 위로 올립니다.
- AI 완료 요약을 기다리는 질문은 자리를 유지하고 완료 말풍선은 숨깁니다. 자동 닫기 시간은 완성된 문구가 표시된 뒤부터 셉니다.
- 후크는 라우팅 정보와 활동 설명만 전달합니다. 답변 원문과 도구 출력은 전달하지 않습니다.
- `ProgressParser`와 `PublicSummaryStream`은 공개 메시지와 공개 요약만 읽습니다. 내부 추론 본문은 표시하지 않습니다.
- 화면 캡처는 한마디를 요청할 때만 만들고, 요청이 끝나면 지웁니다. 화면 내용과 답변 본문은 로그에 남기지 않습니다.
- `AtomicJsonFile`은 저장만 맡습니다. 설정값 정규화와 저장하지 않은 초안 검증은 각 저장소 쪽에 둡니다.
- `installer/*.ps1`은 ASCII 문자만 씁니다. `Install.cmd`가 실행하는 Windows PowerShell 5.1은 BOM 없는 UTF-8을 시스템 코드 페이지로 읽기 때문에, 한글이 필요하면 `[char]` 코드로 만듭니다.

## 검사

```powershell
dotnet build -c Release
.\installer\Run-SelfTest.ps1                 # 약 770개 항목
.\installer\HookScriptsVerification.ps1
.\installer\InstallVerification.ps1
```

`IntegrationBoundaryVerification`은 실제 AI 호출 없이 초기 연결, 임시 세션 확인, 텍스트 갱신, 오류와 도구 요청 거절, 후크 메타데이터 경계를 검사합니다. 실제 Codex CLI를 부르는 검사는 `--commentary-smoke`, `--completion-summary-smoke`처럼 따로 실행합니다(`src/Verification/DevelopmentCommands.cs` 참고).

자체 테스트는 `%LOCALAPPDATA%\SecretaryOverlay`의 설정 파일을 잠시 바꾸었다가 원래대로 돌려놓습니다.
