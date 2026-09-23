using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Path = System.Windows.Shapes.Path;

namespace SecretaryOverlay;

// All cards share a passive window, so capture exclusion continues to cover every bubble.
internal sealed partial class SpeechBubble : Window
{
    internal const double PreferredWidth = 440;
    private const int MaximumTaskCards = BubbleCapacity.MaximumVisible;
    private bool HasAmbientSlot => !displayTasks || tasks.Count < MaximumTaskCards;
    private int OverflowCount => displayTasks ? Math.Max(0, tasks.Count - MaximumTaskCards) : 0;
    private readonly StackPanel stack = new() { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
    private readonly StackPanel layout = new() { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
    private readonly Grid frame = new();
    internal OverflowBubble Overflow { get; }
    private bool overflowMenuOpen;
    private readonly Dictionary<string, BubbleCard> cards = new();
    private IReadOnlyList<TaskProgress> tasks = Array.Empty<TaskProgress>();
    private bool displayTasks = true;
    private string ambient = "";
    private string ambientDetail = "";
    private long ambientVersion;
    private long ambientShownVersion;
    private long ambientIdentity;
    private bool ambientStreaming;
    private readonly BubbleAutoClosePolicy autoClose = new();
    private readonly Dictionary<string, string> finishedPresentations = new();
    private readonly DispatcherTimer autoCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Func<long> now;
    private bool suppressShow;
    private string petName = PetPersonalization.DefaultName;
    public string PetName
    {
        get => petName;
        set
        {
            petName = PetPersonalization.Normalize(PetPersonalization.Default with { Name = value }).Name;
            Title = petName + " 말풍선";
            foreach (var card in cards.Values) card.SetPetName(petName);
            Reposition();
        }
    }
    private bool positioning;
    private double anchorOffset;
    private BubbleAppearance appearance = BubbleAppearance.Default;
    private bool? onLeft;
    private Rect lastWorkArea = Rect.Empty;

    public BubbleAppearance Appearance
    {
        get => appearance;
        set
        {
            appearance = BubbleAppearance.Normalize(value);
            foreach (var card in cards.Values) card.SetFontSize(appearance.FontSize);
            Reposition();
        }
    }
    public event Action<string>? TaskDismissed;
    public event Action<string>? ChatRequested;
    public event Action? OverflowRequested;
    public event Action<IReadOnlyList<string>>? TasksExpired;
    public bool HasTaskCards => displayTasks && tasks.Count > 0;
    internal IReadOnlyList<BubbleCard> VisibleCards => stack.Children.Cast<FrameworkElement>()
        .Select(surface => cards.Values.Single(c => ReferenceEquals(c.Surface, surface))).ToArray();

    public SpeechBubble(Window owner, Func<long>? clock = null, Func<bool>? animateReflow = null)
    {
        now = clock ?? (() => Environment.TickCount64);
        allowReflow = animateReflow ?? (() => SystemParameters.ClientAreaAnimation);
        Owner = owner;
        Title = "비서 펫 말풍선";
        Width = PreferredWidth;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Overflow = new OverflowBubble(RequestOverflow);
        layout.Children.Add(stack);
        layout.Children.Add(Overflow.Surface);
        frame.Children.Add(layout);
        Content = frame;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeDesktop.MakePassive(handle);
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled) =>
            { if (m == 0x21) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
        };
        owner.LocationChanged += (_, _) => Reposition();
        owner.SizeChanged += (_, _) => Reposition();
        SizeChanged += (_, _) => Reposition();
        autoCloseTimer.Tick += (_, _) => ExpireOlderBubbles();
        // Pause the stack together so another card cannot disappear and move the text being read.
        MouseEnter += (_, _) => autoClose.SetPaused(true, now());
        MouseLeave += (_, _) => autoClose.SetPaused(overflowMenuOpen, now());
        IsVisibleChanged += (_, _) => { if (!IsVisible) { autoClose.SetPaused(false, now()); CancelReflow(); } };
        Closed += (_, _) => { autoCloseTimer.Stop(); CancelReflow(); };
    }

    private void RequestOverflow()
    {
        if (Overflow.Count == 0 || !displayTasks || overflowMenuOpen) return;
        // A native menu runs its own message loop; keep reading time paused after MouseLeave.
        overflowMenuOpen = true;
        autoClose.SetPaused(true, now());
        try { OverflowRequested?.Invoke(); }
        finally { overflowMenuOpen = false; autoClose.SetPaused(IsVisible && IsMouseOver, now()); }
    }

    private void UpdateOverflow() => Overflow.Update(OverflowCount, onLeft ?? true, appearance.FontSize,
        displayTasks ? tasks.Skip(MaximumTaskCards).Count(task => task.State == "PermissionRequest") : 0);

    internal static Grid CreateSurface(TextBlock text, Action close, out Path tail)
    {
        var card = new BubbleCard(close, text);
        tail = card.Tail;
        return card.Surface;
    }

    public void SetTasks(IReadOnlyList<TaskProgress> available, bool display = true)
    {
        // Keep overflow under observation: active work waits for a slot; completed work still expires.
        tasks = available.Where(task => !task.Dismissed).ToArray();
        displayTasks = display;
        Render();
    }

}
