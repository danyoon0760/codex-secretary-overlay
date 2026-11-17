namespace SecretaryOverlay;

internal static class ProgressRetentionVerification
{
    private static string Id(int value) => $"00000000-0000-4000-8000-{value:000000000000}";
    private static TaskProgress Start(ProgressBoard board, int value, string turn = "one") =>
        board.Accept(new("UserPromptSubmit", Id(value), Turn: turn), "test")!;
    private static TaskProgress? Stop(ProgressBoard board, int value, string turn = "one") =>
        board.Accept(new("Stop", Id(value), Turn: turn), "test");

    public static void Check(Action<bool, string> check)
    {
        var board = new ProgressBoard();
        for (int i = 1; i <= ProgressBoard.RetainedTaskLimit + 1; i++) Start(board, i);
        check(board.Tasks.Count == ProgressBoard.RetainedTaskLimit + 1 && board.Tasks.All(t => t.Active),
            "The retention target never rejects or discards an active task");
        var stopped = Stop(board, 1);
        check(stopped is not null && !stopped.Active && !board.Tasks.Contains(stopped)
            && board.Tasks.Count == ProgressBoard.RetainedTaskLimit && board.Tasks.All(t => t.Active),
            "A hidden task can finish and be retired in the same accepted hook");
        check(board.Accept(new("Stop", Id(1), Turn: "one"), "") is null
            && board.Accept(new("PostToolUse", Id(1), Turn: "one"), "") is null
            && board.Accept(new("UserPromptSubmit", Id(1), Turn: "one"), "") is null
            && board.Accept(new("Stop", Id(1)), "") is null,
            "Late hooks and a duplicate old prompt cannot recreate a retired task, including a Stop with no turn");
        var resumed = Start(board, 1, "two");
        check(resumed is not null && resumed.Active && resumed.Turn == "two" && board.Visible[0] == resumed,
            "A new prompt with a different turn restores a retired session at the top");
        check(board.Accept(new("UserPromptSubmit", Id(1), Turn: "one"), "") is null
            && resumed!.Turn == "two" && resumed.Active,
            "An old retired prompt cannot rewind a session after its newer turn has reopened");
        var legacy = board.Accept(new("UserPromptSubmit", Id(1)), "");
        check(!ReferenceEquals(resumed, legacy) && legacy!.Turn == "" && legacy.Active && resumed!.Turn == "two",
            "A legacy prompt creates its own question without clearing the previous turn identity");
        check(!board.IsRetiredEvent(new("PreToolUse", Id(1), Turn: "two"))
            && !board.IsRetiredEvent(new("Stop", Id(1), Turn: "two"))
            && !board.IsRetiredEvent(new("Stop", Id(1), Turn: "two", Preview: true)),
            "Retained older questions can still receive their own hooks and previews remain available");
        check(board.Apply(new(Id(1), "two", "late-final", "이전 질문의 답변", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer))
            && legacy!.FinalAnswer == "" && board.Accept(new("Stop", Id(1), Turn: "two"), "") == resumed && legacy.Active,
            "An unidentified new prompt is isolated from the known previous question's late answer and stop");
        board.Accept(new("PreToolUse", Id(1), Turn: "three"), "");
        check(legacy!.Turn == "three" && legacy.FinalAnswer == ""
            && board.Apply(new(Id(1), "three", "new-final", "새 질문의 답변", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer)),
            "A later identified hook binds the new question without retaining the previous answer");

        CheckVisibleProtection(check);
        CheckBoundedHistory(check);
        CheckSummaryIsolation(check);
    }

    private static void CheckVisibleProtection(Action<bool, string> check)
    {
        var board = new ProgressBoard();
        for (int i = 1; i <= ProgressBoard.RetainedTaskLimit; i++) Start(board, i);
        var older = Stop(board, 1)!;
        var newer = Stop(board, 2)!;
        var visibleFirst = Stop(board, ProgressBoard.RetainedTaskLimit)!;
        var visibleSecond = Stop(board, ProgressBoard.RetainedTaskLimit - 1)!;
        var epoch = DateTimeOffset.UnixEpoch;
        older.UpdatedAt = epoch;
        newer.UpdatedAt = epoch.AddMinutes(1);
        visibleFirst.UpdatedAt = visibleSecond.UpdatedAt = epoch.AddMinutes(-1);
        Start(board, ProgressBoard.RetainedTaskLimit + 1);
        check(!board.Tasks.Contains(older) && board.Tasks.Contains(newer)
            && board.Visible.Contains(visibleFirst) && board.Visible.Contains(visibleSecond),
            "Retention selects the oldest hidden inactive task while protecting even older visible completions");

        var overflow = new ProgressBoard();
        for (int i = 1; i <= ProgressBoard.RetainedTaskLimit + 3; i++) Start(overflow, i);
        foreach (var task in overflow.Visible.ToArray()) overflow.Accept(new("Stop", task.Session, Turn: task.Turn), "");
        var before = overflow.Visible;
        check(overflow.Tasks.Count == ProgressBoard.RetainedTaskLimit + 3 && before.All(t => !t.Active),
            "Three visible completions stay on screen even when active tasks keep storage above its target");
        overflow.Dismiss(before[0].Key);
        check(!overflow.Tasks.Contains(before[0]) && overflow.Tasks.Count == ProgressBoard.RetainedTaskLimit + 2
            && overflow.Visible[0] == before[1] && overflow.Visible[1] == before[2] && overflow.Visible[2].Active,
            "Closing a visible completion retires it and promotes the queue without deleting active work");
    }

