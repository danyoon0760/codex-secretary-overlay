using System.IO;
using System.Windows.Threading;

namespace SecretaryOverlay;

public sealed partial class PetWindow
{
    private readonly CommentaryPolicy commentary = new(Now);
    private readonly CommentaryConversation conversation = new();
    private readonly DispatcherTimer commentaryTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private DesktopActivity? activity;
    private SpeechBubble? bubble;
    private CancellationTokenSource? commentaryRequest;
    private Task? speakingTask;
    private bool manualRequest;
    private bool checkingCommentary;
    private bool manualViewing;
    private ViewingState viewing = new(false);
    private string commentaryStatus = "대기";
    private DateTimeOffset? lastCommentAt;
    private bool HasCommentarySlot => !showProgress || progressBoard.Visible.Length < BubbleCapacity.MaximumVisible;
    private bool ReadyForCommentary => engine.Current == "Idle" && !engine.Preview && !progressBoard.HasActive && HasCommentarySlot;

    private void StartCommentary()
    {
        activity = new DesktopActivity();
        bubble = new SpeechBubble(this) { PetName = personalization.Name, Appearance = bubbleAppearance };
        StartProgress();
        commentaryTimer.Tick += CheckCommentary;
        commentaryTimer.Start();
    }

    private void StopCommentary()
    {
        commentaryTimer.Stop();
        progressTimer.Stop();
        commentaryRequest?.Cancel();
        activity?.Dispose();
        bubble?.Close();
        if (powerNotification != IntPtr.Zero) NativeDesktop.UnregisterPowerSettingNotification(powerNotification);
    }

