using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace SecretaryOverlay;

internal static class PerQuestionVerification
{
    private const string Session = "12345678-1111-4111-8111-111111111111";
    private static TaskProgress Start(ProgressBoard board, string turn) => board.Accept(new("UserPromptSubmit", Session, Turn: turn), "비서")!;
    private static void Text(ProgressBoard board, string turn, string text, bool final = false) =>
        board.Apply(new(Session, turn, "message", text, DateTimeOffset.UtcNow, final ? ProgressKind.FinalAnswer : ProgressKind.Commentary));

    public static void Check(Action<bool, string> check)
    {
        var board = new ProgressBoard();
        var first = Start(board, "A");
        first.ChatTitle = "한 채팅의 여러 질문";
        Text(board, "A", "첫 번째 질문을 처리하고 있습니다.");
        var second = Start(board, "B");
        second.ChatTitle = first.ChatTitle;
        check(board.Visible.SequenceEqual([second, first]) && first.Key != second.Key && first.BubbleIdentity != second.BubbleIdentity
            && first.Turn == "A" && second.QuestionNumber == 2 && first.Body == "첫 번째 질문을 처리하고 있습니다.",
            "Consecutive questions in one chat receive independent cards, identity, content and order");
        Text(board, "A", "첫 질문의 늦은 진행 설명입니다.");
        Text(board, "B", "두 번째 질문을 처리하고 있습니다.");
        check(first.Body == "첫 질문의 늦은 진행 설명입니다." && second.Body == "두 번째 질문을 처리하고 있습니다.",
            "Interleaved progress routes to the exact question instead of the latest card");
        Text(board, "A", "첫 번째 설정을 수정했습니다.", true);
        board.Accept(new("Stop", Session, Turn: "A"), "비서");
        check(first.IsCompleted && first.Body == "첫 번째 설정을 수정했습니다." && second.Active && board.HasActive,
            "An older question's delayed Stop finishes only its own card");
        board.Accept(new("PreToolUse", Session, Turn: "A"), "비서");
        check(first.IsCompleted && first.Body == "첫 번째 설정을 수정했습니다." && second.Active,
            "A delayed tool-start hook cannot resurrect an already completed older question");
        Start(board, "A");
        check(board.Visible.SequenceEqual([second, first]) && first.IsCompleted,
            "A duplicate older prompt neither resurrects its completion nor reorders the stack");
        check(board.IsDuplicatePrompt(new("UserPromptSubmit", Session, Turn: "A"))
            && !board.IsDuplicatePrompt(new("UserPromptSubmit", Session, Turn: "C")),
            "Repeated prompt hooks are rejected before they can move the character focus back to an older question");
        check(board.IsAmbiguousEvent(new("Stop", Session)) && board.Accept(new("Stop", Session), "비서") is null && second.Active,
            "An unidentified Stop cannot select between multiple questions or affect the character focus");
        board.Promote(first.Key);
        check(board.Latest(Session) == second && board.Visible[0] == first,
            "Promoting an old card does not change which question is chronologically newest");
        var third = Start(board, "C");
        var fourth = Start(board, "D");
        check(board.Visible.SequenceEqual([fourth, third, first]) && board.Tasks.Count == 4 && fourth.QuestionNumber == 4,
            "A fourth question in one chat counts toward the same three-card display limit");
        string selected = "";
        var menu = OtherTaskMenu.Create(board.Tasks, key => selected = key)!;
        menu.Children![0].Action!();
        check(selected == second.Key && menu.Children[0].Label.Contains("질문 2"),
            "Overflow choices distinguish questions from the same chat and select the exact question key");
        board.Dismiss(first.Key);
        check(first.Dismissed && !second.Dismissed && !third.Dismissed && !fourth.Dismissed,
            "Closing one question never closes the other questions in its chat");
        check(!board.Promote(Session), "A chat ID cannot ambiguously promote one of its question cards");
        var unknown = Start(board, "");
        board.Accept(new("PreToolUse", Session, Turn: "E"), "비서");
        check(unknown.Turn == "E" && board.Latest(Session) == unknown && fourth.Turn == "D",
            "A later identified hook binds an unidentified new question without changing earlier questions");
        board.Accept(new("SessionEnd", Session), "비서");
        check(board.Tasks.All(t => !t.Active) && unknown.Body == "수고하셨습니다." && first.Body == "첫 번째 설정을 수정했습니다.",
            "Closing the chat settles its active questions while preserving earlier completed results");

        CheckWindow(check);
        CheckReaderAsync(check).GetAwaiter().GetResult();
        Task.Run(() => CheckSummariesAsync(check)).GetAwaiter().GetResult();
    }