    private static void CheckBoundedHistory(Action<bool, string> check)
    {
        var board = new ProgressBoard();
        int total = ProgressBoard.RetainedTaskLimit + ProgressBoard.RetiredTurnLimit + 20;
        for (int i = 1; i <= total; i++)
        {
            Start(board, i);
            Stop(board, i)!.UpdatedAt = DateTimeOffset.UnixEpoch.AddSeconds(i);
        }
        check(board.Tasks.Count == ProgressBoard.RetainedTaskLimit
            && board.RetiredTurnCount == ProgressBoard.RetiredTurnLimit,
            "Both finished-task storage and retired-turn protection remain bounded during a long session");
        int lastRetired = total - ProgressBoard.RetainedTaskLimit;
        check(board.Accept(new("Stop", Id(lastRetired), Turn: "one"), "") is null,
            "The newest retired turn remains protected after older tombstones are trimmed");

        var legacyBoard = new ProgressBoard();
        Start(legacyBoard, 1, "");
        for (int i = 2; i <= ProgressBoard.RetainedTaskLimit + 1; i++) Start(legacyBoard, i);
        Stop(legacyBoard, 1, "");
        check(legacyBoard.Accept(new("Stop", Id(1)), "") is null
            && legacyBoard.Accept(new("PostToolUse", Id(1)), "") is null,
            "A retired legacy session cannot be revived by hooks that omit their turn");
        var reopened = Start(legacyBoard, 1, "");
        check(reopened is not null && reopened.Active && reopened.Turn == "" && legacyBoard.Visible[0] == reopened,
            "A new legacy UserPromptSubmit may deliberately reopen its retired session");
    }

    private sealed class InlineContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    private static void CheckSummaryIsolation(Action<bool, string> check)
    {
        // Drive fake completions synchronously, independent of the desktop dispatcher or timing.
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineContext());
        try
        {
            var board = new ProgressBoard { Style = CompletionStyle.D };
            var original = Start(board, 1_000);
            board.Apply(new(original.Session, "one", "final-one", "첫 작업을 마쳤어요.", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
            Stop(board, 1_000);
            var results = new List<TaskCompletionSource<CompletionPresentation>>();
            var callbacks = new List<Action<string>>();
            var tokens = new List<CancellationToken>();
            var coordinator = new CompletionSummaryCoordinator((_, token, progress) =>
            {
                tokens.Add(token); callbacks.Add(progress);
                var result = new TaskCompletionSource<CompletionPresentation>();
                results.Add(result);
                return result.Task;
            });
            void Refresh() => coordinator.Refresh(board, true, CancellationToken.None, () => { });
            Refresh();
            callbacks[0]("첫 작업 요약 중");
            for (int i = 1; i <= ProgressBoard.RetainedTaskLimit; i++) Start(board, 1_000 + i);
            string retiredBody = original.Body;
            callbacks[0]("이미 정리된 작업에 늦게 도착한 글");
            check(!board.Tasks.Contains(original) && original.Body == retiredBody,
                "A retired task rejects in-flight summary chunks even before coordinator refresh");
            Refresh();
            check(tokens[0].IsCancellationRequested && !original.SummaryPending,
                "Refreshing after retention cancels the evicted task's pending summary");

            var reopened = Start(board, 1_000, "two");
            board.Apply(new(reopened.Session, "two", "final-two", "새 작업을 마쳤어요.", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
            Stop(board, 1_000, "two");
            Refresh();
            results[0].SetResult(new("옛 작업의 늦은 완료", "완료"));
            callbacks[0]("옛 작업의 뒤늦은 글");
            callbacks[1]("새 작업 요약 중");
            check(reopened.SummaryPending && reopened.StreamingCompletion == "새 작업 요약 중" && reopened.WaitingForSummary && reopened.Body == "" && reopened.NaturalCompletion is null,
                "An old request's completion and cleanup cannot replace or cancel a reopened session's new request");
            results[1].SetResult(new("새 작업 요약 완료", "완료"));
            check(reopened.NaturalCompletion?.Body == "새 작업 요약 완료" && !reopened.SummaryPending
                && reopened.Body == "새 작업 요약 완료",
                "The reopened session still finishes its own summary after the old request resolves");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}
