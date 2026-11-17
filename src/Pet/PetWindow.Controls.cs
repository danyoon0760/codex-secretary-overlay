using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace SecretaryOverlay;

public sealed partial class PetWindow
{
    private Window? control;
    private bool menuOpen;
    private StackPanel? completionSettingsPanel;

    private void ConfigureTray()
    {
        tray.Icon = System.Drawing.SystemIcons.Information;
        tray.Text = "비서 펫 · 오른쪽 클릭으로 제어";
        tray.Visible = true;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Opening += (_, args) =>
        {
            foreach (var old in menu.Items.Cast<System.Windows.Forms.ToolStripItem>().ToArray()) old.Dispose();
            menu.Items.Clear();
            var entries = FeatureMenus().Concat(new[] { MenuEntry.Separator,
                new MenuEntry("설정", ShowControls),
                new MenuEntry("캐릭터 다시 표시 · 클릭 통과 해제", RestoreInteraction), new MenuEntry(personalization.Name + " 종료", Close) });
            foreach (var entry in entries) menu.Items.Add(TrayEntry(entry));
            args.Cancel = false; // The first opening starts from an empty, dynamically populated menu.
        };
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowControls);
    }

    private void ShowMenu()
    {
        if (menuOpen || hwnd == IntPtr.Zero) return;
        menuOpen = true;
        Action? selected;
        try
        {
            using var menu = new NativePopupMenu();
            menu.AddEntries(FeatureMenus());
            menu.AddSeparator();
            menu.Add("설정", ShowControls);
            menu.Add("캐릭터 키우기", () => ChangeSize(1.1));
            menu.Add("캐릭터 줄이기", () => ChangeSize(1 / 1.1));
            menu.Add(motion ? "숨쉬는 움직임 끄기" : "숨쉬는 움직임 켜기", () => SetMotionEnabled(!motion));
            menu.Add("마우스 클릭 통과시키기", () => SetClickThrough(true));
            menu.Add(personalization.Name + " 종료", Close);
            selected = menu.Select(hwnd);
        }
        finally { menuOpen = false; }
        // Run only after the native menu and its handle have closed.
        if (!stop.IsCancellationRequested) selected?.Invoke();
    }

    private void SetLabelVisibility(bool visible)
    {
        showLabels = visible;
        badge.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        SaveLayout();
    }
}
