using System.Windows;

namespace SecretaryOverlay;

public sealed partial class PetWindow
{
    private bool storageWarningShown;
    private void ChangeSize(double factor)
    {
        var bottom = Top + Height;
        Height = Math.Clamp(Height * factor, MinimumHeight, MaximumHeight);
        Width = Height * AspectRatio;
        Top = bottom - Height;
        SaveLayout();
    }

    private void LoadLayout()
    {
        personalization = PersonalizationStorage.Load();
        RefreshPetName();
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 30;
        Top = area.Bottom - Height - 20;
        var saved = AppStorage.LoadLayout();
        if (saved is not null)
        {
            Height = Math.Clamp(saved.Height, MinimumHeight, MaximumHeight);
            Width = Height * AspectRatio;
            Left = saved.Left;
            Top = saved.Top;
            motion = saved.Motion;
            commentary.Enabled = saved.AutoCommentary;
            commentary.SetIntervalMinutes(saved.CommentaryIntervalMinutes, Now);
            bubbleAppearance = BubbleAppearance.Normalize(saved.BubbleAppearance);
            showProgress = saved.ShowProgress;
            progressBoard.Style = saved.CompletionStyle == CompletionStyle.D ? CompletionStyle.D : CompletionStyle.B;
            automaticModel = ModelProfile.Validated(saved.AutomaticModel);
            manualModel = ModelProfile.Validated(saved.ManualModel);
            completionModel = ModelProfile.Validated(saved.CompletionModel);
            showLabels = saved.ShowLabels;
            badge.Visibility = showLabels ? Visibility.Visible : Visibility.Hidden;
        }
        // Reset only an off-screen saved position, including monitor changes.
        var rect = new System.Drawing.Rectangle((int)Left, (int)Top, (int)Width, (int)Height);
        if (!System.Windows.Forms.Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect)))
        {
            Left = area.Right - Width - 30;
            Top = area.Bottom - Height - 20;
        }
    }

    private void SaveLayout()
    {
        bool saved = AppStorage.SaveLayout(new Layout(Left, Top, Height, motion, commentary.Enabled, showProgress, progressBoard.Style,
            automaticModel, manualModel, completionModel, showLabels, bubbleAppearance, commentary.IntervalMinutes));
        if (!saved && !storageWarningShown)
        {
            storageWarningShown = true;
            tray.ShowBalloonTip(8000, "비서 펫 설정 저장 실패",
                "설정을 저장하지 못했습니다. 사용자 데이터 폴더의 쓰기 권한을 확인해 주세요.",
                System.Windows.Forms.ToolTipIcon.Warning);
        }
        if (saved) storageWarningShown = false;
        SyncControls();
    }

    private void WriteStatus() => AppStorage.WriteStatus(new
    {
        pid = Environment.ProcessId,
        state = engine.Current,
        label = label.Text,
        preview = engine.Preview,
        liveCount,
        lastHook,
        lastHookAt,
        session = engine.Session,
        left = Left,
        top = Top,
        width = Width,
        height = Height,
        pipe = HookTransport.PipeName,
        autoCommentary = commentary.Enabled,
        commentaryBusy = commentary.Busy,
        commentaryStatus,
        lastCommentAt,
        videoPlaying = viewing.VideoPlaying,
        manualViewing,
        automaticModel,
        manualModel,
        showProgress,
        completionStyle = progressBoard.Style.ToString(),
        completionSummaryModel = completionModel.Model,
        completionSummaryReasoning = completionModel.Reasoning,
        progressStatus,
        progressSourcePath,
        progressCount,
        lastProgressAt,
        progressTurn = progressBoard.Latest(engine.Session)?.Turn,
        progressBubbleVisible = bubble?.HasTaskCards == true && bubble.IsVisible,
        progressTransport = "local-transcript-active150ms-finished5s",
        progressVisibleCount = showProgress ? progressBoard.Visible.Length : 0,
        progressTasks = progressBoard.Tasks.Select(t => new { key = t.Key, session = t.Session, turn = t.Turn, question = t.QuestionNumber, state = t.State, dismissed = t.Dismissed, active = t.Active })
    });
}
