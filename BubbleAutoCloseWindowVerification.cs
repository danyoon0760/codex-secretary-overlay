using System.Windows;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace SecretaryOverlay;

internal static class BubbleAutoCloseWindowVerification
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
        public int Batches { get; private set; }

        public Fixture(bool animate = false)
        {
            owner.Show();
            Bubble = new SpeechBubble(owner, () => Now, () => animate) { Opacity = 0, IsHitTestVisible = false };
            Bubble.TasksExpired += sessions =>
            {
                Batches++;
                foreach (string session in sessions) Board.Dismiss(session);
                Render(); // Exercise the real nested-render path used by PetWindow.
            };
            Bubble.TaskDismissed += session => { Board.Dismiss(session); Render(); };
        }

        public TaskProgress Start(int id, string turn = "one")
        {
            var task = Board.Accept(new("UserPromptSubmit", $"00000000-0000-4000-8000-{id:000000000000}", Turn: turn), "test")!;
            Render();
            return task;
        }
        public void Complete(TaskProgress task)
        {
            Board.Accept(new("Stop", task.Session, Turn: task.Turn), "test");
            Render();
        }
        public void Render() => Bubble.SetTasks(Board.Tasks.Where(task => !task.Dismissed).ToArray(), Display);
        public void Expire(long now) { Now = now; Bubble.ExpireOlderBubbles(); }
        public void Hover(bool hovered) => Bubble.RaiseEvent(new System.Windows.Input.MouseEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, 0)
            { RoutedEvent = hovered ? System.Windows.Input.Mouse.MouseEnterEvent : System.Windows.Input.Mouse.MouseLeaveEvent });
        public void Dispose() { Bubble.Close(); owner.Close(); }
    }

    public static void Check(Action<bool, string> check)
    {
        CheckActiveQueue(check);
        CheckEveryPosition(check);
        CheckQueuedCompletion(check);
        CheckHover(check);
        CheckSummaryAndNewTurn(check);
        CheckAmbient(check);
        CheckHidden(check);
        CheckDisplayToggle(check);
        CheckPromotedCompletedPresentation(check);
    }

    private static void CheckActiveQueue(Action<bool, string> check)
    {
        using var f = new Fixture();
        var oldest = f.Start(1);
        f.Start(2); f.Start(3);
        f.Expire(1_000_000);
        check(f.Bubble.VisibleCards.Count == 3 && f.Board.Tasks.All(task => task.Active && !task.Dismissed) && f.Batches == 0,
            "Three active WPF bubbles remain indefinitely without an age-based dismissal");
        var newest = f.Start(4);
        f.Expire(2_000_000);
        check(f.Board.Tasks.Count == 4 && f.Bubble.VisibleCards.Count == 3
              && f.Board.Tasks.All(task => task.Active && !task.Dismissed) && !f.Board.Visible.Contains(oldest),
            "A fourth active task waits in the queue without being discarded or forcing an active card to expire");
        f.Board.Apply(new(oldest.Session, oldest.Turn, "queued-update", "대기 중에도 최신 진행 설명을 받아요.", DateTimeOffset.UtcNow));
        f.Render();
        f.Now = 2_100_000; f.Complete(newest);
        f.Expire(2_159_999);
        check(!newest.Dismissed && f.Board.Visible[0] == newest && f.Bubble.VisibleCards.Count == 3,
            "A completed top bubble remains for the full minute before admitting the queue");
        f.Expire(2_160_000);
        check(newest.Dismissed && !newest.Active && f.Board.Visible.Contains(oldest)
              && f.Bubble.VisibleCards.Any(card => card.Body.Text == "대기 중에도 최신 진행 설명을 받아요.")
              && oldest.Active && !oldest.Dismissed && f.Batches == 1,
            "Completion expiry admits the queued active task with its latest text through one nested-render batch");
        f.Board.Apply(new(newest.Session, newest.Turn, "late-final", "뒤늦게 저장된 최종 답변입니다.", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
        f.Render();
        check(newest.Dismissed && !f.Board.Visible.Contains(newest) && f.Bubble.VisibleCards.Count == 3,
            "A late final answer cannot reopen an automatically dismissed completion bubble");
    }

    private static void CheckEveryPosition(Action<bool, string> check)
    {
        using var f = new Fixture();
        var oldest = f.Start(10); var middle = f.Start(11); var newest = f.Start(12);
        f.Now = 1_000; f.Complete(newest);
        f.Now = 2_000; f.Complete(middle);
        f.Now = 3_000; f.Complete(oldest);
        f.Expire(60_999);
        check(f.Bubble.VisibleCards.Count == 3, "Each displayed completion keeps its own full one-minute lifetime");
        f.Expire(61_000);
        check(newest.Dismissed && f.Bubble.VisibleCards.Count == 2 && f.Board.Visible[0] == middle,
            "The newest and topmost completed bubble expires normally");
        f.Expire(62_000);
        check(middle.Dismissed && f.Bubble.VisibleCards.Count == 1 && f.Board.Visible[0] == oldest,
            "Removing another completed card does not restart the remaining card's timer");
        f.Expire(62_999);
        check(!oldest.Dismissed && f.Bubble.IsVisible, "The last completed card remains until its exact existing deadline");
        f.Expire(63_000);
        check(oldest.Dismissed && f.Bubble.VisibleCards.Count == 0 && !f.Bubble.IsVisible && f.Batches == 3,
            "The sole remaining completed bubble also closes and hides the empty window after one minute");
    }

    private static void CheckQueuedCompletion(Action<bool, string> check)
    {
        using var f = new Fixture();
        var queued = f.Start(20);
        f.Start(21); f.Start(22); f.Start(23);
        f.Now = 1_000; f.Complete(queued);
        f.Expire(60_999);
        check(!queued.Dismissed && !f.Board.Visible.Contains(queued),
            "A queued completion is observed even without a rendered card");
        f.Expire(61_000);
        check(queued.Dismissed && f.Bubble.VisibleCards.Count == 3 && f.Board.Visible.All(task => task.Active),
            "A queued completion expires after a minute while all visible active tasks remain");
        f.Bubble.VisibleCards[0].Close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(f.Bubble.VisibleCards.Count == 2 && !f.Board.Visible.Contains(queued),
            "An expired queued completion never reappears when a visible slot later opens");
    }

    private static void CheckHover(Action<bool, string> check)
    {
        using var f = new Fixture();
        var reading = f.Start(30); var working = f.Start(31);
        f.Now = 1_000; f.Complete(reading);
        f.Now = 21_000; f.Hover(true);
        f.Now = 101_000;
        working.Body = "읽는 동안에도 진행 내용은 계속 갱신됩니다.";
        f.Render(); f.Bubble.ExpireOlderBubbles();
        check(!reading.Dismissed && f.Bubble.VisibleCards.Count == 2
              && f.Bubble.VisibleCards.Any(card => card.Body.Text == working.Body),
            "Hover pauses completion expiry after twenty seconds while active text keeps updating");
        f.Hover(false);
        f.Expire(140_999);
        check(!reading.Dismissed && f.Bubble.VisibleCards.Count == 2,
            "Leaving hover preserves the remaining forty seconds of the completion lifetime");
        f.Expire(141_000);
        check(reading.Dismissed && f.Bubble.VisibleCards.Count == 1 && working.Active && !working.Dismissed,
            "Only the completed bubble closes after forty more unpaused seconds");
    }

    private static void CheckSummaryAndNewTurn(Action<bool, string> check)
    {
        using (var f = new Fixture())
        {
            f.Board.Style = CompletionStyle.D;
            var task = f.Start(40);
            f.Board.Apply(new(task.Session, task.Turn, "final", "설정을 변경했습니다.", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
            f.Now = 1_000; f.Complete(task);
            f.Now = 2_000; task.BeginSummary(); task.StreamingReasoning = "완료 문구를 정리하고 있습니다.";
            f.Board.RefreshCompletions(); f.Render();
            f.Expire(200_000);
            check(task.SummaryPending && !task.Dismissed && f.Bubble.VisibleCards.Count == 0 && !f.Bubble.IsVisible,
                "A pending AI completion summary stays hidden without timeout even when Stop arrived long ago");
            f.Now = 210_000; task.StreamingCompletion = "결과를 요약하고 있습니다.";
            f.Board.RefreshCompletions(); f.Render();
            f.Expire(300_000);
            check(!task.Dismissed, "Partial completion-summary output remains ineligible for automatic dismissal");
            task.NaturalCompletion = new("이제 설정을 편하게 바꿀 수 있습니다.", "적용 완료");
            task.FinishSummary(); f.Board.RefreshCompletions(); f.Render();
            f.Expire(359_999);
            check(!task.Dismissed && f.Bubble.VisibleCards[0].Body.Text == "이제 설정을 편하게 바꿀 수 있습니다.",
                "A completed AI summary receives a full new minute after its final displayed text");
            f.Expire(360_000);
            check(task.Dismissed && f.Bubble.VisibleCards.Count == 0,
                "The final AI summary expires exactly one minute after readiness");
        }
        using (var f = new Fixture())
        {
            var task = f.Start(41);
            f.Now = 1_000; f.Complete(task);
            long oldIdentity = task.BubbleIdentity;
            f.Now = 20_000; var next = f.Start(41, "two");
            f.Expire(100_000);
            check(next.Active && !next.Dismissed && next.BubbleIdentity != oldIdentity && task.Dismissed
                && task.BubbleIdentity == oldIdentity && f.Bubble.VisibleCards.Count == 1,
                "A new question keeps its own active card while the prior completed question expires independently");
        }
    }

    private static void CheckAmbient(Action<bool, string> check)
    {
        using var f = new Fixture();
        long stream = f.Bubble.BeginAmbientStream();
        check(f.Bubble.UpdateAmbientStream(stream, "화면을 확인하고 있습니다.", "생각하는 중"),
            "A real ambient stream creates its first visible message");
        f.Expire(200_000);
        check(f.Bubble.VisibleCards.Count == 1 && f.Bubble.IsVisible,
            "Ambient partial output stays visible indefinitely while the request is streaming");
        f.Now = 201_000;
        check(f.Bubble.CompleteAmbientStream(stream, "화면에 표시된 내용을 확인했습니다."),
            "Completing the ambient stream explicitly makes its final message ready");
        f.Expire(260_999);
        check(f.Bubble.VisibleCards.Count == 1, "A completed ambient message remains for its full minute");
        f.Expire(261_000);
        check(f.Bubble.VisibleCards.Count == 0 && !f.Bubble.IsVisible
              && !f.Bubble.UpdateAmbientStream(stream, "늦게 도착한 글"),
            "A sole completed ambient message expires and rejects late text from that request");
    }

    private static void CheckHidden(Action<bool, string> check)
    {
        using (var f = new Fixture())
        {
            f.Bubble.Say("이전 한마디입니다.");
            f.Now = 1_000; var task = f.Start(50);
            f.Now = 2_000; long pending = f.Bubble.BeginAmbientStream();
            f.Now = 3_000; f.Bubble.Hide();
            f.Expire(60_000);
            check(!f.Bubble.IsVisible && f.Bubble.VisibleCards.Count == 1 && task.Active && !task.Dismissed,
                "Expiring completed ambient text does not reopen a hidden window or dismiss an active task");
            check(f.Bubble.UpdateAmbientStream(pending, "새 요청의 첫 글입니다.") && f.Bubble.VisibleCards.Count == 2,
                "Expiring old ambient text preserves a newer request that is still waiting for its first token");
            f.Now = 61_000; f.Complete(task); f.Bubble.Hide();
            f.Expire(121_000);
            check(!f.Bubble.IsVisible && task.Dismissed && f.Bubble.VisibleCards.Count == 1 && f.Board.Visible.Length == 0,
                "Task expiry and its nested render preserve a hidden unfinished ambient stream");
            f.Expire(500_000);
            check(!f.Bubble.IsVisible && f.Bubble.VisibleCards.Count == 1,
                "A hidden unfinished ambient request still has no completion deadline");
        }
        using (var f = new Fixture())
        {
            var task = f.Start(51); f.Complete(task);
            f.Now = 20_000; f.Hover(true);
            f.Now = 30_000; f.Bubble.Hide();
            f.Expire(69_999);
            check(!task.Dismissed && !f.Bubble.IsVisible,
                "Hiding a hovered bubble resumes its remaining time without immediately expiring it");
            f.Expire(70_000);
            check(task.Dismissed && !f.Bubble.IsVisible && f.Bubble.VisibleCards.Count == 0,
                "A hidden hovered completion expires after its remaining forty seconds without reopening");
        }
    }

    private static void CheckDisplayToggle(Action<bool, string> check)
    {
        using (var f = new Fixture())
        {
            var task = f.Start(60); f.Complete(task);
            f.Now = 20_000; f.Display = false; f.Render();
            check(!f.Bubble.IsVisible && f.Bubble.VisibleCards.Count == 0 && !task.Dismissed,
                "Disabling task display hides its cards while preserving their completion observations");
            f.Now = 50_000; f.Display = true; f.Render();
            f.Expire(59_999);
            check(f.Bubble.IsVisible && f.Bubble.VisibleCards.Count == 1 && !task.Dismissed,
                "Re-enabling task display preserves the completed card until its original deadline");
            f.Expire(60_000);
            check(task.Dismissed && f.Bubble.VisibleCards.Count == 0 && !f.Bubble.IsVisible,
                "Hiding at twenty seconds and restoring at fifty does not restart the original completion minute");
        }
        using (var f = new Fixture())
        {
            f.Display = false;
            var task = f.Start(61);
            f.Board.Apply(new(task.Session, task.Turn, "hidden-update", "숨겨진 동안에도 작업 내용을 받고 있습니다.", DateTimeOffset.UtcNow));
            f.Render(); f.Expire(1_000_000);
            check(task.Active && !task.Dismissed && !f.Bubble.IsVisible && f.Bubble.VisibleCards.Count == 0,
                "A new active task received with display disabled stays tracked indefinitely without appearing or expiring");
            f.Display = true; f.Render();
            check(f.Bubble.VisibleCards.Count == 1
                  && f.Bubble.VisibleCards[0].Body.Text == "숨겨진 동안에도 작업 내용을 받고 있습니다.",
                "Restoring task display reveals the latest text of work that started while hidden");
        }
    }

    private static void CheckPromotedCompletedPresentation(Action<bool, string> check)
    {
        using var f = new Fixture(animate: true);
        var queued = f.Start(70);
        f.Start(71); f.Start(72); f.Start(73);
        f.Complete(queued); // It completes in the queue at time zero, so the original deadline is 60,000.
        f.Now = 50_000;
        f.Board.Promote(queued.Key); f.Render();
        check(f.Board.Visible[0] == queued && !f.Bubble.VisibleCards[0].PresentationComplete && !queued.Dismissed,
            "Promoting a completed queue entry starts its staged entrance without changing its completion identity");
        f.Expire(50_180); // Finish movement, then begin entrance.
        f.Expire(50_330); // Finish entrance, then begin revealing the existing text.
        f.Expire(51_530); // Finish even the longest permitted text reveal.
        f.Expire(59_999);
        check(!queued.Dismissed && f.Bubble.VisibleCards[0].PresentationComplete
              && f.Bubble.VisibleCards[0].DisplayedText == queued.Body,
            "The promoted completed entry finishes its staged display within its existing lifetime");
        f.Expire(60_000);
        check(queued.Dismissed && !f.Board.Visible.Contains(queued) && f.Board.Visible.All(task => task.Active),
            "Revealing the same completed queue text after promotion does not extend its original one-minute deadline");
    }
}
