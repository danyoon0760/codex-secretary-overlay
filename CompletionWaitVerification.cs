using System.Windows;

namespace SecretaryOverlay;

internal static class CompletionWaitVerification
{
    public static void Check(Action<bool, string> check)
    {
        const string session = "aaaaaaaa-1111-4111-8111-111111111111";
        const string excerpt = "원문에서 추린 작업 결과입니다.";
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        var owner = new Window { Width = 200, Height = 300, Left = 600, Top = 200, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        SpeechBubble? bubble = null;
        try
        {
            owner.Show();
            long now = 0;
            var board = new ProgressBoard { Style = CompletionStyle.D };
            bubble = new SpeechBubble(owner, () => now, () => false) { Opacity = 0 };
            void Render() { bubble.SetTasks(board.Tasks.Where(t => !t.Dismissed).ToArray()); bubble.UpdateLayout(); }
            bubble.TasksExpired += keys => { foreach (string key in keys) board.Dismiss(key); Render(); };
            var pending = new TaskCompletionSource<CompletionPresentation>();
            Action<string>? publish = null, reasoning = null;
            int calls = 0;
            var coordinator = new CompletionSummaryCoordinator((_, _, progress, summary) =>
            { calls++; publish = progress; reasoning = summary; return pending.Task; });
            void Refresh() { coordinator.Refresh(board, true, CancellationToken.None, Render); Render(); }
            var task = board.Accept(new("UserPromptSubmit", session, Turn: "one"), "프로젝트")!;
            task.Body = "작업을 진행하고 있습니다."; Render();
            check(bubble.VisibleCards.Single().Body.Text == task.Body && !task.WaitingForSummary,
                "AI completion waiting does not hide ordinary working progress");
            board.Accept(new("Stop", session, Turn: "one"), "프로젝트"); Refresh();
            check(calls == 0 && task.WaitingForSummary && task.Body == "" && bubble.VisibleCards.Count == 0 && !bubble.IsVisible,
                "Stop before the final answer hides the old progress without flashing an original or generic completion");
            now = 100000; bubble.ExpireOlderBubbles();
            check(!task.Dismissed && !task.CompletionReady,
                "Waiting for the source answer cannot consume the completion lifetime");
            board.Apply(new(session, "one", "final", excerpt, DateTimeOffset.UtcNow, ProgressKind.FinalAnswer)); Refresh();
            check(calls == 1 && task.SummaryPending && task.Completion == excerpt && task.Body == "" && bubble.VisibleCards.Count == 0,
                "Receiving the source answer starts one summary request without ever displaying its excerpt");
            publish!("아직 작성 중인 요약"); reasoning!("생각 중인 설명"); Render();
            check(task.WaitingForSummary && task.NaturalCompletion is null && bubble.VisibleCards.Count == 0,
                "Neither partial answer tokens nor summary reasoning can reveal a waiting completion bubble");
            now = 200000; pending.SetResult(new("AI가 정리한 최종 결과입니다.", "확인 완료")); Render();
            check(!task.WaitingForSummary && task.CompletionReady && bubble.VisibleCards.Single().Body.Text == "AI가 정리한 최종 결과입니다."
                && bubble.VisibleCards[0].OpenChat.Visibility == Visibility.Visible,
                "Only the completed AI result reveals the completion bubble and original-chat button");
            now = 259999; bubble.ExpireOlderBubbles();
            check(!task.Dismissed && bubble.VisibleCards.Count == 1,
                "The final AI completion keeps its full minute regardless of time spent waiting");
            now = 260000; bubble.ExpireOlderBubbles();
            check(task.Dismissed && bubble.VisibleCards.Count == 0,
                "The completed AI result closes exactly one minute after it becomes visible");

            var next = board.Accept(new("UserPromptSubmit", session, Turn: "two"), "프로젝트")!;
            board.Apply(new(session, "two", "final", excerpt, DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
            board.Accept(new("Stop", session, Turn: "two"), "프로젝트");
            next.BeginSummary(); board.RefreshCompletions(); Render();
            next.SummaryFailed = true; next.FinishSummary(); board.RefreshCompletions(); Render();
            check(!next.WaitingForSummary && bubble.VisibleCards.Single().Body.Text == excerpt && next.Detail.Contains("실패"),
                "A failed summary still uses the explicitly labelled original-excerpt fallback");
            next.ResetSummary(); board.RefreshCompletions(); Render();
            board.Style = CompletionStyle.B; board.RefreshCompletions(); Render();
            check(!next.WaitingForSummary && next.CompletionReady && bubble.VisibleCards.Single().Body.Text == excerpt,
                "Switching to original excerpts immediately reveals the available result without waiting for AI");
            board.Style = CompletionStyle.D; board.RefreshCompletions(); Render();
            board.Accept(new("SessionEnd", session, Turn: "two"), "프로젝트"); Render();
            check(!next.WaitingForSummary && bubble.VisibleCards.Single().Body.Text == "수고하셨습니다.",
                "A session-end farewell remains immediately visible even while AI completion mode is selected");
        }
        finally { bubble?.Close(); owner.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}
