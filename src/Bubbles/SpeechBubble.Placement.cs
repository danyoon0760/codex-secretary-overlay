using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace SecretaryOverlay;

internal sealed partial class SpeechBubble
{
    private void Reposition()
    {
        if (positioning || rendering || !IsVisible || Owner is null || stack.Children.Count == 0) return;
        positioning = true;
        try { CancelReflow(); anchorOffset = 0; }
        finally { positioning = false; }
        Relayout(new Dictionary<string, Rect>(), false);
    }

    private Rect MeasurePlacement(double? keepTop = null)
    {
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(Owner).Handle).WorkingArea;
        var transform = PresentationSource.FromVisual(Owner)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var a = transform.Transform(new Point(screen.Left, screen.Top));
        var b = transform.Transform(new Point(screen.Right, screen.Bottom));
        double width = Math.Min(appearance.Width, Math.Max(1, b.X - a.X - 16));
        stack.Width = width;
        layout.Width = width;
        var workArea = new Rect(a, b);
        if (workArea != lastWorkArea) { onLeft = null; lastWorkArea = workArea; }
        onLeft = BubbleSidePolicy.OnLeft(Owner.Left + Owner.Width / 2, (a.X + b.X) / 2, onLeft);
        foreach (var card in cards.Values) card.PointTail(onLeft.Value);
        UpdateOverflow();
        // Release the previous transition's fixed HWND height before measuring the new
        // natural layout. Measuring only the child leaves its parent layout constraint stale.
        SizeToContent = SizeToContent.Height;
        Width = width;
        UpdateLayout();
        double height = Math.Max(1, layout.ActualHeight);
        double distance = appearance.HeadDistance - BubbleAppearance.Default.HeadDistance;
        double desired = onLeft.Value ? Owner.Left - width + Owner.Width * .38 - distance : Owner.Left + Owner.Width * .62 + distance;
        double left = Math.Clamp(desired, a.X + 8, Math.Max(a.X + 8, b.X - width - 8));
        // Anchor the first card's bottom/tail by the face; older cards extend down from it.
        // Body content keeps its natural height; screen boundaries never create a body scroller.
        var first = (FrameworkElement)stack.Children[0];
        double firstHeight = first.ActualHeight;
        double headAnchor = Owner.Top + Owner.Height * .16;
        // Reserve exactly the inserted card's height by retaining the stack's top during
        // slot changes. Later text growth still expands upward from this card's bottom.
        if (keepTop is { } previousTop) anchorOffset = previousTop + firstHeight - headAnchor;
        double top = BottomAnchoredTop(headAnchor + anchorOffset, firstHeight, height, a.Y, b.Y);
        return new Rect(left, top, width, height);
    }

    internal static double BottomAnchoredTop(double anchor, double firstHeight, double stackHeight, double screenTop, double screenBottom) =>
        Math.Clamp(anchor - firstHeight, screenTop + 8, Math.Max(screenTop + 8, screenBottom - stackHeight - 8));
}
