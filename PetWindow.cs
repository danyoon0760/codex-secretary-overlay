using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace SecretaryOverlay;

public sealed partial class PetWindow : Window
{
    private const double MinimumHeight = 280;
    private const double MaximumHeight = 950;
    private const double AspectRatio = .625;
    private const double BadgeHeight = 52;
    private readonly StateEngine engine = new();
    private readonly PoseAssets assets = new(AppStorage.Root);
    private readonly Grid root = new();
    private readonly Canvas layers = new();
    private readonly TextBlock label = new()
    {
        FontSize = 12,
        Foreground = Brushes.White,
        TextAlignment = TextAlignment.Center
    };
    private readonly TextBlock indicator = new()
    {
        FontSize = 10,
        Foreground = new SolidColorBrush(Color.FromRgb(185, 192, 207)),
        TextAlignment = TextAlignment.Center
    };
    private readonly CancellationTokenSource stop = new();
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly System.Windows.Forms.NotifyIcon tray = new();
    private readonly ScaleTransform breath = new(1, 1);
    private long liveCount;
    private string lastHook = "";
    private DateTimeOffset? lastHookAt;
    private bool motion = true;
    private bool clickThrough;
    private bool showLabels = true;
    private ModelProfile automaticModel = ModelProfile.Default;
    private ModelProfile manualModel = ModelProfile.Default;
    private ModelProfile completionModel = ModelProfile.Default;
    private PetPersonalization personalization = PetPersonalization.Default;
    private BubbleAppearance bubbleAppearance = BubbleAppearance.Default;
    private readonly bool settingsPreviewOnly;
    private Image? front;
    private string activeFile = "idle";
    private DateTime lastSave = DateTime.MinValue;
    private Task? listener;
    private Border badge = null!;
    private static long Now => Environment.TickCount64;

    public PetWindow() : this(false) { }

    internal PetWindow(bool settingsPreview)
    {
        settingsPreviewOnly = settingsPreview;
        Title = "비서 펫 · Codex 후크 오버레이";
        Width = 350;
        Height = 560;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;

        BuildVisualTree();
        ConfigureInput();
        if (!settingsPreview) ConfigureTray();
        engine.Changed += SetPose;
        if (!settingsPreview)
        {
            SourceInitialized += (_, _) => InitializeNativeWindow();
            Loaded += (_, _) => StartServices();
            Closing += OnClosing;
        }
    }

}
