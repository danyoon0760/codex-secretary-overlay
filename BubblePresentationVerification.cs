using System.Windows;
using Point = System.Windows.Point;

namespace SecretaryOverlay;

internal static class BubblePresentationVerification
{
    private sealed class Fixture : IDisposable
    {
        public long Now { get; set; }
        public Window Owner { get; }
        public SpeechBubble Bubble { get; }

        public Fixture()
        {
            var area = SystemParameters.WorkArea;
            Owner = new Window { Width = 260, Height = 300, Left = area.Left + area.Width * .55, Top = area.Top + area.Height * .35,
                Opacity = 0, ShowActivated = false, ShowInTaskbar = false, IsHitTestVisible = false };
            Owner.Show(); Owner.UpdateLayout();
            Bubble = new SpeechBubble(Owner, () => Now, () => true) { Opacity = 0, IsHitTestVisible = false };
        }
        public void Show(params TaskProgress[] tasks) { Bubble.SetTasks(tasks); Bubble.UpdateLayout(); }
        public void Advance(long time) { Now = time; Bubble.AdvanceReflow(); Bubble.UpdateLayout(); }
        public void Expire(long time) { Now = time; Bubble.ExpireOlderBubbles(); Bubble.UpdateLayout(); }
        public void Dispose() { Bubble.Close(); Owner.Close(); }
    }

    private static TaskProgress Task(string id, string body = "진행 중인 작업을 확인하고 있어요.") => new(id)
        { Project = "비서", ChatTitle = id, Body = body, Detail = "작업 중" };
    private static double Y(SpeechBubble bubble, BubbleCard card) => bubble.Top + card.Surface.TranslatePoint(new Point(), (UIElement)bubble.Content).Y;
    private static bool Near(double left, double right) => Math.Abs(left - right) < 1;

    public static void Check(Action<bool, string> check)
    {
        CheckStages(check);
        CheckReadinessAndPromotion(check);
        CheckHiddenCancellation(check);
    }

    private static void CheckStages(Action<bool, string> check)
    {
        using var fixture = new Fixture();
        var existing = Task("기존 작업");
        fixture.Show(existing);
        var original = fixture.Bubble.VisibleCards.Single();
        check(original.Surface.Opacity == 0 && original.DisplayedText == "" && fixture.Bubble.IsPresenting && !fixture.Bubble.IsReflowing,
            "The first real WPF bubble starts with an invisible surface and empty visible text without requiring a stack movement");
        fixture.Advance(75);
        check(original.Surface.Opacity > 0 && original.Surface.Opacity < 1 && original.DisplayedText == "",
            "The first bubble fades in before any visible body text is revealed");
        fixture.Advance(150);
        check(original.Surface.Opacity == 1 && original.DisplayedText == "" && !original.PresentationComplete,
            "The first 150 ms entrance completes before starting its text reveal");
        fixture.Advance(2000);
        check(original.DisplayedText == existing.Body && original.PresentationComplete && !fixture.Bubble.IsPresenting,
            "The first bubble finishes revealing its complete body and releases its presentation state");

        var newest = Task("새 작업", "새 말풍선을 표시할 자리를 먼저 만들고 있어요.\n기존 말풍선이 아래로 이동한 뒤 새 창이 나타나요.\n이제 도착한 설명을 차례대로 보여드립니다.");
        double oldY = Y(fixture.Bubble, original);
        fixture.Now = 2100; fixture.Show(newest, existing);
        var added = fixture.Bubble.VisibleCards[0];
        double reservedHeight = added.Surface.ActualHeight;
        check(fixture.Bubble.IsReflowing && Near(Y(fixture.Bubble, original), oldY)
            && added.Surface.Opacity == 0 && added.DisplayedText == "" && !added.Surface.IsHitTestVisible,
            "An inserted bubble reserves its full size while remaining invisible and noninteractive during the old card's movement");
        fixture.Advance(2190);
        check(Y(fixture.Bubble, original) > oldY && fixture.Bubble.IsReflowing && added.Surface.Opacity == 0 && added.DisplayedText == "",
            "Existing cards move down first while the new card and its body remain hidden");
        fixture.Advance(2280);
        check(!fixture.Bubble.IsReflowing && added.Surface.Opacity == 0 && added.DisplayedText == "" && fixture.Bubble.IsPresenting,
            "The new card begins its entrance only after the original 180 ms layout movement ends");
        fixture.Advance(2355);
        check(added.Surface.Opacity > 0 && added.Surface.Opacity < 1 && added.DisplayedText == "" && Near(added.Surface.ActualHeight, reservedHeight),
            "The new surface fades in over the next 150 ms without revealing text or changing its reserved height");
        fixture.Advance(2430);
        check(added.Surface.Opacity == 1 && added.DisplayedText == "" && Equals(added.Surface.ReadLocalValue(UIElement.IsHitTestVisibleProperty), true),
            "The completed entrance enables interaction and starts the new card's text reveal");
        fixture.Advance(2520);
        check(added.DisplayedText.Length > 0 && added.DisplayedText.Length < newest.Body.Length && newest.Body.StartsWith(added.DisplayedText, StringComparison.Ordinal)
            && Near(added.Surface.ActualHeight, reservedHeight) && !fixture.Bubble.IsReflowing,
            "Text then appears as a real prefix while the fully reserved card layout remains stationary");
        fixture.Advance(4000);
        check(added.DisplayedText == newest.Body && added.PresentationComplete && !fixture.Bubble.IsPresenting
            && Near(added.Surface.ActualHeight, reservedHeight),
            "The staged insertion ends with the entire text visible at exactly its reserved natural height");

        string prefix = added.DisplayedText;
        newest.Body += " 추가로 확인한 내용도 이어서 표시합니다.";
        fixture.Now = 4100; fixture.Show(newest, existing);
        check(ReferenceEquals(added, fixture.Bubble.VisibleCards[0]) && added.Surface.Opacity == 1
            && added.DisplayedText.StartsWith(prefix, StringComparison.Ordinal) && !fixture.Bubble.IsReflowing,
            "An appended body chunk reuses the visible card and prefix without replaying entrance or stack movement");
        fixture.Advance(4140);
        check(added.DisplayedText.Length > prefix.Length && added.DisplayedText.StartsWith(prefix, StringComparison.Ordinal)
            && added.Surface.Opacity == 1, "An existing card progressively adds only its newly arrived suffix");
        fixture.Advance(6000);
        check(added.DisplayedText == newest.Body && !fixture.Bubble.IsPresenting, "An appended suffix completes without leaving an active presentation callback");
    }

