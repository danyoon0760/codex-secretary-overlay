using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SecretaryOverlay;

internal static class StreamingVerification
{
    public static void Check(Action<bool, string> check)
    {
        var updates = new List<string>();
        var summaries = new List<string>();
        var stream = new AgentTextStream("thread", updates.Add, summaries.Add);
        void Event(string method, object data)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(data)); stream.Accept(method, doc.RootElement);
        }
        Event("turn/started", new { threadId = "thread", turn = new { id = "turn" } });
        Event("item/reasoning/textDelta", new { threadId = "thread", turnId = "turn", itemId = "reason", delta = "PRIVATE" });
        Event("item/agentMessage/delta", new { threadId = "other", turnId = "turn", itemId = "item", delta = "OTHER" });
        Event("item/agentMessage/delta", new { threadId = "thread", turnId = "old-turn", itemId = "item", delta = "OLD" });
        check(updates.Count == 0 && summaries.Count == 0, "Streaming ignores raw reasoning, other threads and stale turns");
        Event("item/reasoning/summaryTextDelta", new { threadId = "other", turnId = "turn", itemId = "r", summaryIndex = 0, delta = "다른 채팅" });
        Event("item/reasoning/summaryTextDelta", new { threadId = "thread", turnId = "old-turn", itemId = "r", summaryIndex = 0, delta = "옛 질문" });
        check(summaries.Count == 0, "Public reasoning deltas cannot cross task or turn boundaries");
        Event("item/reasoning/summaryTextDelta", new { threadId = "thread", turnId = "turn", itemId = "r", summaryIndex = 0, delta = "**표시" });
        Event("item/reasoning/summaryTextDelta", new { threadId = "thread", turnId = "turn", itemId = "r", summaryIndex = 0, delta = "를 확인합니다.**" });
        check(summaries.Count == 2 && summaries[^1] == "표시를 확인합니다." && stream.Text == "",
            "Public reasoning is displayed as real chunks before the answer without contaminating final text");
        Event("item/reasoning/summaryTextDelta", new { threadId = "thread", turnId = "turn", itemId = "r", summaryIndex = 1, delta = "Checking the result" });
        check(summaries.Count == 2 && summaries[^1] == "표시를 확인합니다.", "English reasoning preserves the previous Korean explanation");
        Event("item/reasoning/summaryTextDelta", new { threadId = "thread", turnId = "turn", itemId = "r", summaryIndex = 2, delta = "다음 항목을 확인합니다." });
        Event("item/reasoning/summaryTextDelta", new { threadId = "thread", turnId = "turn", itemId = "r", summaryIndex = 0, delta = "뒤늦은 이전 항목" });
        Event("item/completed", new { threadId = "thread", turnId = "turn", item = new { type = "reasoning", id = "r", summary = new[] { "표시를 확인합니다.", "다음 항목을 확인합니다." }, content = new[] { "PRIVATE" } } });
        check(summaries.Count == 3 && summaries[^1] == "다음 항목을 확인합니다.",
            "New summary sections replace old sections; final snapshots do not replay earlier sections or raw content");
        Event("item/started", new { threadId = "thread", turnId = "turn", item = new { type = "agentMessage", id = "preface", phase = "commentary" } });
        Event("item/agentMessage/delta", new { threadId = "thread", turnId = "turn", itemId = "preface", delta = "A preface" });
        check(updates.Count == 0, "Helper-agent prefaces do not replace the requested final text");
        Event("item/started", new { threadId = "thread", turnId = "turn", item = new { type = "agentMessage", id = "item", phase = "final_answer" } });
        Event("item/agentMessage/delta", new { threadId = "thread", turnId = "turn", itemId = "item", delta = "안녕" });
        Event("item/agentMessage/delta", new { threadId = "thread", turnId = "turn", itemId = "item", delta = "하세요." });
        Event("item/reasoning/summaryTextDelta", new { threadId = "thread", turnId = "turn", itemId = "late-r", summaryIndex = 0, delta = "늦은 요약" });
        check(summaries.Count == 3, "Late reasoning cannot replace an answer that is already streaming");
        check(updates.SequenceEqual(new[] { "안녕", "안녕하세요." }) && !stream.Completed, "Real text deltas publish accumulated text immediately before completion");
        Event("item/completed", new { threadId = "thread", turnId = "turn", item = new { type = "agentMessage", id = "item", phase = "final_answer", text = "안녕하세요." } });
        Event("turn/completed", new { threadId = "thread", turn = new { id = "turn", status = "completed" } });
        Event("item/agentMessage/delta", new { threadId = "thread", turnId = "turn", itemId = "item", delta = "late" });
        check(updates.Count == 2 && stream.Completed && stream.Text == "안녕하세요.", "Final snapshots are deduplicated and late deltas cannot append after completion");
        var preferred = new List<string>();
        var summaryOnly = new PublicSummaryStream(preferred.Add);
        void SummaryEvent(string method, object data)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(data));
            summaryOnly.Accept(method, doc.RootElement);
        }
        SummaryEvent("item/reasoning/summaryTextDelta", new { itemId = "fallback", summaryIndex = 0, delta = "Checking" });
        check(preferred.SequenceEqual(new[] { "Checking" }), "Summary streams allow English before any Korean arrives");
        SummaryEvent("item/completed", new { item = new { type = "reasoning", id = "snapshot", summary = new[] { "한국어 결과를 확인합니다.", "Checking the final result" } } });
        check(preferred.Count == 2 && preferred[^1] == "한국어 결과를 확인합니다.", "Completed summary snapshots prefer Korean even when their last section is English");
        SummaryEvent("item/reasoning/summaryTextDelta", new { itemId = "mixed", summaryIndex = 0, delta = "API" });
        check(preferred.Count == 2, "An English prefix does not replace an existing Korean stream");
        SummaryEvent("item/reasoning/summaryTextDelta", new { itemId = "mixed", summaryIndex = 0, delta = " 연결을 확인합니다." });
        check(preferred.Count == 3 && preferred[^1] == "API 연결을 확인합니다.", "A buffered English prefix displays once the same summary gains Korean text");
        string json = JsonSerializer.Serialize(new { detail = "응답 완료", body = "말풍선을 수정했어요. \"확인\"\n끝😀" });
        string last = "";
        for (int i = 1; i <= json.Length; i++)
        {
            string part = PartialSummary.Body(json[..i]);
            if (part.Length == 0) continue;
            if (!"말풍선을 수정했어요. \"확인\"\n끝😀".StartsWith(part) || char.IsHighSurrogate(part[^1]))
                throw new InvalidOperationException("Partial summary exposed JSON or an unfinished escape.");
            last = part;
        }
        check(last == "말풍선을 수정했어요. \"확인\"\n끝😀", "Every split in structured summary JSON decodes only body text, including escaped quotes and surrogate pairs");
        var start = CodexStreamingClient.CreateStartInfo(Path.GetTempPath());
        check(start.ArgumentList.Contains("stdio://") && start.ArgumentList.Contains("mcp_servers={}")
            && start.ArgumentList.Contains("hooks") && start.ArgumentList.Contains("shell_tool") && start.CreateNoWindow,
            "Streaming uses a hidden local stdio process with MCP, hooks and execution tools disabled");
        Task.Run(() => CoordinatorAsync(check)).GetAwaiter().GetResult();
    }

    private static async Task CoordinatorAsync(Action<bool, string> check)
    {
        const string id = "77777777-7777-4777-8777-777777777777";
        var board = new ProgressBoard { Style = CompletionStyle.D };
        var task = board.Accept(new("UserPromptSubmit", id, Turn: "one"), "비서")!;
        board.Apply(new(id, "one", "answer", "표시를 수정했어요.", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
        board.Accept(new("Stop", id, Turn: "one"), "비서");
        var results = new List<TaskCompletionSource<CompletionPresentation>>();
        var callbacks = new List<Action<string>>();
        var reasonCallbacks = new List<Action<string>>();
        int renders = 0;
        var coordinator = new CompletionSummaryCoordinator((_, _, progress, reasoning) =>
        {
            reasonCallbacks.Add(reasoning);
            callbacks.Add(progress); var result = new TaskCompletionSource<CompletionPresentation>(); results.Add(result); return result.Task;
        });
        void Refresh() => coordinator.Refresh(board, true, CancellationToken.None, () => renders++);
        Refresh(); reasonCallbacks[0]("완료된 변경을 확인하고 있어요.");
        check(task.Body == "" && task.WaitingForSummary && task.StreamingReasoning == "완료된 변경을 확인하고 있어요." && task.NaturalCompletion is null,
            "Completion reasoning is retained internally without exposing a completion bubble before the result");
        callbacks[0]("말풍선"); callbacks[0]("말풍선을 바꿨어요."); reasonCallbacks[0]("늦은 요약");
        check(task.Body == "" && task.StreamingCompletion == "말풍선을 바꿨어요." && task.SummaryPending && renders == 0 && task.NaturalCompletion is null && task.StreamingReasoning == "",
            "Summary tokens are buffered without rendering partial completion text before the AI request finishes");
        board.Style = CompletionStyle.B; Refresh(); callbacks[0]("늦은 글"); reasonCallbacks[0]("늦은 추론 요약");
        check(task.Body == task.Completion && task.StreamingCompletion == "" && task.StreamingReasoning == "", "Switching to original excerpts discards partial text and reasoning and ignores late chunks");
        results[0].SetResult(new("늦은 결과", "응답 완료")); await Task.Yield();
        board.Style = CompletionStyle.D; Refresh(); callbacks[1]("중간 문장");
        results[1].SetException(new IOException("Synthetic stream interruption")); await Task.Yield();
        check(task.SummaryFailed && task.Body == task.Completion && task.StreamingCompletion == "",
            "A broken stream replaces its unfinished fragment with the original completion excerpt");
        board.Style = CompletionStyle.B; Refresh(); board.Style = CompletionStyle.D; Refresh();
        // A failed request is not automatically retried and billed again.
        check(results.Count == 2, "A stream failure does not trigger repeated AI calls");
        task = board.Accept(new("UserPromptSubmit", id, Turn: "two"), "비서")!;
        board.Apply(new(id, "two", "next", "다음 작업을 마쳤어요.", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
        board.Accept(new("Stop", id, Turn: "two"), "비서"); Refresh();
        reasonCallbacks[2]("새 작업을 확인합니다.");
        board.Dismiss(task.Key); Refresh();
        string closedBody = task.Body; callbacks[2]("닫힌 말풍선의 늦은 글"); reasonCallbacks[2]("닫힌 말풍선의 늦은 요약");
        check(task.Dismissed && task.Body == closedBody && task.StreamingCompletion == "" && task.StreamingReasoning == "", "Dismissing a streamed summary invalidates later answer and reasoning chunks");
        results[2].SetResult(new("늦은 완료", "응답 완료")); await Task.Yield();
    }

    public static async Task<int> ImageSmokeAsync()
    {
        CommentaryVerification.RenderPreview(); // Synthetic local fixture, not the user's screen.
        var timings = new List<double>(); var lengths = new List<int>(); var clock = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        string response = await CommentaryClient.GenerateAsync(AppStorage.DataDirectory,
            [Path.Combine(AppStorage.DataDirectory, "commentary-preview.png")], new(false), [], null, timeout.Token,
            progress: text => { timings.Add(clock.Elapsed.TotalMilliseconds); lengths.Add(text.Length); });
        bool ok = timings.Count >= 3 && lengths.Any(n => n < response.Length);
        await File.WriteAllTextAsync(Path.Combine(AppStorage.DataDirectory, "streaming-image-smoke.json"), JsonSerializer.Serialize(new
            { ok, updates = timings.Count, firstMs = timings.FirstOrDefault(), completedMs = clock.Elapsed.TotalMilliseconds, finalLength = response.Length }, AppStorage.Json));
        return ok ? 0 : 1;
    }

    public static async Task<int> SmokeAsync()
    {
        var timings = new List<double>();
        var texts = new List<string>();
        var reasoningTimes = new List<double>();
        var clock = Stopwatch.StartNew();
        var result = await CompletionSummaryClient.GenerateAsync("말풍선 위쪽에 비서 이름을 표시하도록 수정했습니다. 이름을 저장하면 열린 말풍선에도 바로 반영됩니다. 331개 검사가 통과했습니다.",
            CancellationToken.None, progress: text => { timings.Add(clock.Elapsed.TotalMilliseconds); texts.Add(text); }, reasoning: _ => reasoningTimes.Add(clock.Elapsed.TotalMilliseconds));
        double done = clock.Elapsed.TotalMilliseconds;
        bool ok = texts.Distinct().Count() >= 3 && texts.Any(t => t.Length < result.Body.Length) && timings[0] < done;
        await File.WriteAllTextAsync(Path.Combine(AppStorage.DataDirectory, "streaming-smoke.json"), JsonSerializer.Serialize(new
            { ok, updates = texts.Count, firstMs = timings.FirstOrDefault(), completedMs = done, finalBodyLength = result.Body.Length,
                reasoningUpdates = reasoningTimes.Count, firstReasoningMs = reasoningTimes.FirstOrDefault(), reasoningBeforeAnswer = reasoningTimes.Count > 0 && reasoningTimes[0] < timings[0] }, AppStorage.Json));
        return ok ? 0 : 1;
    }
}
