namespace SecretaryOverlay;

// Called on the UI thread. Await preserves its synchronization context for applying a result.
internal sealed class CompletionSummaryCoordinator(Func<string, CancellationToken, Action<string>, Action<string>, Task<CompletionPresentation>> generate)
{
    public CompletionSummaryCoordinator(Func<string, CancellationToken, Action<string>, Task<CompletionPresentation>> generate)
        : this((answer, token, progress, _) => generate(answer, token, progress)) { }
    public CompletionSummaryCoordinator(Func<string, CancellationToken, Task<CompletionPresentation>> generate)
        : this((answer, token, _) => generate(answer, token)) { }
    private sealed record Request(TaskProgress Task, string Turn, string Answer, CancellationTokenSource Cancel);
    private readonly Dictionary<string, Request> requests = new();
    private readonly List<Task> running = new();
    private bool enabled;

    public Task WaitForIdleAsync() => Task.WhenAll(running.ToArray());

    public void Refresh(ProgressBoard board, bool visible, CancellationToken token, Action changed)
    {
        enabled = visible && board.Style == CompletionStyle.D && !token.IsCancellationRequested;
        bool Eligible(TaskProgress task) => enabled && task.IsCompleted && task.State == "Stop" && !task.Dismissed
            && task.FinalAnswer.Length > 0 && board.Visible.Contains(task);
        foreach (var request in requests.Values.ToArray())
        {
            if (Eligible(request.Task) && Matches(request)) continue;
            requests.Remove(request.Task.Key);
            request.Cancel.Cancel();
            if (Matches(request)) request.Task.CancelSummary();
        }
        foreach (var task in board.Visible.Where(Eligible))
        {
            if (task.SummaryAttempted || task.NaturalCompletion is not null || requests.ContainsKey(task.Key)) continue;
            var request = new Request(task, task.Turn, task.FinalAnswer, CancellationTokenSource.CreateLinkedTokenSource(token));
            requests.Add(task.Key, request);
            task.BeginSummary();
            var work = RunAsync(request, board, changed);
            running.Add(work);
            _ = TrackAsync(work);
        }
        board.RefreshCompletions();
    }

    private async Task TrackAsync(Task work)
    {
        try { await work; }
        catch (Exception ex) { AppStorage.LogError(ex); }
        finally { running.Remove(work); }
    }

    private static bool Matches(Request r) => r.Task.Turn == r.Turn && r.Task.FinalAnswer == r.Answer;

    private async Task RunAsync(Request request, ProgressBoard board, Action changed)
    {
        try
        {
            var result = await generate(request.Answer, request.Cancel.Token, PublishAnswer, PublishReasoning);
            if (CanApply()) request.Task.NaturalCompletion = result;
        }
        catch (Exception) { if (CanApply()) request.Task.SummaryFailed = true; } // No final-answer text in logs.
        finally
        {
            bool current = requests.TryGetValue(request.Task.Key, out var active) && ReferenceEquals(active, request);
            if (current)
            {
                requests.Remove(request.Task.Key);
                if (Matches(request)) request.Task.FinishSummary();
            }
            request.Cancel.Dispose();
            if (current && enabled) RefreshDisplay();
        }

        void PublishAnswer(string body)
        {
            body = ProgressDisplayText.Clean(body);
            if (!CanApply() || string.IsNullOrWhiteSpace(body) || body.Length > 240) return;
            request.Task.StreamingCompletion = body;
            request.Task.StreamingReasoning = "";
        }

        void PublishReasoning(string summary)
        {
            if (!CanApply() || request.Task.StreamingCompletion.Length > 0) return;
            string display = PublicSummaryStream.Display(summary);
            if (display.Length == 0 || request.Task.StreamingReasoning == display) return;
            request.Task.StreamingReasoning = display;
        }

        void RefreshDisplay()
        {
            board.RefreshCompletions();
            changed();
        }

        bool CanApply() => enabled && board.Style == CompletionStyle.D && !request.Cancel.IsCancellationRequested
            && Matches(request) && request.Task.IsCompleted && request.Task.State == "Stop"
            && !request.Task.Dismissed && board.Tasks.Contains(request.Task);
    }
}
