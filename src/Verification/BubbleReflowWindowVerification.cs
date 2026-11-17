using System.Windows;
using Point = System.Windows.Point;

namespace SecretaryOverlay;

internal static class BubbleReflowWindowVerification
{
    private sealed class Fixture : IDisposable
    {
        public long Now { get; set; }
        public Window Owner { get; }
        public SpeechBubble Bubble { get; }

        public Fixture(bool animate = true)
        {
            var work = SystemParameters.WorkArea;
            Owner = new Window
            {
                Width = 260, Height = 300, Left = work.Left + work.Width * .55,
                Top = work.Top + work.Height * .35, Opacity = 0,
                ShowActivated = false, ShowInTaskbar = false, IsHitTestVisible = false
            };
            Owner.Show(); Owner.UpdateLayout();
            Bubble = new SpeechBubble(Owner, () => Now, () => animate)
            { Opacity = 0, IsHitTestVisible = false };
        }

        public void Show(params TaskProgress[] tasks) { Bubble.SetTasks(tasks); Bubble.UpdateLayout(); }
        public void Advance(long now) { Now = now; Bubble.AdvanceReflow(); Bubble.UpdateLayout(); }
        public void Dispose() { Bubble.Close(); Owner.Close(); }
    }

    private static TaskProgress Task(string id) => new(id) { Body = "작업을 확인하고 있어요.", Detail = "작업 중" };
    private static double Y(SpeechBubble window, BubbleCard card) =>
        window.Top + card.Surface.TranslatePoint(new Point(), (UIElement)window.Content).Y;
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1;
    private static bool ContainsCards(SpeechBubble window)
    {
        var bounds = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
        bounds.Inflate(1, 1); // Permit pixel rounding when the monitor uses display scaling.
        return window.VisibleCards.All(card =>
        {
            var point = card.Surface.TranslatePoint(new Point(), (UIElement)window.Content);
            return bounds.Contains(new Rect(new Point(window.Left + point.X, window.Top + point.Y), card.Surface.RenderSize));
        });
    }

    public static void Check(Action<bool, string> check)
    {
        CheckInsertion(check);
        CheckRemoval(check);
        CheckLiveUpdates(check);
        CheckInterruptedInsertion(check);
        CheckCancellation(check);
        CheckReducedMotion(check);
    }

    private static void CheckInsertion(Action<bool, string> check)
    {
        using var fixture = new Fixture();
        var a = Task("insert-a"); var b = Task("insert-b"); var c = Task("insert-c");
        fixture.Show(a, b);
        var oldA = fixture.Bubble.VisibleCards[0]; var oldB = fixture.Bubble.VisibleCards[1];
        double aStart = Y(fixture.Bubble, oldA), bStart = Y(fixture.Bubble, oldB);
        fixture.Now = 10; fixture.Show(c, a, b);
        check(fixture.Bubble.IsReflowing && Near(Y(fixture.Bubble, oldA), aStart) && Near(Y(fixture.Bubble, oldB), bStart),
            "Inserting a top bubble preserves each surviving card's screen position at animation start");
        fixture.Advance(100);
        double aMiddle = Y(fixture.Bubble, oldA), bMiddle = Y(fixture.Bubble, oldB);
        check(fixture.Bubble.IsReflowing && aMiddle > aStart && bMiddle > bStart && ContainsCards(fixture.Bubble),
            "Existing cards move downward through real intermediate WPF positions inside their window");
        fixture.Advance(190);
        check(!fixture.Bubble.IsReflowing && Y(fixture.Bubble, oldA) > aMiddle && Y(fixture.Bubble, oldB) > bMiddle
            && ContainsCards(fixture.Bubble), "Insertion settles all surviving cards at their new positions after 180 ms");
    }

    private static void CheckRemoval(Action<bool, string> check)
    {
        using var fixture = new Fixture();
        var a = Task("remove-a"); var b = Task("remove-b"); var c = Task("remove-c");
        fixture.Show(a, b, c);
        var bottom = fixture.Bubble.VisibleCards[2];
        double start = Y(fixture.Bubble, bottom);
        fixture.Now = 10; fixture.Show(a, c);
        check(fixture.Bubble.IsReflowing && Near(Y(fixture.Bubble, bottom), start) && ContainsCards(fixture.Bubble),
            "Removing a middle bubble preserves the lower card and keeps its old rectangle inside the shrinking window");
        fixture.Advance(100);
        double middle = Y(fixture.Bubble, bottom);
        check(fixture.Bubble.IsReflowing && middle < start && ContainsCards(fixture.Bubble),
            "Gap removal moves the surviving card upward without clipping it during window shrinkage");
        fixture.Advance(190);
        check(!fixture.Bubble.IsReflowing && Y(fixture.Bubble, bottom) < middle && ContainsCards(fixture.Bubble),
            "The smaller window contains the complete settled stack after removal finishes");
    }

