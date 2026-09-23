using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TabControl = System.Windows.Controls.TabControl;
using SystemFonts = System.Windows.SystemFonts;
using SystemColors = System.Windows.SystemColors;

namespace SecretaryOverlay;

public sealed partial class PetWindow
{
    private readonly List<Action> settingsRefresh = new();
    private bool syncingSettings;
    private PersonalizationSettings? personalizationEditor;
    private readonly SettingsNavigation settingsNavigation = new();
    private SettingsNavigation.Binding? settingsNavigationBinding;

    private void SyncControls()
    {
        if (syncingSettings) return;
        syncingSettings = true;
        try { foreach (var refresh in settingsRefresh) refresh(); }
        finally { syncingSettings = false; }
    }

    private void ShowControls()
    {
        if (control is not null) { SyncControls(); control.Activate(); return; }
        control = new Window
        {
            Title = personalization.Name + " 설정", Width = 560, Height = Math.Min(780, SystemParameters.WorkArea.Height - 40),
            MinWidth = 480, MinHeight = 420, Content = BuildSettingsContent(),
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true,
            FontFamily = SystemFonts.MessageFontFamily, FontSize = 13
        };
        control.Activated += (_, _) => SyncControls();
        control.Closing += (_, _) => { settingsNavigationBinding?.Capture(); personalizationEditor?.FlushDraft(); };
        control.Closed += (_, _) =>
        {
            settingsNavigationBinding?.Dispose(); settingsNavigationBinding = null;
            control = null; completionSettingsPanel = null; personalizationEditor = null; settingsRefresh.Clear();
        };
        control.Show();
    }

    internal DockPanel BuildSettingsContent()
    {
        settingsRefresh.Clear();
        settingsNavigationBinding?.Dispose();
        var surface = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        var smoothScrolling = SmoothScrolling.Attach(surface);
        var heading = new StackPanel();
        var settingsTitle = new TextBlock { Text = personalization.Name + " 설정", FontSize = 23, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        heading.Children.Add(settingsTitle);
        settingsRefresh.Add(() => settingsTitle.Text = personalization.Name + " 설정");
        heading.Children.Add(SettingsDescription("기능 설정은 바로 적용돼요. 사용자 맞춤 설정은 ‘저장’을 눌러 적용하세요."));
        DockPanel.SetDock(heading, Dock.Top); surface.Children.Add(heading);
        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var quit = SettingsButton(personalization.Name + " 종료", Close);
        quit.MaxWidth = 180;
        quit.Content = new TextBlock { Text = personalization.Name + " 종료", TextTrimming = TextTrimming.CharacterEllipsis };
        settingsRefresh.Add(() => { ((TextBlock)quit.Content).Text = personalization.Name + " 종료"; quit.ToolTip = personalization.Name + " 종료"; });
        DockPanel.SetDock(quit, Dock.Right); bottom.Children.Add(quit);
        bottom.Children.Add(SettingsDescription("펫을 종료하면 캐릭터와 말풍선이 닫혀요. Codex 작업은 계속돼요."));
        DockPanel.SetDock(bottom, Dock.Bottom); surface.Children.Add(bottom);
        var tabs = new TabControl { Margin = new Thickness(0, 10, 0, 0) };
        surface.Children.Add(tabs);
        BuildSpeakingSettings(SettingsTab(tabs, "말하기"));
        BuildProgressSettings(SettingsTab(tabs, "작업 알림"));
        BuildCharacterSettings(SettingsTab(tabs, "캐릭터"));
        BuildPreviewSettings(SettingsTab(tabs, "동작 미리보기"));
        BuildPersonalizationSettings(SettingsTab(tabs, "사용자 맞춤 설정"));
        var saveBar = personalizationEditor!.CreateStickySaveBar();
        DockPanel.SetDock(saveBar, Dock.Bottom);
        surface.Children.Insert(surface.Children.IndexOf(tabs), saveBar);
        void UpdateSaveBar() => saveBar.Visibility = tabs.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, tabs)) UpdateSaveBar(); };
        settingsNavigationBinding = settingsNavigation.Attach(tabs, smoothScrolling.Stop);
        UpdateSaveBar();
        SyncControls();
        return surface;
    }

    private static TextBlock SettingsHeading(string text) => new()
        { Text = text, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 6), TextWrapping = TextWrapping.Wrap };
    private static TextBlock SettingsDescription(string text) => new()
        { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12), LineHeight = 20, Foreground = SystemColors.ControlTextBrush };
    private static Button SettingsButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 8, 8, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        button.Click += (_, _) => action(); return button;
    }
    private WrapPanel CreatePreviewButtons()
    {
        var wrap = new WrapPanel();
        foreach (var (name, pose) in StateEngine.Poses.Where(x => x.Key != "Idle"))
        {
            var button = SettingsButton(pose.Label, () => Receive(new PetEvent(name, Preview: true)));
            button.Width = 190; wrap.Children.Add(button);
        }
        return wrap;
    }
    private void SetMotionEnabled(bool enabled) { motion = enabled; SetBreathing(); SaveLayout(); }
    private void SetClickThrough(bool enabled) { clickThrough = enabled; ApplyClickThrough(); SyncControls(); }

    private void SetBubbleAppearance(BubbleAppearance value)
    {
        bubbleAppearance = BubbleAppearance.Normalize(value);
        if (bubble is not null) bubble.Appearance = bubbleAppearance;
        SaveLayout();
    }

    private void SetCommentaryInterval(int minutes)
    {
        commentary.SetIntervalMinutes(minutes, Now);
        SaveLayout();
    }

    private bool SavePersonalization(PetPersonalization value)
    {
        value = PetPersonalization.Normalize(value);
        if (!PersonalizationStorage.Save(value)) return false;
        personalization = value;
        RefreshPetName();
        SyncControls();
        return true;
    }

    private void RefreshPetName()
    {
        Title = personalization.Name + " · Codex 작업 알림";
        tray.Text = personalization.Name + " · 오른쪽 클릭으로 제어";
        if (control is not null) control.Title = personalization.Name + " 설정";
        if (bubble is not null) bubble.PetName = personalization.Name;
    }
}
