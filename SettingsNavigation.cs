using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TabControl = System.Windows.Controls.TabControl;

namespace SecretaryOverlay;

// Lives with one pet window, not on disk. The remembered values never retain closed controls.
internal sealed class SettingsNavigation
{
    private string? selected;
    private readonly Dictionary<string, double> offsets = new();

    public Binding Attach(TabControl tabs, Action stopScrolling) => new(this, tabs, stopScrolling);

    internal sealed class Binding : IDisposable
    {
        private sealed class Page(string key, TabItem tab, ScrollViewer viewer)
        {
            public string Key { get; } = key;
            public TabItem Tab { get; } = tab;
            public ScrollViewer Viewer { get; } = viewer;
            public bool Restoring { get; set; } = true;
            public long Version { get; set; }
        }

        private readonly SettingsNavigation memory;
        private readonly TabControl tabs;
        private readonly Action stopScrolling;
        private readonly Page[] pages;
        private bool disposed;

        public Binding(SettingsNavigation memory, TabControl tabs, Action stopScrolling)
        {
            this.memory = memory; this.tabs = tabs; this.stopScrolling = stopScrolling;
            pages = tabs.Items.OfType<TabItem>().Where(tab => tab.Content is ScrollViewer)
                .Select((tab, index) => new Page(tab.Header as string ?? index.ToString(), tab, (ScrollViewer)tab.Content)).ToArray();
            foreach (var page in pages)
            {
                page.Viewer.ScrollChanged += Scrolled;
                page.Viewer.Loaded += Loaded;
                page.Viewer.Unloaded += Unloaded;
            }
            tabs.SelectionChanged += Selected;
            var initial = pages.FirstOrDefault(page => page.Key == memory.selected) ?? pages.FirstOrDefault();
            if (initial is not null)
            {
                tabs.SelectedItem = initial.Tab;
                memory.selected = initial.Key;
                Restore(initial);
            }
        }

        public void Capture()
        {
            if (disposed) return;
            stopScrolling();
            if (pages.FirstOrDefault(page => page.Tab.IsSelected) is not { } current) return;
            memory.selected = current.Key;
            RememberOffset(current);
        }

        private void Selected(object sender, SelectionChangedEventArgs e)
        {
            if (disposed || !ReferenceEquals(e.OriginalSource, tabs)) return;
            stopScrolling();
            if (pages.FirstOrDefault(page => page.Tab.IsSelected) is not { } current) return;
            memory.selected = current.Key;
            Restore(current);
        }

        private void Loaded(object sender, RoutedEventArgs e)
        {
            if (pages.FirstOrDefault(page => ReferenceEquals(page.Viewer, sender)) is { } page && page.Tab.IsSelected)
                Restore(page);
        }

        private void Unloaded(object sender, RoutedEventArgs e)
        {
            if (pages.FirstOrDefault(page => ReferenceEquals(page.Viewer, sender)) is not { } page) return;
            // TabControl may reset layout/offsets while detaching its previous content. The last
            // real ScrollChanged value is already remembered; never replace it with unload zeroes.
            page.Version++;
            page.Restoring = true;
        }

        private void Scrolled(object sender, ScrollChangedEventArgs e)
        {
            if (disposed || !ReferenceEquals(e.OriginalSource, sender)) return;
            if (pages.FirstOrDefault(page => ReferenceEquals(page.Viewer, sender)) is { } page) RememberOffset(page);
        }

        private void RememberOffset(Page page)
        {
            if (page.Restoring || !page.Tab.IsSelected || !page.Viewer.IsLoaded || !page.Viewer.IsVisible
                || page.Viewer.ViewportHeight <= 0) return;
            memory.offsets[page.Key] = page.Viewer.VerticalOffset;
        }

        private void Restore(Page page)
        {
            if (disposed) return;
            page.Restoring = true;
            long version = ++page.Version;
            if (!page.Viewer.IsLoaded || !page.Tab.IsSelected) return;
            // Restore only after the selected page has a measured viewport. Restoring directly
            // from SelectionChanged would clamp the saved offset against an empty layout.
            page.Viewer.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (disposed || page.Version != version || !page.Tab.IsSelected || !page.Viewer.IsLoaded) return;
                stopScrolling();
                page.Viewer.UpdateLayout();
                page.Viewer.ScrollToVerticalOffset(Math.Clamp(memory.offsets.GetValueOrDefault(page.Key), 0, page.Viewer.ScrollableHeight));
                page.Viewer.UpdateLayout();
                page.Restoring = false;
                RememberOffset(page);
            }));
        }

        public void Dispose()
        {
            if (disposed) return;
            Capture();
            disposed = true;
            tabs.SelectionChanged -= Selected;
            foreach (var page in pages)
            {
                page.Viewer.ScrollChanged -= Scrolled;
                page.Viewer.Loaded -= Loaded;
                page.Viewer.Unloaded -= Unloaded;
            }
        }
    }
}