    private static void CheckLiveUpdates(Action<bool, string> check)
    {
        using var fixture = new Fixture();
        var a = Task("text-a"); var b = Task("text-b"); var c = Task("text-c");
        fixture.Show(a, b);
        fixture.Now = 10; fixture.Show(c, a, b);
        fixture.Advance(70);
        var existing = fixture.Bubble.VisibleCards.ToArray();
        var before = existing.Select(card => Y(fixture.Bubble, card)).ToArray();
        a.Body = string.Join("\n", Enumerable.Range(1, 14).Select(line => $"실시간으로 도착한 긴 설명 {line}"));
        fixture.Show(c, a, b);
        check(existing.Select((card, index) => Near(Y(fixture.Bubble, card), before[index])).All(value => value)
            && ContainsCards(fixture.Bubble),
            "A streamed body growing mid-motion keeps every current card position and expands the transition window");
        fixture.Advance(189);
        check(fixture.Bubble.IsReflowing, "Live text updates keep the current reflow active until its original deadline");
        fixture.Advance(190);
        check(!fixture.Bubble.IsReflowing && existing[1].Body.Text == a.Body && ContainsCards(fixture.Bubble),
            "Live body changes do not restart the original 180 ms transition or postpone its end");
        using (var immediate = new Fixture(false))
        {
            immediate.Show(c, a, b);
            double naturalHeight = immediate.Bubble.VisibleCards.Sum(card => card.Surface.ActualHeight + card.Surface.Margin.Top + card.Surface.Margin.Bottom);
            check(existing[1].Body.ActualHeight >= existing[1].Body.DesiredSize.Height - 1
                && existing[1].Surface.ActualHeight > existing[1].Body.DesiredSize.Height
                && Near(fixture.Bubble.ActualHeight, immediate.Bubble.ActualHeight)
                && Near(fixture.Bubble.ActualHeight, naturalHeight),
                "Fourteen streamed lines finish at the complete natural stack height, matching immediate layout without clipped text");
        }
        a.Body = "이제 마지막 문장이 도착했어요.";
        fixture.Show(c, a, b);
        check(!fixture.Bubble.IsReflowing && existing[1].Body.Text == a.Body,
            "A text-only update while idle is displayed immediately without starting a card transition");
    }

    private static void CheckInterruptedInsertion(Action<bool, string> check)
    {
        using var fixture = new Fixture();
        var a = Task("burst-a"); var b = Task("burst-b"); var c = Task("burst-c");
        fixture.Show(a);
        fixture.Now = 10; fixture.Show(b, a);
        fixture.Advance(70);
        var prior = fixture.Bubble.VisibleCards.ToArray();
        var before = prior.Select(card => Y(fixture.Bubble, card)).ToArray();
        fixture.Show(c, b, a);
        check(fixture.Bubble.IsReflowing && prior.Select((card, index) => Near(Y(fixture.Bubble, card), before[index])).All(value => value),
            "Another arriving card rebases an interrupted insertion from current screen positions without a jump");
        fixture.Advance(160);
        check(prior.Select((card, index) => Y(fixture.Bubble, card) > before[index]).All(value => value) && ContainsCards(fixture.Bubble),
            "Survivors continue downward smoothly after a second insertion interrupts the first without clipping");
        fixture.Advance(250);
        check(!fixture.Bubble.IsReflowing && ContainsCards(fixture.Bubble),
            "The interrupted transition settles 180 ms after the latest structural change");
    }

    private static void CheckCancellation(Action<bool, string> check)
    {
        using (var moved = new Fixture())
        {
            var a = Task("drag-a"); var b = Task("drag-b");
            moved.Show(a); moved.Now = 10; moved.Show(b, a); moved.Advance(70);
            moved.Owner.Left += 15; moved.Owner.UpdateLayout(); moved.Bubble.UpdateLayout();
            check(!moved.Bubble.IsReflowing && ContainsCards(moved.Bubble),
                "Dragging the character cancels reflow and immediately positions complete cards beside their owner");
        }
        using (var hidden = new Fixture())
        {
            var a = Task("hide-a"); var b = Task("hide-b");
            hidden.Show(a); hidden.Now = 10; hidden.Show(b, a); hidden.Advance(70);
            hidden.Bubble.Hide(); hidden.Advance(190);
            check(!hidden.Bubble.IsVisible && !hidden.Bubble.IsReflowing,
                "Hiding during reflow cancels its frames without resurrecting the bubble at completion");
        }
    }

    private static void CheckReducedMotion(Action<bool, string> check)
    {
        using var fixture = new Fixture(false);
        var a = Task("reduced-a"); var b = Task("reduced-b");
        fixture.Show(a);
        var survivor = fixture.Bubble.VisibleCards[0];
        double before = Y(fixture.Bubble, survivor);
        fixture.Now = 10; fixture.Show(b, a);
        check(!fixture.Bubble.IsReflowing && Y(fixture.Bubble, survivor) > before && ContainsCards(fixture.Bubble),
            "Reduced-motion mode places cards immediately at the final layout without an animation");
    }
}
