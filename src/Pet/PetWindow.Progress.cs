using System.IO;
using System.Windows.Threading;

namespace SecretaryOverlay;

public sealed partial class PetWindow
{
    private readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly ProgressBoard progressBoard = new();
    private CompletionSummaryCoordinator? summaryCoordinator;
    private CompletionSummaryCoordinator completionSummaries => summaryCoordinator ??= new((answer, token, progress, reasoning) => CompletionSummaryClient.GenerateAsync(answer, token, completionModel, personalization, progress, reasoning));
    private readonly ProjectNames projectNames = new();
    private readonly ChatTitles chatTitles = new();
    private readonly Dictionary<string, ProgressTranscript> progressReaders = new();
    private readonly ProgressPollingPolicy progressPolling = new();
    private bool readingProgress;
    private bool showProgress = true;
    private long progressCount;
    private DateTimeOffset? lastProgressAt;
    private string progressStatus = "후크 연결 대기";
    private string progressSourcePath = "";

    private void StartProgress()
    {
        if (bubble is not null) bubble.ChatRequested += OpenTaskChat;
        if (bubble is not null) bubble.OverflowRequested += OpenOtherTaskMenu;
        if (bubble is not null) bubble.TaskDismissed += key => DismissTasks([key]);
        if (bubble is not null) bubble.TasksExpired += DismissTasks;
        progressTimer.Tick += ReadProgress;
        progressTimer.Start();
    }

    private void DismissTasks(IReadOnlyList<string> keys)
    {
        foreach (string key in keys) progressBoard.Dismiss(key);
        RemoveRetiredProgressReaders();
        RefreshCompletionSummaries();
        RenderProgress();
        WriteStatus();
    }

