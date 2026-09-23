namespace SecretaryOverlay;

internal static class BubbleLifecycleVerification
{
    public static void Check(Action<bool, string> check)
    {
        static string Id(int value) => $"10000000-0000-4000-8000-{value:000000000000}";
        var board = new ProgressBoard();
        var oldest = board.Accept(new("UserPromptSubmit", Id(1), Turn: "one"), "test")!;
        var middle = board.Accept(new("UserPromptSubmit", Id(2), Turn: "one"), "test")!;
        var newest = board.Accept(new("UserPromptSubmit", Id(3), Turn: "one"), "test")!;
        long previousIdentity = oldest.BubbleIdentity;
        var previousQuestion = oldest;
        oldest = board.Accept(new("UserPromptSubmit", Id(1), Turn: "two"), "test")!;
        check(board.Visible.SequenceEqual([oldest, newest, middle]) && oldest.Active
              && oldest.BubbleIdentity != previousIdentity && oldest.Turn == "two" && previousQuestion.Turn == "one"
              && board.Tasks.Contains(previousQuestion) && previousQuestion.Active,
            "A new question in an already-visible session gets its own top card while the old question remains queued");
        board.Apply(new(Id(2), "one", "progress", "현재 진행 상황입니다.", DateTimeOffset.UtcNow));
        board.Accept(new("UserPromptSubmit", Id(2), Turn: "one"), "test");
        check(board.Visible.SequenceEqual([oldest, newest, middle]),
            "Content updates and repeated same-question hooks do not reorder the bubble stack");

        check(!oldest.CompletionReady, "An active question is not ready for a completion lifetime");
        board.Accept(new("Stop", Id(1), Turn: "two"), "test");
        check(oldest.CompletionReady, "A stopped question without pending summary is ready for its display lifetime");
        oldest.BeginSummary();
        oldest.StreamingReasoning = "결과를 정리하고 있습니다.";
        oldest.StreamingCompletion = "완료 문구를 작성하고 있습니다.";
        check(!oldest.CompletionReady,
            "AI reasoning and partial completion text remain ineligible while their summary request is pending");
        oldest.FinishSummary();
        check(oldest.CompletionReady, "Finishing a summary makes the completed question eligible again");
        oldest = board.Accept(new("UserPromptSubmit", Id(1), Turn: "three"), "test")!;
        board.Accept(new("Interrupt", Id(1), Turn: "three"), "test");
        check(oldest.CompletionReady && oldest.State == "Interrupt",
            "An interrupted task also becomes eligible after its final visible message");

        var queue = new ProgressBoard();
        for (int i = 1; i <= 5; i++) queue.Accept(new("UserPromptSubmit", Id(i), Turn: "one"), "test");
        var queued = queue.Tasks.Single(task => task.Session == Id(1));
        long queuedIdentity = queued.BubbleIdentity;
        DateTimeOffset updated = queued.UpdatedAt;
        queued.Body = "대기 목록에서도 작업은 계속 진행 중입니다.";
        check(queue.Promote(queued.Key) && ReferenceEquals(queue.Visible[0], queued) && queued.Active
              && queued.BubbleIdentity == queuedIdentity && queued.Turn == "one"
              && queued.State == "UserPromptSubmit" && queued.UpdatedAt == updated
              && queued.Body == "대기 목록에서도 작업은 계속 진행 중입니다.",
            "Manually showing an overflow task changes only its display position without restarting the task or identity");
        var order = queue.Tasks.ToArray();
        check(!queue.Promote(Id(999)) && queue.Tasks.SequenceEqual(order),
            "A stale overflow selection cannot create a task or change the remaining order");

        var ending = new ProgressBoard { Style = CompletionStyle.D };
        var ended = ending.Accept(new("UserPromptSubmit", Id(10), Turn: "one"), "test")!;
        ending.Apply(new(Id(10), "one", "final", "설정을 변경했습니다.", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
        ending.Accept(new("Stop", Id(10), Turn: "one"), "test");
        ended.NaturalCompletion = new("AI가 만든 완료 요약입니다.", "응답 완료");
        ending.Accept(new("SessionEnd", Id(10), Turn: "one"), "test");
        ending.RefreshCompletions();
        check(ended.Body == "수고하셨습니다." && ended.Detail == "채팅 종료"
              && ended.CompletionReady && ended.NaturalCompletion is null,
            "Session end displays the exact farewell regardless of a previously cached completion summary");
        check(!ending.Apply(new(Id(10), "one", "late-final", "늦은 최종 답변입니다.", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer))
              && !ending.Apply(new(Id(10), "one", "late-summary", "늦은 추론 요약입니다.", DateTimeOffset.UtcNow, ProgressKind.Summary))
              && ended.Body == "수고하셨습니다.",
            "Late final answers and public reasoning summaries cannot replace the session farewell");
        ending.Accept(new("Stop", Id(10), Turn: "one"), "test");
        ending.Accept(new("PreToolUse", Id(10), Turn: "one"), "test");
        check(ended.State == "SessionEnd" && ended.CompletionReady && ended.Body == "수고하셨습니다.",
            "Delayed hooks cannot reopen an ended session without a new user prompt");
        var reopened = ending.Accept(new("UserPromptSubmit", Id(10), Turn: "two"), "test")!;
        check(reopened.Active && !reopened.CompletionReady && reopened.Body == "생각하고 있어요." && ended.Body == "수고하셨습니다.",
            "A new question starts fresh work while the previous session farewell keeps its own card");

        Task.Run(() => CheckSummaryCancellationAsync(check)).GetAwaiter().GetResult();
    }

    private static async Task CheckSummaryCancellationAsync(Action<bool, string> check)
    {
        const string session = "20000000-0000-4000-8000-000000000001";
        var board = new ProgressBoard { Style = CompletionStyle.D };
        var task = board.Accept(new("UserPromptSubmit", session, Turn: "one"), "test")!;
        board.Apply(new(session, "one", "final", "말풍선 설정을 적용했습니다.", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
        var completion = new TaskCompletionSource<CompletionPresentation>();
        Action<string>? publishAnswer = null, publishReasoning = null;
        CancellationToken requestToken = default;
        int requests = 0;
        var coordinator = new CompletionSummaryCoordinator((_, token, answer, reasoning) =>
        {
            requests++;
            requestToken = token;
            publishAnswer = answer;
            publishReasoning = reasoning;
            return completion.Task;
        });
        void Refresh() => coordinator.Refresh(board, true, CancellationToken.None, () => { });
        board.Accept(new("Stop", session, Turn: "one"), "test");
        Refresh();
        publishReasoning!("결과를 정리하고 있습니다.");
        publishAnswer!("말풍선을 수정했습니다.");
        check(requests == 1 && task.SummaryPending && !task.CompletionReady,
            "Entering D summary mode keeps the lifetime unready throughout its partial output");

        board.Accept(new("SessionEnd", session, Turn: "one"), "test");
        publishAnswer("채팅 종료 뒤에 도착한 요약입니다.");
        publishReasoning("채팅 종료 뒤에 도착한 추론 요약입니다.");
        check(task.Body == "수고하셨습니다." && task.StreamingCompletion.Length == 0
              && task.StreamingReasoning.Length == 0 && task.CompletionReady,
            "A session end rejects in-flight summary callbacks even before the coordinator's next refresh");
        Refresh();
        completion.SetResult(new("뒤늦은 AI 최종 요약입니다.", "응답 완료"));
        await Task.Yield();
        Refresh();
        check(requestToken.IsCancellationRequested && requests == 1 && task.Body == "수고하셨습니다."
              && task.NaturalCompletion is null && task.CompletionReady,
            "Session end cancels an existing D request and never replaces its farewell with the late result");

        var cold = new ProgressBoard { Style = CompletionStyle.D };
        cold.Accept(new("UserPromptSubmit", session, Turn: "two"), "test");
        cold.Apply(new(session, "two", "final", "최종 답변이 있습니다.", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
        cold.Accept(new("SessionEnd", session, Turn: "two"), "test");
        coordinator.Refresh(cold, true, CancellationToken.None, () => { });
        check(requests == 1 && cold.Visible[0].Body == "수고하셨습니다.",
            "An ended session never starts a completion-summary request even when a final answer is cached");
    }
}