    private async void CheckCommentary(object? sender, EventArgs e)
    {
        if (activity is null || stop.IsCancellationRequested) return;
        if (!activity.Available)
        {
            commentaryRequest?.Cancel();
            bubble?.Hide();
            return;
        }
        if (checkingCommentary || commentary.Busy || !commentary.Enabled || !ReadyForCommentary || Now < commentary.NextAt) return;
        checkingCommentary = true;
        try
        {
            viewing = await activity.GetViewingStateAsync(stop.Token);
            if (manualViewing) viewing = new(true);
            if (commentary.CanStart(Now, false, ReadyForCommentary, activity.Available, activity.MouseRecent(Now), viewing.VideoPlaying))
                await SpeakAsync(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppStorage.LogError(ex); }
        finally { checkingCommentary = false; }
    }

    private Task SpeakAsync(bool manual)
    {
        if (speakingTask is { IsCompleted: false }) return speakingTask;
        speakingTask = SpeakCoreAsync(manual);
        return speakingTask;
    }

    private async Task SpeakCoreAsync(bool manual)
    {
        if (activity is null || stop.IsCancellationRequested) return;
        if (!HasCommentarySlot) return;
        if (!commentary.CanStart(Now, manual, ReadyForCommentary, activity.Available, activity.MouseRecent(Now), viewing.VideoPlaying)) return;
        var selectedModel = manual ? manualModel : automaticModel;
        var selectedPersonalization = personalization;
        commentary.Begin();
        manualRequest = manual;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        request.CancelAfter(TimeSpan.FromSeconds(100));
        commentaryRequest = request;
        bool success = false;
        bool streamDismissed = false;
        long streamVersion = bubble?.BeginAmbientStream() ?? 0;
        string folder = Path.Combine(Path.GetTempPath(), "SecretaryOverlay", Guid.NewGuid().ToString("N"));
        void Stream(string text, string detail)
        {
            if (request.IsCancellationRequested || stop.IsCancellationRequested || activity?.Available != true
                || !ReferenceEquals(commentaryRequest, request) || (!manual && (!ReadyForCommentary || !commentary.Enabled))) return;
            if (bubble?.UpdateAmbientStream(streamVersion, text, detail) != true) { streamDismissed = true; request.Cancel(); }
        }
        try
        {
            commentaryStatus = "한마디 준비 중";
            SyncControls();
            UpdateIndicator();
            WriteStatus();
            // Allow the context menu to close before any capture.
            await Task.Delay(250, request.Token);
            viewing = manualViewing ? new(true) : await activity.GetViewingStateAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (!activity.Available) return;
            Directory.CreateDirectory(folder);
            var observedWindow = NativeDesktop.GetCommentaryWindow();
            var images = new List<string>();
            int frameCount = viewing.VideoPlaying ? 2 : 1;
            for (int i = 0; i < frameCount; i++)
            {
                if (i > 0) await Task.Delay(1200, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (!activity.Available) return;
                var file = Path.Combine(folder, $"screen-{i}.png");
                var bubbleHandle = bubble is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(bubble).Handle;
                var settingsHandle = control is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(control).Handle;
                NativeDesktop.CaptureWindow(file, observedWindow, hwnd, bubbleHandle, settingsHandle);
                images.Add(file);
            }
            var response = await CommentaryClient.GenerateAsync(folder, images, viewing, conversation.Recent, observedWindow, request.Token, selectedModel, selectedPersonalization,
                text => Stream(text, ""), summary => Stream(summary, "생각하는 중"));
            request.Token.ThrowIfCancellationRequested();
            // A real task may have started while the request was in flight.
            if (!activity.Available || (!manual && (!ReadyForCommentary || !commentary.Enabled))) return;
            if (bubble is null) return;
            if (!bubble.CompleteAmbientStream(streamVersion, response)) return;
            conversation.Remember(response);
            lastCommentAt = DateTimeOffset.Now;
            success = true;
            commentaryStatus = "대기";
        }
        catch (OperationCanceledException)
        {
            commentaryStatus = "대기";
            if (!request.IsCancellationRequested) return;
            if (manual && !streamDismissed && !stop.IsCancellationRequested && commentary.Enabled && activity.Available)
            {
                bubble?.CompleteAmbientStream(streamVersion, "한마디 요청이 중단됐어. 필요하면 다시 불러줘.");
            }
            else if (!stop.IsCancellationRequested) bubble?.DiscardAmbientStream(streamVersion);
        }
        catch (Exception ex)
        {
            if (request.IsCancellationRequested)
            {
                commentaryStatus = "대기";
                if (!stop.IsCancellationRequested) bubble?.DiscardAmbientStream(streamVersion);
                return;
            }
            commentaryStatus = "요청 실패 · 다시 시도 가능";
            AppStorage.LogError(ex);
            if (manual && activity.Available && !stop.IsCancellationRequested)
            {
                bubble?.CompleteAmbientStream(streamVersion, ex is InvalidOperationException or FileNotFoundException ? ex.Message : "화면을 확인하지 못했어. 잠시 후 다시 불러줘.");
            }
            else if (!stop.IsCancellationRequested) bubble?.DiscardAmbientStream(streamVersion);
        }
        finally
        {
            commentaryRequest = null;
            manualRequest = false;
            commentary.Finish(Now, success || request.IsCancellationRequested);
            if (commentaryStatus == "한마디 준비 중") commentaryStatus = "대기";
            if (Directory.Exists(folder))
            {
                try { Directory.Delete(folder, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppStorage.LogError(new IOException("한마디 임시 파일을 정리하지 못했습니다.")); }
            }
            if (!stop.IsCancellationRequested) { UpdateIndicator(); WriteStatus(); SyncControls(); }
        }
    }

    private void ToggleCommentary() => SetCommentaryEnabled(!commentary.Enabled);

    private void SetCommentaryEnabled(bool enabled)
    {
        if (commentary.Enabled == enabled) return;
        commentary.Enabled = enabled;
        if (!commentary.Enabled) { commentaryRequest?.Cancel(); bubble?.ClearAmbient(); RenderProgress(); }
        commentary.ResetTimer(Now);
        SaveLayout();
        WriteStatus();
    }

}
