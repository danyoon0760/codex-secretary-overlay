using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using Point = System.Windows.Point;

namespace SecretaryOverlay;

internal sealed partial class SpeechBubble
{
    private readonly Func<bool> allowReflow;
    private BubbleReflowMotion? reflow;
    private Rect targetBounds;
    private Rect transitionBounds;
    private bool rendering;
    private bool reflowListening;
    internal bool IsReflowing => reflow is not null;
    internal bool IsPresenting => cards.Values.Any(card => !card.PresentationComplete);

    private IEnumerable<(string Key, FrameworkElement Surface)> ReflowSurfaces()
    {
        foreach (var (key, card) in cards) yield return (key, card.Surface);
        if (Overflow.Surface.Visibility == Visibility.Visible) yield return ("overflow", Overflow.Surface);
    }

    private Dictionary<string, Rect> CaptureCardBounds()
    {
        if (!IsVisible) return new();
        AdvanceReflow();
        return ReflowSurfaces().ToDictionary(pair => pair.Key, pair =>
        {
            var point = pair.Surface.TranslatePoint(new Point(), frame);
            return new Rect(new Point(Left + point.X, Top + point.Y), pair.Surface.RenderSize);
        });
    }

    private void Relayout(IReadOnlyDictionary<string, Rect> previous, bool structureChanged, bool firstChanged = false)
    {
        if (positioning || !IsVisible || Owner is null || stack.Children.Count == 0) return;
        positioning = true;
        try
        {
            ClearTransforms();
            targetBounds = MeasurePlacement(firstChanged && targetBounds.Height > 0 ? targetBounds.Top : null);
            var offsets = new Dictionary<string, Vector>();
            transitionBounds = targetBounds;
            foreach (var (key, surface) in ReflowSurfaces())
            {
                if (!previous.TryGetValue(key, out var old)) continue;
                var local = surface.TranslatePoint(new Point(), layout);
                offsets[key] = old.TopLeft - new Point(targetBounds.Left + local.X, targetBounds.Top + local.Y);
                // Keep the entire route inside the HWND, including a card whose text grew mid-motion.
                transitionBounds.Union(new Rect(old.TopLeft, surface.RenderSize));
            }
            long time = now();
            bool moved = offsets.Values.Any(offset => offset.Length > .5);
            if (structureChanged)
                reflow = allowReflow() && moved ? new BubbleReflowMotion(time, offsets) : null;
            else if (reflow is not null && !reflow.IsComplete(time) && allowReflow())
                reflow.Rebase(offsets, time);
            else reflow = null;
            ApplyReflowFrame(time);
            if ((reflow is not null || IsPresenting) && !reflowListening)
            {
                CompositionTarget.Rendering += OnReflowFrame;
                reflowListening = true;
            }
        }
        finally { positioning = false; }
    }

    private void OnReflowFrame(object? sender, EventArgs e) => AdvanceReflow();

    internal void AdvanceReflow()
    {
        if ((reflow is null && !IsPresenting) || positioning || rendering) return;
        positioning = true;
        try { ApplyReflowFrame(now()); }
        finally { positioning = false; }
    }

    private void ApplyReflowFrame(long time)
    {
        if (reflow is not null && (reflow.IsComplete(time) || !allowReflow())) reflow = null;
        if (reflow is null)
        {
            ClearTransforms();
            SizeToContent = SizeToContent.Height;
            Width = targetBounds.Width;
            Left = targetBounds.Left;
            Top = targetBounds.Top;
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            Width = transitionBounds.Width;
            Height = transitionBounds.Height;
            Left = transitionBounds.Left;
            Top = transitionBounds.Top;
            layout.RenderTransform = new TranslateTransform(targetBounds.Left - Left, targetBounds.Top - Top);
            foreach (var (key, surface) in ReflowSurfaces())
            {
                var offset = reflow.Offset(key, time);
                surface.RenderTransform = new TranslateTransform(offset.X, offset.Y);
            }
        }
        foreach (var card in cards.Values) card.AdvancePresentation(time, reflow is not null, allowReflow());
        UpdateLayout();
        UpdateAutoClose();
        if (reflow is null && !IsPresenting) StopReflowFrames();
    }

    private void CancelReflow()
    {
        reflow = null;
        StopReflowFrames();
        ClearTransforms();
        foreach (var card in cards.Values) card.FinishPresentation(now());
        SizeToContent = SizeToContent.Height;
    }

    private void StopReflowFrames()
    {
        if (!reflowListening) return;
        CompositionTarget.Rendering -= OnReflowFrame;
        reflowListening = false;
    }

    private void ClearTransforms()
    {
        layout.RenderTransform = Transform.Identity;
        Overflow.Surface.RenderTransform = Transform.Identity;
        foreach (var card in cards.Values) card.Surface.RenderTransform = Transform.Identity;
    }
}
