using System.Text.Json;
using System.Windows;

namespace SecretaryOverlay;

internal static class ReadableBubbleVerification
{
    internal const string Result = "말풍선 표시를 정리했습니다.";
    internal static readonly string[] Directives =
    [
        ":codex-annotation{index=\"1\"}",
        ":codex-followup[계속하기]{prompt=\"숨길 동작\"}",
        "::code-comment{title=\"검토\" body=\"숨길 내용 } 와 {, \\\"따옴표\\\"\" file=\"C:/private/file.cs\"}",
        "::created-thread{threadId=\"hidden-id\"}",
        "::created-thread{clientThreadId=\"pending-id\"}",
        ":codex-future-widget[label]{payload={nested:1}}",
        "::code-comment{\n title=\"숨길 제목\"\n body=\"실패했어요. 내부 의견입니다.\"\n file=\"C:/private/a.cs\"\n}"
    ];

    public static void Check(Action<bool, string> check)
    {
        foreach (string directive in Directives)
        {
            check(ProgressDisplayText.Clean(Result + " " + directive + " 다음 문장입니다.") == Result + " 다음 문장입니다.",
                "A dedicated display directive is fully hidden: " + directive.Split('{', '[')[0]);
            bool hidden = true;
            for (int i = 2; i <= directive.Length; i++)
                hidden &= ProgressDisplayText.Clean(Result + " " + directive[..i]) == Result;
            check(hidden, "Every streaming boundary hides the directive, label and attributes: " + directive.Split('{', '[')[0]);
        }
        check(ProgressDisplayText.Clean(string.Join('\n', Directives)) == "",
            "A response containing only UI directives has no bubble content");
        check(ProgressDisplayText.Clean("설명 `" + Directives[0] + "` 다음 내용") == "설명 다음 내용",
            "A hidden directive does not leave empty inline-code punctuation behind");
        check(ProgressDisplayText.Clean(Result + "\n- " + Directives[1] + "\n\n다만 실행은 하지 못했습니다.")
            == Result + "\n\n다만 실행은 하지 못했습니다.",
            "Removing a follow-up button also removes its empty bullet while preserving limitations");
        check(ProgressDisplayText.Clean("### **진행 상황**\n- *위치*를 확인합니다.\n1. `my_file.cs` 수정\n> 다음 설명입니다.")
            == "진행 상황\n위치를 확인합니다.\nmy_file.cs 수정\n다음 설명입니다.",
            "Common headings, emphasis, lists, quotes and inline code become readable text");
        const string link = "[설정 파일](<C:/private/folder (copy)/settings.json>)";
        check(ProgressDisplayText.Clean(link + "을 수정했습니다.") == "설정 파일을 수정했습니다.",
            "A link keeps its label but hides its entire destination, including nested parentheses");
        bool destinationsHidden = true;
        for (int i = link.IndexOf('(') + 1; i <= link.Length; i++)
            destinationsHidden &= ProgressDisplayText.Clean(link[..i]) == "설정 파일";
        check(destinationsHidden, "A local link destination never leaks as it streams in");
        check(ProgressDisplayText.Clean("[문서][doc]\n[doc]: https://example.test/private\n![결과 이미지](C:/private/image.png)") == "문서\n결과 이미지",
            "Reference links and images retain their labels without exposing destinations");
        check(ProgressDisplayText.Clean(Result + "\n```csharp\n비공개 구현 내용\n```\n다음 설명입니다.") == Result + "\n다음 설명입니다.",
            "Implementation code blocks are omitted without dropping the following explanation");
        check(ProgressDisplayText.Clean(Result + "\n```csharp\n아직 입력 중인 코드") == Result,
            "An unfinished code block does not leak during streaming");
        const string literal = "C:\\work\\my_file.cs, --flag, x * y, a ** b, foo_bar, 상태: 확인";
        check(ProgressDisplayText.Clean(literal) == literal,
            "Meaningful file names, plain paths, command flags and arithmetic remain intact");
        string mixed = "## **결과**\n" + Result + " " + Directives[0] + "\n- " + Directives[1] + "\n" + link;
        check(ProgressDisplayText.Clean(ProgressDisplayText.Clean(mixed)) == ProgressDisplayText.Clean(mixed),
            "Cleaning through multiple display paths does not change the result");
        check(CompletionExcerpt.Extract(Result + "\n" + Directives[6] + "\n다만 배포하지 못했습니다.")
            == Result + " 다만 배포하지 못했습니다.",
            "Multiline code-review metadata never becomes a completion result or caveat");
        string prompt = CompletionSummaryClient.CreatePrompt(Result + "\n" + string.Join('\n', Directives));
        using var data = JsonDocument.Parse(prompt[(prompt.LastIndexOf('\n') + 1)..]);
        string source = data.RootElement.GetProperty("finalAnswer").GetString()!;
        check(source.Contains(Result) && !source.Contains("codex-") && !source.Contains("private") && !source.Contains("숨길"),
            "AI completion summaries receive the result without UI action metadata");

        var card = new BubbleCard(() => { });
        card.SetPetName("나의 비서");
        card.Update("프로젝트", mixed, "검사 완료", "현재 채팅");
        string opened = "";
        const string session = "11111111-1111-4111-8111-111111111111";
        card.SetNavigation(session, true, value => opened = value);
        card.OpenChat.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        check(card.Body.Text == "결과\n" + Result + "\n\n설정 파일" && card.PetName.Text == "나의 비서"
            && card.Project.Text == "프로젝트" && card.ChatTitle.Text == "현재 채팅" && card.Detail.Text == "검사 완료",
            "Real bubble keeps its name, project, chat and activity with only readable body text");
        check(card.OpenChat.Visibility == Visibility.Visible && opened == session,
            "Hiding text directives preserves the real completion chat button and destination");
        CheckAmbient(check);
    }

    private static void CheckAmbient(Action<bool, string> check)
    {
        var owner = new Window { Width = 100, Height = 100, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        SpeechBubble? bubble = null;
        try
        {
            owner.Show();
            bubble = new SpeechBubble(owner, animateReflow: () => false) { Opacity = 0 };
            long version = bubble.BeginAmbientStream();
            check(bubble.UpdateAmbientStream(version, Directives[1]) && bubble.VisibleCards.Count == 0,
                "A directive-only stream stays valid without creating an empty ambient bubble");
            bubble.UpdateAmbientStream(version, Result);
            check(bubble.UpdateAmbientStream(version, Directives[2]) && bubble.VisibleCards.Single().Body.Text == Result,
                "A hidden-only update cannot erase the last useful ambient explanation");
            check(!bubble.CompleteAmbientStream(version, Directives[1]) && bubble.VisibleCards.Count == 0
                && !bubble.UpdateAmbientStream(version, "늦은 내용"),
                "An empty final result clears the pending ambient stream instead of leaving it active forever");
        }
        finally { bubble?.Close(); owner.Close(); }
    }
}