    private void OpenTaskChat(string session)
    {
        if (ChatNavigation.TryOpen(session)) return;
        System.Windows.MessageBox.Show(this, "채팅을 열지 못했어요. Codex 앱을 실행한 뒤 다시 눌러 주세요.",
            personalization.Name, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void ShowOtherTask(string key)
    {
        if (!progressBoard.Tasks.Any(task => task.Key == key && !task.Dismissed)) return;
        if (!progressBoard.Promote(key)) return;
        RefreshCompletionSummaries();
        RenderProgress();
        WriteStatus();
    }

    private void OpenOtherTaskMenu()
    {
        if (!showProgress || menuOpen || hwnd == IntPtr.Zero) return;
        var entries = OtherTaskMenu.Create(progressBoard.Tasks, ShowOtherTask)?.Children;
        if (entries is null || entries.Count == 0) return;
        menuOpen = true;
        Action? selected;
        try
        {
            using var menu = new NativePopupMenu();
            menu.AddEntries(entries);
            selected = menu.Select(hwnd);
        }
        finally { menuOpen = false; }
        if (!stop.IsCancellationRequested) selected?.Invoke();
    }

    private void FollowProgress(PetEvent ev)
    {
        var task = progressBoard.Accept(ev, projectNames.Resolve(ev.Session, ev.Cwd));
        if (task is null) return;
        RemoveRetiredProgressReaders();
        if (!progressBoard.Tasks.Contains(task))
        {
            RefreshCompletionSummaries();
            RenderProgress();
            return;
        }
        task.ChatTitle = chatTitles.Resolve(task.Session);
        if (ev.Event == "UserPromptSubmit") bubble?.ClearAmbient();
        if (ev.Transcript.Length > 0)
        {
            var path = ProgressTranscript.ResolvePath(ev.Transcript, ev.Session);
            if (path is null) progressStatus = "지원하지 않는 대화 기록 경로";
            else
            {
                progressSourcePath = path;
                if (!progressReaders.TryGetValue(ev.Session, out var reader) || reader.Path != path)
                    progressReaders[ev.Session] = new ProgressTranscript(path, ev.Session, "",
                        progressBoard.Tasks.Where(t => t.Session == ev.Session).Min(t => t.CreatedAt).AddSeconds(-2));
            }
        }
        // One reader follows all question IDs in a chat without resetting its offset.
        // Late records from an earlier question still reach that question's card.
        if (progressReaders.ContainsKey(ev.Session)) progressPolling.NotifyHook(task, Now);
        RefreshCompletionSummaries();
        RenderProgress();
        ReadProgress(null, EventArgs.Empty);
    }

    private void RemoveRetiredProgressReaders()
    {
        foreach (string stale in progressReaders.Keys.Where(s => !progressBoard.Tasks.Any(t => t.Session == s)).ToArray())
        {
            progressReaders.Remove(stale);
            progressPolling.Remove(stale);
        }
    }

    private void RenderProgress()
    {
        if (bubble is null) return;
        var available = progressBoard.Tasks.Where(t => !t.Dismissed).ToArray();
        bubble.SetTasks(available, showProgress && !engine.Preview && activity?.Available == true);
        if (activity?.Available != true) bubble.Hide();
    }

    private async void ReadProgress(object? sender, EventArgs e)
    {
        if (readingProgress || stop.IsCancellationRequested) return;
        if (activity?.Available != true) bubble?.Hide();
        readingProgress = true;
        bool changed = false;
        try
        {
            await Task.WhenAll(chatTitles.RefreshAsync(stop.Token), projectNames.RefreshAsync(stop.Token));
            if (stop.IsCancellationRequested) return;
            foreach (var task in progressBoard.Tasks)
            {
                string title = chatTitles.Resolve(task.Session);
                if (task.ChatTitle != title) { task.ChatTitle = title; changed = true; }
                string project = projectNames.Resolve(task.Session, task.Cwd);
                if (task.Project != project) { task.Project = project; changed = true; }
            }
            foreach (var reader in progressReaders.Values.ToArray())
            {
                if (!progressReaders.TryGetValue(reader.Session, out var scheduled) || !ReferenceEquals(reader, scheduled)) continue;
                var task = progressBoard.Tasks.Where(t => t.Session == reader.Session)
                    .OrderByDescending(t => t.Active && !t.Dismissed).ThenByDescending(t => t.Active)
                    .ThenByDescending(t => t.BubbleIdentity).FirstOrDefault();
                if (task is null || !progressPolling.TryBeginRead(task, Now)) continue;
                try
                {
                    var updates = await reader.ReadUpdatesAsync(stop.Token);
                    if (stop.IsCancellationRequested) return;
                    if (!progressReaders.TryGetValue(reader.Session, out var current) || !ReferenceEquals(reader, current)) continue;
                    progressStatus = "연결됨 · 기록 도착 즉시 갱신";
                    if (reader.Cwd.Length > 0) task.Cwd = reader.Cwd;
                    if (task.Cwd.Length > 0) task.Project = projectNames.Resolve(task.Session, task.Cwd);
                    foreach (var update in updates)
                    {
                        bool wasActive = progressBoard.Find(update.Session, update.Turn)?.Active == true;
                        if (!progressBoard.Apply(update)) continue;
                        if (update.CompletesTurn && wasActive)
                            engine.Accept(new("Stop", update.Session, Turn: update.Turn), Now);
                        changed = true;
                        progressCount++;
                        lastProgressAt = update.At;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { progressStatus = "일부 대화 기록 연결 대기"; }
            }
            if (changed) RefreshCompletionSummaries();
            if (changed)
            {
                if (showProgress && !engine.Preview && activity?.Available == true && !manualRequest) commentaryRequest?.Cancel();
                RenderProgress();
                WriteStatus();
            }
        }
        catch (OperationCanceledException) { }
        finally { readingProgress = false; }
    }

    private void SetProgressEnabled(bool enabled)
    {
        if (showProgress == enabled) return;
        showProgress = enabled;
        RefreshCompletionSummaries();
        RenderProgress();
        SaveLayout();
        WriteStatus();
    }

    private void RefreshCompletionSummaries() => completionSummaries.Refresh(progressBoard, showProgress, stop.Token,
        RenderProgress); // The regular status timer writes diagnostics; token updates only render text.

    private void SetCompletionStyle(CompletionStyle style)
    {
        if (progressBoard.Style == style) return;
        progressBoard.Style = style;
        CompletionSettings.Sync(completionSettingsPanel, style);
        RefreshCompletionSummaries();
        progressBoard.RefreshCompletions();
        RenderProgress();
        SaveLayout();
        WriteStatus();
    }
}