    private static void CheckWindow(Action<bool, string> check)
    {
        var owner = new Window { Width = 280, Height = 440, Left = 600, Top = 220, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        SpeechBubble? bubble = null;
        try
        {
            owner.Show();
            long now = 0;
            var board = new ProgressBoard();
            bubble = new SpeechBubble(owner, () => now, () => false) { Opacity = 0 };
            void Render() { bubble.SetTasks(board.Tasks.Where(t => !t.Dismissed).ToArray()); bubble.UpdateLayout(); }
            bubble.TaskDismissed += key => { board.Dismiss(key); Render(); };
            bubble.TasksExpired += keys => { foreach (var key in keys) board.Dismiss(key); Render(); };
            var first = Start(board, "A"); first.Body = "첫 질문 작업 중"; Render();
            var firstCard = bubble.VisibleCards[0];
            Text(board, "A", "첫 번째 작업을 마쳤습니다.", true);
            board.Accept(new("Stop", Session, Turn: "A"), "비서");
            now = 1000; Render();
            now = 20000;
            var second = Start(board, "B"); second.Body = "두 번째 질문 작업 중"; Render();
            var secondCard = bubble.VisibleCards[0];
            check(bubble.VisibleCards.Count == 2 && ReferenceEquals(bubble.VisibleCards[1], firstCard)
                && firstCard.Body.Text == "첫 번째 작업을 마쳤습니다." && secondCard.Body.Text == second.Body,
                "The actual WPF window keeps both same-chat cards without replacing the older surface");
            string opened = "";
            bubble.ChatRequested += id => opened = id;
            firstCard.OpenChat.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check(opened == Session && firstCard.OpenChat.Visibility == Visibility.Visible,
                "An older question's completion button still opens the original chat ID, not its display key");
            now = 60999; bubble.ExpireOlderBubbles();
            check(!first.Dismissed && bubble.VisibleCards.Count == 2, "A new same-chat question does not shorten the older completion minute");
            now = 61000; bubble.ExpireOlderBubbles();
            check(first.Dismissed && second.Active && !second.Dismissed && bubble.VisibleCards.Single() == secondCard,
                "The older same-chat card expires at its own deadline without dismissing the active newer card");
            var third = Start(board, "C"); var fourth = Start(board, "D"); var fifth = Start(board, "E"); Render();
            check(bubble.VisibleCards.Count == 3 && bubble.Overflow.Count == 1 && bubble.Overflow.Text == "다른 작업 1개",
                "Four active questions in the same chat show three real cards and one overflow indicator");
            bubble.VisibleCards[1].Close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check(fourth.Dismissed && !third.Dismissed && !fifth.Dismissed && !second.Dismissed
                && bubble.Overflow.Count == 0 && bubble.VisibleCards.Count == 3,
                "Closing a middle same-chat card promotes only the queued question");
        }
        finally { bubble?.Close(); owner.Close(); }
    }

    private static async Task CheckReaderAsync(Action<bool, string> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SecretaryQuestions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "transcript.jsonl");
            await File.WriteAllTextAsync(path, "");
            var board = new ProgressBoard();
            var first = Start(board, "A");
            var reader = new ProgressTranscript(path, Session, "", first.CreatedAt.AddSeconds(-2));
            var at = DateTimeOffset.UtcNow;
            string Record(object payload) => JsonSerializer.Serialize(new { timestamp = at, type = "event_msg", payload }) + "\n";
            object Message(string turn, string text, string phase = "commentary") => new
            {
                type = "item_completed", thread_id = Session, turn_id = turn,
                item = new { type = "AgentMessage", phase, content = new[] { new { type = "Text", text } } }
            };
            await File.AppendAllTextAsync(path, Record(Message("A", "첫 질문을 처리합니다.")));
            foreach (var update in await reader.ReadUpdatesAsync(CancellationToken.None)) board.Apply(update);
            var second = Start(board, "B");
            await File.AppendAllTextAsync(path, Record(Message("B", "둘째 질문을 처리합니다."))
                + Record(Message("A", "첫 질문의 작업을 마쳤습니다.", "final_answer"))
                + Record(new { type = "task_complete", thread_id = Session, turn_id = "A", last_agent_message = "첫 질문의 작업을 마쳤습니다." }));
            foreach (var update in await reader.ReadUpdatesAsync(CancellationToken.None)) board.Apply(update);
            check(first.IsCompleted && first.Body == "첫 질문의 작업을 마쳤습니다." && second.Active && second.Body == "둘째 질문을 처리합니다.",
                "A shared transcript reader delivers a late old completion after a new question without resetting or mixing turns");
            await File.AppendAllTextAsync(path, Record(Message("B", "둘째 질문의 작업을 마쳤습니다.", "final_answer"))
                + Record(new { type = "task_complete", thread_id = Session, turn_id = "B", last_agent_message = "" }));
            foreach (var update in await reader.ReadUpdatesAsync(CancellationToken.None)) board.Apply(update);
            check(second.IsCompleted && second.Body == "둘째 질문의 작업을 마쳤습니다." && first.FinalAnswer != second.FinalAnswer,
                "A completion record without repeated answer text finishes its question without erasing the cached answer");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task CheckSummariesAsync(Action<bool, string> check)
    {
        var board = new ProgressBoard { Style = CompletionStyle.D };
        var pending = new List<TaskCompletionSource<CompletionPresentation>>();
        var publish = new List<Action<string>>();
        var tokens = new List<CancellationToken>();
        var coordinator = new CompletionSummaryCoordinator((_, token, progress) =>
        {
            var result = new TaskCompletionSource<CompletionPresentation>();
            pending.Add(result); publish.Add(progress); tokens.Add(token);
            return result.Task;
        });
        void Refresh() => coordinator.Refresh(board, true, CancellationToken.None, () => { });
        var first = Start(board, "A");
        Text(board, "A", "첫 작업을 마쳤습니다.", true); board.Accept(new("Stop", Session, Turn: "A"), "비서"); Refresh();
        var second = Start(board, "B");
        Text(board, "B", "둘째 작업을 마쳤습니다.", true); board.Accept(new("Stop", Session, Turn: "B"), "비서"); Refresh();
        publish[0]("첫 질문의 요약입니다."); publish[1]("둘째 질문의 요약입니다.");
        check(pending.Count == 2 && !tokens[0].IsCancellationRequested && first.StreamingCompletion == "첫 질문의 요약입니다." && second.StreamingCompletion == "둘째 질문의 요약입니다."
            && first.Body == "" && second.Body == "" && first.WaitingForSummary && second.WaitingForSummary,
            "Two same-chat completion summaries buffer independently without exposing unfinished question cards");
        var third = Start(board, "C"); Refresh();
        pending[0].SetResult(new("첫 질문의 최종 요약입니다.", "완료")); await Task.Yield();
        check(first.NaturalCompletion?.Body == "첫 질문의 최종 요약입니다." && third.Active && third.FinalAnswer == "" && third.Body == "생각하고 있어요.",
            "A late older summary finishes its older card without replacing the latest active question");
        board.Dismiss(second.Key); Refresh();
        publish[1]("닫힌 둘째 질문의 늦은 내용");
        pending[1].SetResult(new("닫힌 질문의 결과", "완료")); await Task.Yield();
        check(tokens[1].IsCancellationRequested && second.NaturalCompletion is null && !third.Dismissed && first.NaturalCompletion is not null,
            "Dismissing one same-chat summary cancels only that request and keeps the other question results");
    }

    public static int Render()
    {
        var rows = new StackPanel { Width = 440, Background = System.Windows.Media.Brushes.WhiteSmoke };
        foreach (var body in new[] { "세 번째 질문의 글자 크기를 조절하고 있습니다.", "두 번째 질문의 말풍선 위치를 수정했습니다.", "첫 번째 질문의 설정을 저장했습니다." })
        {
            var card = new BubbleCard(() => { });
            card.Update("비서", body, body.Contains("있습니다") ? "작업 중" : "응답 완료", "같은 채팅");
            card.Surface.Margin = new Thickness(0, 0, 0, 12);
            rows.Children.Add(card.Surface);
        }
        rows.Measure(new System.Windows.Size(440, double.PositiveInfinity));
        rows.Arrange(new Rect(0, 0, 440, rows.DesiredSize.Height)); rows.UpdateLayout();
        var bitmap = new RenderTargetBitmap(440, (int)Math.Ceiling(rows.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(rows);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(AppStorage.DataDirectory);
        using var file = File.Create(Path.Combine(AppStorage.DataDirectory, "per-question-preview.png")); encoder.Save(file);
        return 0;
    }
}
