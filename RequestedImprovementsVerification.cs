using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SecretaryOverlay;

internal static class RequestedImprovementsVerification
{
    public static int RenderLongBubble()
    {
        var card = new BubbleCard(() => { });
        card.Update("프로젝트", string.Concat(Enumerable.Repeat("진행 내용을 확인하고 필요한 부분을 차례대로 정리하고 있습니다. ", 14)),
            "파일 검토 중", "긴 작업");
        string output = Path.Combine(AppStorage.DataDirectory, "previews");
        Directory.CreateDirectory(output);
        Save("long-bubble-collapsed.png");
        card.More.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Save("long-bubble-expanded.png");
        return 0;

        void Save(string file)
        {
            card.Surface.Width = 440;
            card.Surface.InvalidateMeasure();
            card.Surface.Measure(new System.Windows.Size(440, double.PositiveInfinity));
            int height = Math.Max(1, (int)Math.Ceiling(card.Surface.DesiredSize.Height));
            card.Surface.Arrange(new Rect(0, 0, 440, height));
            card.Surface.UpdateLayout();
            height = Math.Max(1, (int)Math.Ceiling(card.Surface.ActualHeight));
            card.Surface.Arrange(new Rect(0, 0, 440, height));
            card.Surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(440, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(card.Surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, file));
            encoder.Save(stream);
        }
    }

    public static void Check(Action<bool, string> check)
    {
        CheckStorageAndCleanup(check);
        CheckBubble(check);
        CheckApprovalNavigation(check);
    }

    private static void CheckStorageAndCleanup(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "SecretaryOverlay-verification-" + Guid.NewGuid().ToString("N"));
        string oldData = Path.Combine(root, "old");
        string newData = Path.Combine(root, "new");
        string temp = Path.Combine(root, "temporary");
        try
        {
            Directory.CreateDirectory(oldData);
            Directory.CreateDirectory(newData);
            File.WriteAllText(Path.Combine(oldData, "layout.json"), "old");
            File.WriteAllText(Path.Combine(oldData, "personalization.json"), "settings");
            File.WriteAllText(Path.Combine(newData, "layout.json"), "new");
            AppStorage.Initialize(newData, oldData);
            check(File.ReadAllText(Path.Combine(newData, "layout.json")) == "new"
                && File.ReadAllText(Path.Combine(newData, "personalization.json")) == "settings",
                "Portable settings migrate into user data without overwriting an existing setting");
            var roundTrip = new Layout(1, 2, 500);
            string atomic = Path.Combine(newData, "atomic.json");
            check(AtomicJsonFile.Save(atomic, roundTrip)
                && System.Text.Json.JsonSerializer.Deserialize<Layout>(File.ReadAllText(atomic)) == roundTrip,
                "Settings are committed as complete JSON");

            Directory.CreateDirectory(temp);
            string old = Path.Combine(temp, Guid.NewGuid().ToString("N"));
            string fresh = Path.Combine(temp, "completion-" + Guid.NewGuid().ToString("N"));
            string foreign = Path.Combine(temp, "unrelated");
            Directory.CreateDirectory(old);
            Directory.CreateDirectory(fresh);
            Directory.CreateDirectory(foreign);
            var now = DateTime.UtcNow;
            Directory.SetLastWriteTimeUtc(old, now.AddDays(-2));
            Directory.SetLastWriteTimeUtc(fresh, now);
            Directory.SetLastWriteTimeUtc(foreign, now.AddDays(-2));
            check(TemporaryFiles.CleanupStale(temp, now) == 1 && !Directory.Exists(old)
                && Directory.Exists(fresh) && Directory.Exists(foreign),
                "Startup cleanup removes only old pet-owned temporary folders");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void CheckBubble(Action<bool, string> check)
    {
        var card = new BubbleCard(() => { });
        card.Update("", "짧은 응답입니다.", "");
        check(card.More.Visibility == Visibility.Collapsed,
            "Ordinary bubbles keep their existing layout without a details control");
        string longText = string.Concat(Enumerable.Repeat("긴 작업 내용을 계속 확인하고 있습니다. ", 18));
        card.Update("", longText, "");
        check(card.More.Visibility == Visibility.Visible && Equals(card.More.Content, "자세히 보기"),
            "Long bubbles expose a small details control");
        card.More.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        check(Equals(card.More.Content, "간략히 보기") && card.Body.Text == ProgressDisplayText.Clean(longText),
            "Details expand the original full text inside the same bubble");
        check(card.Body.FontFamily.Equals(PetTypography.Mixed) && card.Body.FontWeight == FontWeights.Normal,
            "All bubble bodies use the bundled serif font for progress and responses");
    }

    private static void CheckApprovalNavigation(Action<bool, string> check)
    {
        string session = "11111111-1111-4111-8111-111111111111";
        var area = SystemParameters.WorkArea;
        var owner = new Window { Left = area.Left + 100, Top = area.Top + 100, Width = 300, Height = 400,
            Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        SpeechBubble? bubble = null;
        try
        {
            owner.Show();
            bubble = new SpeechBubble(owner, animateReflow: () => false) { Opacity = 0 };
            bubble.SetTasks([new TaskProgress(session) { State = "PermissionRequest", Detail = "승인 기다리는 중" }]);
            check(bubble.VisibleCards.Single().OpenChat.Visibility == Visibility.Visible,
                "An approval request provides a direct link to its existing Codex chat");
            bubble.SetTasks([new TaskProgress(session) { State = "PreToolUse" }]);
            check(bubble.VisibleCards.Single().OpenChat.Visibility == Visibility.Collapsed,
                "Other active work keeps the chat button hidden");
        }
        finally { bubble?.Close(); owner.Close(); }
    }
}
