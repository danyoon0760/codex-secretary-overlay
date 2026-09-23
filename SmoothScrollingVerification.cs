using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace SecretaryOverlay;

internal static class SmoothScrollingVerification
{
    public static void Check(Action<bool, string> check)
    {
        var motion = new ScrollMotion(0);
        motion.Add(72, 0, 1000);
        double first = motion.Advance(.016, 1000);
        check(first > 0 && first < 72, "A wheel step moves through intermediate offsets rather than jumping to its target");
        motion.Add(72, first, 1000);
        check(motion.Target == 144, "Successive wheel input extends the destination without dropping earlier input");
        motion.Add(-72, first, 1000);
        check(motion.Target == 0, "Reversing the wheel immediately cancels the previous direction's remaining motion");
        motion.Add(10000, first, 300);
        for (int i = 0; i < 60; i++) motion.Advance(.016, 300);
        check(motion.Position == 300 && motion.Finished, "Scrolling settles exactly at the content boundary without overshoot");
        check(ScrollMotion.WheelDistance(-15, 3, 400) == 9 && ScrollMotion.WheelDistance(-120, -1, 400) == 360
            && ScrollMotion.WheelDistance(-120, 0, 400) == 0, "Wheel distance preserves partial deltas and Windows line/page/disabled preferences");
        var editor = new TextBox { Text = string.Join("\n", Enumerable.Range(1, 60).Select(i => "지침 " + i)), Height = 100,
            AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var rows = new StackPanel(); rows.Children.Add(editor); rows.Children.Add(new Border { Height = 1500 });
        var viewer = new ScrollViewer { Content = rows, CanContentScroll = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new Border { Child = viewer };
        var behavior = SmoothScrolling.Attach(root);
        var window = new Window { Width = 400, Height = 320, Content = root, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout(); editor.ApplyTemplate();
            var inner = (ScrollViewer)editor.Template.FindName("PART_ContentHost", editor);
            inner.ScrollToTop(); viewer.ScrollToTop(); window.UpdateLayout();
            check(inner.ScrollableHeight > 0 && viewer.ScrollableHeight > 0, "The WPF fixture has independent editor and outer-page scroll regions");
            behavior.Wheel(editor, -120, 3, true);
            behavior.Wheel(editor, 120, 3, true);
            check(behavior.ActiveCount == 0 && inner.VerticalOffset == 0,
                "Reversing before the first frame cancels queued motion even at the top boundary");
            behavior.Wheel(editor, -120, 3, true);
            behavior.Advance(.016); window.UpdateLayout();
            check(inner.VerticalOffset > 0 && inner.VerticalOffset < 72 && viewer.VerticalOffset == 0 && behavior.ActiveCount == 1,
                "The real editor scrolls smoothly first without moving the outer page");
            for (int i = 0; i < 60; i++) { behavior.Advance(.016); window.UpdateLayout(); }
            check(Math.Abs(inner.VerticalOffset - 72) < 1 && behavior.ActiveCount == 0, "Native ScrollChanged events preserve animation until the wheel destination is reached");
            inner.ScrollToEnd(); window.UpdateLayout();
            behavior.Wheel(editor, -120, 3, true); behavior.Advance(.016); window.UpdateLayout();
            check(viewer.VerticalOffset > 0 && inner.VerticalOffset == inner.ScrollableHeight,
                "At the editor's bottom edge, downward wheel input passes to the outer settings page");
            behavior.Stop();
            viewer.ScrollToTop(); window.UpdateLayout();
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            viewer.RaiseEvent(wheel);
            check(wheel.Handled, "The actual settings preview-wheel route consumes native jumps");
            behavior.Stop();
            behavior.Wheel(viewer, -120, 3, false); window.UpdateLayout();
            check(behavior.ActiveCount == 0 && viewer.VerticalOffset > 0, "Reduced animation mode uses immediate native scrolling without a rendering callback");
            behavior.Wheel(viewer, -120, 3, true);
            root.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            check(behavior.ActiveCount == 0, "Keyboard navigation cancels pending wheel motion instead of fighting it");
            behavior.Wheel(viewer, -120, 3, true);
            viewer.ScrollToEnd(); window.UpdateLayout();
            check(behavior.ActiveCount == 0, "External scrolling cancels an old animated destination");
            viewer.ScrollToTop(); window.UpdateLayout(); behavior.Wheel(viewer, -120, 3, true);
            root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            check(behavior.ActiveCount == 0, "Closing or unloading settings detaches all active rendering callbacks");
        }
        finally { behavior.Stop(); window.Close(); }
    }
}
