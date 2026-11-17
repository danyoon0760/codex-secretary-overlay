
namespace SecretaryOverlay;

public sealed partial class PetWindow
{
    private bool closingStarted;
    private bool closingComplete;
    private static readonly TimeSpan RecentHookWindow = TimeSpan.FromMinutes(2);

    private void StartServices()
    {
        LoadLayout();
        _ = Task.Run(() => TemporaryFiles.CleanupStale());
        StartCommentary();
        SetPose("Idle");
        SetBreathing();
        listener = Task.Run(() => HookTransport.ListenAsync(
            ev => Dispatcher.BeginInvoke(() => Receive(ev)), stop.Token));
        clock.Tick += OnTick;
        clock.Start();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (closingComplete) return;
        e.Cancel = true;
        if (closingStarted) return;
        closingStarted = true;
        try { await StopServicesAsync(); }
        catch (Exception ex) { AppStorage.LogError(ex); }
        finally
        {
            closingComplete = true;
            // The cleanup task can finish synchronously. Close after the current
            // Closing event returns, or WPF rejects a reentrant Close call.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private async Task StopServicesAsync()
    {
        control?.Close();
        SaveLayout();
        stop.Cancel();
        completionSummaries.Refresh(progressBoard, false, stop.Token, () => { });
        StopCommentary();
        clock.Stop();
        tray.Dispose();
        try
        {
            await Task.WhenAll(listener ?? Task.CompletedTask, speakingTask ?? Task.CompletedTask,
                    summaryCoordinator?.WaitForIdleAsync() ?? Task.CompletedTask)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            AppStorage.LogError(ex);
        }
        finally
        {
            stop.Dispose();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        engine.Tick(Now);
        if ((DateTime.Now - lastSave).TotalSeconds <= 2) return;
        UpdateIndicator();
        WriteStatus();
        lastSave = DateTime.Now;
        SyncControls();
    }

    private void Receive(PetEvent ev)
    {
        if (stop.IsCancellationRequested) return;
        switch (ev.Event)
        {
            case "__quit": Close(); return;
            case "__show": RestoreInteraction(); return;
            case "__idle": engine.Reset(Now); return;
            case "__commentary-preview": bubble?.Say("Windows 기본 글꼴을 굵게 표시합니다.\n작업 내용을 바로 확인할 수 있어요."); return;
            case "__menu-preview": ShowMenu(); return;
        }
        if (!StateEngine.Poses.ContainsKey(ev.Event)) return;
        // Retired hooks must not affect the character before the board can reject them.
        if (progressBoard.IsRetiredEvent(ev) || progressBoard.IsAmbiguousEvent(ev) || progressBoard.IsDuplicatePrompt(ev)) return;
        if (!ev.Preview)
        {
            liveCount++;
            lastHook = ev.Event;
            lastHookAt = DateTimeOffset.Now;
            AppStorage.AppendEvent(ev, lastHookAt);
        }
        engine.Accept(ev, Now);
        // The character follows the latest prompt; bubbles track every task independently.
        if (!ev.Preview) FollowProgress(ev);
        UpdateIndicator();
        WriteStatus();
    }

    private void RestoreInteraction()
    {
        clickThrough = false;
        ApplyClickThrough();
        Show();
        SyncControls();
    }

    private void UpdateIndicator()
    {
        indicator.Text = engine.Preview ? "동작 미리보기"
            : commentary.Busy ? "한마디 준비 중"
            : liveCount == 0 ? "Codex 작업 알림 기다리는 중"
            : lastHookAt is { } received && DateTimeOffset.Now - received <= RecentHookWindow
                ? "Codex 알림 최근 수신" : "최근 작업 알림 없음";
    }
}
