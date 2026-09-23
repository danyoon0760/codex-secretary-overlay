using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace SecretaryOverlay;

internal sealed class ScrollMotion(double offset)
{
    public double Position { get; private set; } = offset;
    public double Target { get; private set; } = offset;
    public bool Finished => Math.Abs(Target - Position) < .25;

    public void Add(double distance, double actual, double maximum)
    {
        // Reversing the wheel changes direction immediately, without old momentum fighting it.
        if (Math.Sign(distance) != Math.Sign(Target - actual)) Target = actual;
        Position = actual;
        Target = Math.Clamp(Target + distance, 0, maximum);
    }

    public double Advance(double seconds, double maximum)
    {
        Target = Math.Clamp(Target, 0, maximum);
        Position = Math.Clamp(Position, 0, maximum);
        Position += (Target - Position) * (1 - Math.Exp(-Math.Clamp(seconds, 0, .05) / .055));
        if (Finished) Position = Target;
        return Position;
    }

    internal static double WheelDistance(int delta, int lines, double viewport) =>
        -delta / 120.0 * (lines < 0 ? viewport * .9 : lines * 24.0);
}

// One handler on the settings surface also covers the TextBox template's inner ScrollViewer.
// Render callbacks exist only during motion; tab changes, keyboard input and scrollbar drags stop it.
internal sealed class SmoothScrolling
{
    private readonly Dictionary<ScrollViewer, ScrollMotion> motions = new();
    private readonly Dictionary<ScrollViewer, double> requested = new();
    private long lastFrame;
    private bool rendering;
    internal int ActiveCount => motions.Count;

    public static SmoothScrolling Attach(FrameworkElement root)
    {
        var behavior = new SmoothScrolling();
        root.PreviewMouseWheel += (_, e) =>
        {
            if (e.Handled || e.OriginalSource is not DependencyObject source) return;
            e.Handled = behavior.Wheel(source, e.Delta, SystemParameters.WheelScrollLines, SystemParameters.ClientAreaAnimation);
        };
        root.PreviewMouseDown += (_, _) => behavior.Stop();
        root.PreviewKeyDown += (_, _) => behavior.Stop();
        root.Unloaded += (_, _) => behavior.Stop();
        return behavior;
    }

    internal bool Wheel(DependencyObject source, int delta, int lines, bool animate)
    {
        if (delta == 0 || lines == 0) return false;
        var ancestors = Ancestors(source).ToArray();
        // Preserve native model selection behavior, including the popup's own scrolling.
        if (ancestors.Any(x => x is ComboBox)) return false;
        // A reversal can arrive before the first frame, while the actual offset is still at an edge.
        foreach (var (pending, pendingMotion) in motions.ToArray())
            if (Math.Sign(pendingMotion.Target - pending.VerticalOffset) == Math.Sign(delta)) Remove(pending);
        DetachWhenIdle();
        var viewers = ancestors.OfType<ScrollViewer>().ToList();
        if (ancestors.OfType<TextBox>().FirstOrDefault() is { } editor)
        {
            editor.ApplyTemplate();
            if (editor.Template.FindName("PART_ContentHost", editor) is ScrollViewer inner && !viewers.Contains(inner)) viewers.Insert(0, inner);
        }
        var viewer = viewers.FirstOrDefault(v => v.ScrollableHeight > .5 && (delta < 0
            ? v.VerticalOffset < v.ScrollableHeight - .5 : v.VerticalOffset > .5));
        if (viewer is null) return viewers.Count > 0;
        // Once the editor reaches its edge, let the outer page take over exclusively.
        foreach (var other in motions.Keys.Where(v => v != viewer).ToArray()) Remove(other);
        double distance = ScrollMotion.WheelDistance(delta, lines, viewer.ViewportHeight);
        if (!animate)
        {
            Stop(); viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset + distance, 0, viewer.ScrollableHeight)); return true;
        }
        if (!motions.TryGetValue(viewer, out var motion))
        {
            motion = new(viewer.VerticalOffset); motions.Add(viewer, motion);
            requested[viewer] = viewer.VerticalOffset;
            viewer.ScrollChanged += Scrolled;
        }
        motion.Add(distance, viewer.VerticalOffset, viewer.ScrollableHeight);
        if (!rendering)
        {
            lastFrame = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += Render;
            rendering = true;
        }
        return true;
    }

    private void Render(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double seconds = (now - lastFrame) / (double)Stopwatch.Frequency;
        lastFrame = now;
        Advance(seconds);
    }

    internal void Advance(double seconds)
    {
        foreach (var (viewer, motion) in motions.ToArray())
        {
            if (!viewer.IsVisible) { Remove(viewer); continue; }
            double next = motion.Advance(seconds, viewer.ScrollableHeight);
            requested[viewer] = next;
            viewer.ScrollToVerticalOffset(next);
            if (motion.Finished) Remove(viewer);
        }
        DetachWhenIdle();
    }

    private void Scrolled(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer || !ReferenceEquals(e.OriginalSource, viewer)) return;
        // Caret navigation, ScrollToEnd and other native scrolling should win over animation.
        if (e.VerticalChange != 0 && requested.TryGetValue(viewer, out double expected) && Math.Abs(e.VerticalOffset - expected) > 1.1)
        { Remove(viewer); DetachWhenIdle(); }
    }

    internal void Stop()
    {
        foreach (var viewer in motions.Keys.ToArray()) Remove(viewer);
        DetachWhenIdle();
    }
    private void Remove(ScrollViewer viewer)
    {
        viewer.ScrollChanged -= Scrolled;
        motions.Remove(viewer); requested.Remove(viewer);
    }
    private void DetachWhenIdle()
    {
        if (motions.Count != 0 || !rendering) return;
        CompositionTarget.Rendering -= Render; rendering = false;
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject? value)
    {
        while (value is not null)
        {
            yield return value;
            value = value is Visual or Visual3D ? VisualTreeHelper.GetParent(value)
                : value is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(value);
        }
    }
}
