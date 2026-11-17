using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TabControl = System.Windows.Controls.TabControl;

namespace SecretaryOverlay;

internal static class SettingsNavigationVerification
{
    private sealed class Fixture : IDisposable
    {
        public TabControl Tabs { get; } = new();
        public ScrollViewer[] Pages { get; } = new ScrollViewer[3];
        public ScrollViewer Inner { get; } = new() { Height = 90, Content = new Border { Height = 900 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        public Window Window { get; }
        public SmoothScrolling Scrolling { get; }
        private readonly SettingsNavigation.Binding binding;

        public Fixture(SettingsNavigation memory, double contentHeight = 1_800)
        {
            for (int i = 0; i < Pages.Length; i++)
            {
                var content = new StackPanel();
                if (i == 0) content.Children.Add(Inner);
                content.Children.Add(new Border { Height = contentHeight });
                Pages[i] = new ScrollViewer { Content = content, CanContentScroll = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                Tabs.Items.Add(new TabItem { Header = "Tab " + i, Content = Pages[i] });
            }
            var root = new Border { Child = Tabs };
            Scrolling = SmoothScrolling.Attach(root);
            binding = memory.Attach(Tabs, Scrolling.Stop);
            Window = new Window { Width = 500, Height = 350, Content = root, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            Window.Closing += (_, _) => binding.Capture();
            Window.Closed += (_, _) => binding.Dispose();
            Window.Show(); Settle();
        }

        public void Select(int index) { Tabs.SelectedIndex = index; Settle(); }
        public void Scroll(double offset) { Pages[Tabs.SelectedIndex].ScrollToVerticalOffset(offset); Settle(); }
        public void Settle()
        {
            Window.UpdateLayout();
            var frame = new DispatcherFrame();
            Window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Window.UpdateLayout();
        }
        public void Dispose() { Window.Close(); binding.Dispose(); Scrolling.Stop(); }
    }

    public static void Check(Action<bool, string> check)
    {
        var memory = new SettingsNavigation();
        double secondOffset;
        using (var first = new Fixture(memory))
        {
            check(first.Tabs.SelectedIndex == 0 && first.Pages[0].VerticalOffset == 0,
                "A fresh settings session starts on its first tab at the top");
            first.Scroll(360);
            first.Inner.ScrollToVerticalOffset(500); first.Settle();
            first.Select(1); first.Scroll(520);
            first.Select(0);
            check(Math.Abs(first.Pages[0].VerticalOffset - 360) < 1,
                "Switching tabs restores their independent outer offsets without confusing inner editor scrolling");
            first.Select(1);
            check(Math.Abs(first.Pages[1].VerticalOffset - 520) < 1,
                "Unloading a tab cannot replace its remembered offset with zero");
            first.Scrolling.Wheel(first.Pages[1], -120, 3, true);
            first.Scrolling.Advance(.016); first.Window.UpdateLayout();
            secondOffset = first.Pages[1].VerticalOffset;
        }
        using (var reopened = new Fixture(memory))
        {
            check(reopened.Tabs.SelectedIndex == 1 && Math.Abs(reopened.Pages[1].VerticalOffset - secondOffset) < 1,
                "Closing and rebuilding settings restores the last tab and its actual closing scroll position");
            check(reopened.Scrolling.ActiveCount == 0,
                "Initial position restoration does not start or fight a smooth-scroll animation");
            reopened.Select(0);
            check(Math.Abs(reopened.Pages[0].VerticalOffset - 360) < 1,
                "Other tabs retain their own scroll positions after the settings window is rebuilt");
            reopened.Select(1);
        }
        using (var resized = new Fixture(memory, 100))
            check(resized.Tabs.SelectedIndex == 1 && resized.Pages[1].VerticalOffset == 0,
                "A remembered offset safely clamps when the rebuilt page no longer needs scrolling");
        using (var separate = new Fixture(new SettingsNavigation()))
            check(separate.Tabs.SelectedIndex == 0 && separate.Pages[0].VerticalOffset == 0,
                "A separate pet or preview window cannot inherit another instance's settings navigation");
    }
}