    private static void CheckReadinessAndPromotion(Action<bool, string> check)
    {
        using var fixture = new Fixture();
        var completed = Task("완료", "작업 결과를 모두 정리했습니다.");
        completed.Active = false; completed.State = "Stop";
        var active = Task("계속 작업 중"); var second = Task("두 번째 작업"); var overflow = Task("대기 중인 작업");
        var expired = new List<string>();
        fixture.Bubble.TasksExpired += sessions => expired.AddRange(sessions);
        fixture.Show(completed, active, second, overflow);
        var oldActive = fixture.Bubble.VisibleCards[1];
        fixture.Advance(150);
        fixture.Advance(2000);
        check(fixture.Bubble.VisibleCards[0].PresentationComplete && expired.Count == 0,
            "A completed task becomes eligible for its reading minute only after the full body is displayed");
        fixture.Expire(61999);
        check(expired.Count == 0 && fixture.Bubble.VisibleCards.Count == 3 && ReferenceEquals(fixture.Bubble.VisibleCards[1], oldActive),
            "Completed text remains present until the last millisecond of its post-reveal reading minute");
        double before = Y(fixture.Bubble, oldActive);
        fixture.Expire(62000);
        check(expired.SequenceEqual([completed.Key]) && ReferenceEquals(fixture.Bubble.VisibleCards[0], oldActive)
            && fixture.Bubble.VisibleCards[2].ChatTitle.Text == overflow.ChatTitle && fixture.Bubble.IsReflowing
            && Near(Y(fixture.Bubble, oldActive), before),
            "After one full reading minute the completed card expires, active survivors are promoted, and active overflow receives a slot");
        fixture.Advance(62100);
        check(Y(fixture.Bubble, oldActive) < before,
            "The promoted active card smoothly fills the expired completed card's space");
        fixture.Advance(62180); fixture.Advance(62330); fixture.Advance(64000);
        fixture.Expire(200000);
        check(expired.Count == 1 && fixture.Bubble.VisibleCards.Count == 3,
            "Active and promoted overflow tasks never inherit a completed task's automatic close timer");
    }

    private static void CheckHiddenCancellation(Action<bool, string> check)
    {
        using var fixture = new Fixture();
        fixture.Show(Task("숨김 확인"));
        fixture.Advance(75);
        fixture.Bubble.Hide();
        fixture.Advance(2000);
        check(!fixture.Bubble.IsVisible && !fixture.Bubble.IsPresenting && !fixture.Bubble.IsReflowing,
            "Hiding mid-entrance cancels presentation frames and cannot reopen the window when a late frame arrives");
    }
}
