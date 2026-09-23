using System.Windows;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Point = System.Windows.Point;

namespace SecretaryOverlay;

internal static class OverflowBubbleWindowVerification
{
    private sealed class Fixture : IDisposable
    {
        private readonly Window owner = new()
        {
            Width = 350, Height = 560, Left = 100, Top = 300,
            Opacity = 0, IsHitTestVisible = false, ShowActivated = false, ShowInTaskbar = false
        };
        public long Now { get; set; }
        public bool Display { get; set; } = true;
        public SpeechBubble Bubble { get; }
        public ProgressBoard Board { get; } = new();

        public Fixture(bool animate = false)
        {
            owner.Show();
            Bubble = new SpeechBubble(owner, () => Now, () => animate) { Opacity = 0, IsHitTestVisible = false };
            Bubble.TasksExpired += sessions =>
            {
                foreach (string session in sessions) Board.Dismiss(session);
                Render(); // The real owner also renders synchronously inside the expiry callback.
            };
            Bubble.TaskDismissed += session => { Board.Dismiss(session); Render(); };
        }

        public TaskProgress Start(int id)
        {
            var task = Board.Accept(new("UserPromptSubmit", $"30000000-0000-4000-8000-{id:000000000000}", Turn: "one"), "비서 펫")!;
            task.ChatTitle = $"설정 확인 {id}";
            task.Body = "설정 내용을 확인하고 있어요.";
            Render();
            return task;
        }
        public void Complete(TaskProgress task)
        {
            Board.Accept(new("Stop", task.Session, Turn: task.Turn), "비서 펫");
            Render();
        }
        public void Render()
        {
            Bubble.SetTasks(Board.Tasks.Where(task => !task.Dismissed).ToArray(), Display);
            Bubble.UpdateLayout();
        }
        public void Expire(long now) { Now = now; Bubble.ExpireOlderBubbles(); Bubble.UpdateLayout(); }
        public void ClickOverflow() => Bubble.Overflow.Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        public void Hover(bool hovered) => Bubble.RaiseEvent(new System.Windows.Input.MouseEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, 0)
            { RoutedEvent = hovered ? System.Windows.Input.Mouse.MouseEnterEvent : System.Windows.Input.Mouse.MouseLeaveEvent });
        public void Dispose() { Bubble.Close(); owner.Close(); }
    }

    public static void Check(Action<bool, string> check)
    {
        CheckCountsAndActions(check);
        CheckApprovalPriority(check);
        CheckQueuedExpiry(check);
        CheckLayoutAndFont(check);
        CheckMenuPause(check);
        CheckReflow(check);
    }

    private static void CheckApprovalPriority(Action<bool, string> check)
    {
        using var f = new Fixture();
        var oldest = f.Start(1);
        for (int i = 2; i <= 5; i++) f.Start(i);
        f.Board.Accept(new("PermissionRequest", oldest.Session, Turn: oldest.Turn), "비서 펫");
        f.Render();
        check(f.Board.Visible[0] == oldest && f.Bubble.VisibleCards[0].ChatTitle.Text == oldest.ChatTitle
            && f.Bubble.Overflow.Text == "다른 작업 2개",
            "An older approval request immediately moves into the first visible card slot");
        for (int i = 6; i <= 8; i++) f.Start(i);
        var menu = OtherTaskMenu.Create(f.Board.Tasks, key => { f.Board.Promote(key); f.Render(); });
        var approvalRow = menu?.Children?.SingleOrDefault(item => item.Label.Contains("승인 필요"));
        check(!f.Board.Visible.Contains(oldest) && f.Bubble.Overflow.Text == "다른 작업 5개\n승인 필요 1개"
            && approvalRow?.Label.Contains(oldest.ChatTitle) == true,
            "A later queue still exposes its hidden approval count and identifies the task in the overflow menu");
        f.Bubble.Appearance = new BubbleAppearance(300, 26, 24);
        f.Bubble.UpdateLayout();
        check(IsContained(f.Bubble) && f.Bubble.Overflow.Text.Contains("승인 필요 1개"),
            "The approval count stays in the narrowest bubble at the largest configured text size");
        approvalRow?.Action?.Invoke();
        check(f.Board.Visible[0] == oldest && f.Bubble.VisibleCards[0].ChatTitle.Text == oldest.ChatTitle,
            "Choosing the approval from overflow returns its card to the visible stack");
    }

    private static void CheckReflow(Action<bool, string> check)
    {
        using var f = new Fixture(true);
        void Advance(long time) { f.Now = time; f.Bubble.AdvanceReflow(); f.Bubble.UpdateLayout(); }
        double Top() => f.Bubble.Top + f.Bubble.Overflow.Surface.TranslatePoint(new Point(), (UIElement)f.Bubble.Content).Y;
        for (int i = 50; i < 54; i++) f.Start(i);
        Advance(180); Advance(330); Advance(2000);
        double before = Top();
        f.Now = 2100;
        var latest = f.Board.Accept(new("UserPromptSubmit", "30000000-0000-4000-8000-000000000054", Turn: "one"), "비서 펫")!;
        latest.Body = "새 작업을 확인하고 있습니다.\n추가 설명을 표시합니다.\n세 번째 줄도 포함합니다.";
        f.Render();
        check(Near(Top(), before) && f.Bubble.IsReflowing && IsContained(f.Bubble),
            "The overflow label retains its current screen position when a larger new task starts a reflow");
        Advance(2190);
        double middle = Top();
        check(middle > before && IsContained(f.Bubble),
            "The overflow label moves with the stack and remains unclipped at an intermediate animation frame");
        Advance(2280);
        check(Top() > middle && IsBelowLastCard(f.Bubble) && IsContained(f.Bubble)
              && f.Bubble.Overflow.Count == 2 && f.Bubble.VisibleCards.Count == 3,
            "The animated stack settles with its complete overflow label below exactly three task cards");
    }

    private static void CheckCountsAndActions(Action<bool, string> check)
    {
        using var f = new Fixture();
        var oldest = f.Start(1); f.Start(2); f.Start(3);
        int clicks = 0;
        f.Bubble.OverflowRequested += () => clicks++;
        f.ClickOverflow();
        check(f.Bubble.VisibleCards.Count == 3 && f.Bubble.Overflow.Count == 0
              && f.Bubble.Overflow.Text.Length == 0 && f.Bubble.Overflow.Surface.Visibility == Visibility.Collapsed && clicks == 0,
            "Three real task cards have no overflow label or actionable overflow button");
        f.Start(4);
        check(f.Bubble.VisibleCards.Count == 3 && f.Bubble.Overflow.Count == 1
              && f.Bubble.Overflow.Text == "다른 작업 1개" && f.Bubble.Overflow.Surface.Visibility == Visibility.Visible,
            "A fourth task adds one compact overflow label without taking a real card slot");
        f.Start(5);
        check(f.Bubble.VisibleCards.Count == 3 && f.Bubble.Overflow.Count == 2
              && f.Bubble.Overflow.Text == "다른 작업 2개" && f.Board.Tasks.Count == 5,
            "Five available tasks still render three real cards and accurately count two queued tasks");

        var tasksBefore = f.Board.Tasks.Select(task =>
            (task.Session, task.BubbleIdentity, task.Turn, task.State, task.Active, task.Dismissed, task.Body)).ToArray();
        var cardsBefore = f.Bubble.VisibleCards.ToArray();
        f.ClickOverflow();
        check(clicks == 1 && tasksBefore.SequenceEqual(f.Board.Tasks.Select(task =>
                  (task.Session, task.BubbleIdentity, task.Turn, task.State, task.Active, task.Dismissed, task.Body)))
              && cardsBefore.SequenceEqual(f.Bubble.VisibleCards) && f.Bubble.Overflow.Count == 2,
            "One overflow click raises exactly one request and neither dismisses nor modifies any task");

        long identity = oldest.BubbleIdentity;
        string state = oldest.State;
        bool promoted = f.Board.Promote(oldest.Key);
        f.Render();
        check(promoted && f.Board.Visible[0] == oldest && f.Bubble.VisibleCards[0].ChatTitle.Text == oldest.ChatTitle
              && oldest.BubbleIdentity == identity && oldest.State == state && oldest.Active && f.Bubble.Overflow.Count == 2,
            "Selecting an overflow task moves it into view without changing its identity, state, or queue count");
        f.Bubble.VisibleCards[0].Close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(oldest.Dismissed && oldest.Active && f.Bubble.VisibleCards.Count == 3
              && f.Bubble.Overflow.Count == 1 && f.Bubble.Overflow.Text == "다른 작업 1개"
              && f.Board.Visible.Select(task => task.ChatTitle).SequenceEqual(f.Bubble.VisibleCards.Select(card => card.ChatTitle.Text)),
            "Closing a visible card admits the next queued task and immediately reduces the overflow count");

        var availableBeforeHide = f.Board.Tasks.Where(task => !task.Dismissed).ToArray();
        f.Display = false; f.Render();
        check(f.Bubble.Overflow.Count == 0 && f.Bubble.Overflow.Surface.Visibility == Visibility.Collapsed
              && f.Bubble.VisibleCards.Count == 0 && !f.Bubble.IsVisible
              && availableBeforeHide.SequenceEqual(f.Board.Tasks.Where(task => !task.Dismissed)),
            "Disabling task display hides the overflow label while retaining every available task");
        f.Display = true; f.Render();
        check(f.Bubble.IsVisible && f.Bubble.VisibleCards.Count == 3 && f.Bubble.Overflow.Count == 1
              && f.Bubble.Overflow.Surface.Visibility == Visibility.Visible,
            "Re-enabling task display restores its real cards and current overflow count");
    }

    private static void CheckQueuedExpiry(Action<bool, string> check)
    {
        using var f = new Fixture();
        var queued = f.Start(10); f.Start(11); f.Start(12); f.Start(13); f.Start(14);
        f.Now = 1_000; f.Complete(queued);
        f.Expire(60_999);
        check(!queued.Dismissed && f.Bubble.Overflow.Count == 2 && !f.Board.Visible.Contains(queued),
            "A queued completion remains counted until its full completion minute has elapsed");
        f.Expire(61_000);
        check(queued.Dismissed && f.Bubble.Overflow.Count == 1 && f.Bubble.Overflow.Text == "다른 작업 1개"
              && f.Bubble.VisibleCards.Count == 3 && f.Board.Visible.All(task => task.Active),
            "Queued completion expiry updates the overflow count during nested rendering without disturbing active cards");
        f.Bubble.VisibleCards[0].Close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(f.Bubble.VisibleCards.Count == 3 && f.Bubble.Overflow.Count == 0
              && f.Bubble.Overflow.Surface.Visibility == Visibility.Collapsed && !f.Board.Visible.Contains(queued),
            "The overflow label disappears when the last waiting active task is admitted and an expired task stays hidden");
    }

    private static void CheckLayoutAndFont(Action<bool, string> check)
    {
        using var f = new Fixture();
        f.Start(20); f.Start(21); f.Start(22);
        var survivor = f.Bubble.VisibleCards[0];
        double bodyHeight = survivor.Body.ActualHeight;
        double cardHeight = survivor.Surface.ActualHeight;
        f.Start(23);
        check(f.Bubble.VisibleCards.Contains(survivor) && Near(survivor.Body.ActualHeight, bodyHeight)
              && Near(survivor.Surface.ActualHeight, cardHeight),
            "Adding the separate overflow label preserves existing task body and full card heights");
        check(IsBelowLastCard(f.Bubble) && IsContained(f.Bubble),
            "The fourth-task label sits below the last real card and the window includes its full body and tail");
        f.Start(24);
        check(IsBelowLastCard(f.Bubble) && IsContained(f.Bubble),
            "Updating the overflow count to two keeps the compact label below the cards and wholly inside the window");

        double labelHeight = f.Bubble.Overflow.Surface.ActualHeight;
        double labelFont = f.Bubble.Overflow.Button.FontSize;
        f.Bubble.Appearance = f.Bubble.Appearance with { FontSize = 26 };
        f.Bubble.UpdateLayout();
        check(f.Bubble.Overflow.Button.FontSize > labelFont && f.Bubble.Overflow.Surface.ActualHeight > labelHeight
              && f.Bubble.Overflow.Button.FontWeight == FontWeights.Bold && f.Bubble.VisibleCards.All(card => card.Body.FontSize == 26),
            "Larger bubble text also enlarges the bold overflow label and its measured height");
        check(IsBelowLastCard(f.Bubble) && IsContained(f.Bubble),
            "The enlarged overflow label still clears the last card and stays inside the window including its tail");
        f.Bubble.Appearance = BubbleAppearance.Default;
        f.Bubble.UpdateLayout();
        check(Near(f.Bubble.Overflow.Button.FontSize, labelFont) && Near(f.Bubble.Overflow.Surface.ActualHeight, labelHeight),
            "Restoring the default appearance restores the overflow label's original font and height");
    }

    private static void CheckMenuPause(Action<bool, string> check)
    {
        using var f = new Fixture();
        var queued = f.Start(30); f.Start(31); f.Start(32); f.Start(33);
        f.Complete(queued);
        int clicks = 0;
        bool protectedInside = false;
        f.Bubble.OverflowRequested += () =>
        {
            clicks++;
            f.Hover(false); // Moving from the label to a native menu must not end the pause.
            f.Expire(100_000);
            protectedInside = !queued.Dismissed && f.Bubble.Overflow.Count == 1;
        };
        f.Now = 20_000;
        f.ClickOverflow();
        check(clicks == 1 && protectedInside && !queued.Dismissed && f.Bubble.Overflow.Count == 1,
            "Opening the overflow menu pauses completion expiry even after MouseLeave and a long nested menu loop");
        f.Expire(139_999);
        check(!queued.Dismissed && f.Bubble.Overflow.Count == 1,
            "Closing the overflow menu resumes the exact remaining forty seconds instead of using elapsed menu time");
        f.Expire(140_000);
        check(queued.Dismissed && f.Bubble.Overflow.Count == 0 && f.Bubble.Overflow.Surface.Visibility == Visibility.Collapsed
              && f.Bubble.VisibleCards.Count == 3 && f.Board.Visible.All(task => task.Active),
            "The queued completion expires after its remaining forty seconds and removes only the overflow label");
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .5;

    private static bool IsBelowLastCard(SpeechBubble bubble)
    {
        if (bubble.VisibleCards.Count == 0) return false;
        var content = (UIElement)bubble.Content;
        var last = bubble.VisibleCards[^1].Surface;
        Point lastTop = last.TranslatePoint(new Point(), content);
        Point labelTop = bubble.Overflow.Surface.TranslatePoint(new Point(), content);
        return labelTop.Y >= lastTop.Y + last.ActualHeight - .5;
    }

    private static bool IsContained(SpeechBubble bubble)
    {
        var label = bubble.Overflow.Surface;
        Point top = label.TranslatePoint(new Point(), (UIElement)bubble.Content);
        return label.ActualWidth > 0 && label.ActualHeight > 0 && top.X >= -.5 && top.Y >= -.5
            && top.X + label.ActualWidth <= bubble.ActualWidth + .5
            && top.Y + label.ActualHeight <= bubble.ActualHeight + .5;
    }
}
