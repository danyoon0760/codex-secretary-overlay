using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using TabControl = System.Windows.Controls.TabControl;

namespace SecretaryOverlay;

internal static class SettingsVerification
{
    public static void Check(Action<bool, string> check)
    {
        ModelOption[] catalog = [new("gpt-5.6-luna", "GPT-5.6 Luna", ["low", "medium", "high"], true),
            new("gpt-6-astra", "GPT-6 Astra", ["high", "xhigh"], true), new("text-only", "Text Only", ["low"], false)];
        var current = ModelProfile.Default;
        int changes = 0;
        var selector = new ModelSettings(() => current, value => { current = value; changes++; }, true, () => catalog);
        check(changes == 0 && selector.Model.Items.Count == 2 && selector.Effort.Items.Count == 3,
            "Opening settings leaves the profile unchanged and only lists image-capable models for screen commentary");
        selector.Model.SelectedIndex = 1;
        check(current == new ModelProfile("gpt-6-astra", "high") && changes == 1 && selector.Effort.Items.Count == 2,
            "Changing models selects a supported effort once when the previous effort is unsupported");
        selector.Effort.SelectedIndex = 1;
        check(current.Reasoning == "xhigh" && changes == 2, "The settings effort selector applies the chosen supported value");
        current = ModelProfile.Default; selector.Sync();
        check(changes == 2 && ((ComboBoxItem)selector.Model.SelectedItem).Tag.Equals(current.Model)
            && ((ComboBoxItem)selector.Effort.SelectedItem).Tag.Equals("medium"), "External menu changes refresh open selectors without writing back or running AI");
        current = new("future-model", "high"); selector.Sync();
        check(changes == 2 && ((ComboBoxItem)selector.Model.SelectedItem).IsEnabled == false,
            "An unavailable saved model stays visible without silently replacing the saved choice");
        catalog = [.. catalog, new("future-model", "Future Model", ["high"], true)]; selector.Sync(true);
        check(((ComboBoxItem)selector.Model.SelectedItem).IsEnabled && changes == 2, "Refreshing local metadata makes a newly available model selectable");
        var summary = new ModelSettings(() => ModelProfile.Default, _ => { }, false, () => catalog);
        check(summary.Model.Items.Count == 4, "Completion summary settings also allow text-only models");
        int styleWrites = 0;
        var completion = CompletionSettings.Create(CompletionStyle.B, _ => styleWrites++);
        CompletionSettings.Sync(completion, CompletionStyle.D);
        check(styleWrites == 0 && completion.Children.OfType<System.Windows.Controls.ComboBox>().Single().SelectedIndex == 1,
            "Completion style synchronization never triggers a second summary request");
        string layoutPath = Path.Combine(AppStorage.DataDirectory, "layout.json");
        string? oldLayout = File.Exists(layoutPath) ? File.ReadAllText(layoutPath) : null;
        string? oldPersonalization = File.Exists(PersonalizationStorage.FilePath) ? File.ReadAllText(PersonalizationStorage.FilePath) : null;
        var pet = new PetWindow(true) { Left = 100, Top = 100 };
        try
        {
            var surface = pet.BuildSettingsContent();
            var tabs = surface.Children.OfType<TabControl>().Single();
            check(tabs.Items.Cast<TabItem>().Select(t => (string)t.Header).SequenceEqual(new[] { "말하기", "작업 알림", "캐릭터", "동작 미리보기", "사용자 맞춤 설정" }),
                "The actual settings view includes the dedicated personalization tab");
            var panels = tabs.Items.Cast<TabItem>().Select(t => (StackPanel)((ScrollViewer)t.Content).Content).ToArray();
            check(panels.SelectMany(p => p.Children.OfType<ModelSettings>()).Count() == 3,
                "The actual settings view exposes independent automatic, manual, and completion model selectors");
            var character = panels[2];
            check(character.Children.OfType<Slider>().Single().Maximum == 950
                && character.Children.OfType<System.Windows.Controls.CheckBox>().Any(c => (string)c.Content == "마우스 클릭 통과시키기"),
                "Character settings expose the complete size range and click-through control");
            check(panels[0].Children.OfType<System.Windows.Controls.Button>().Any(b => (string)b.Content == "지금 말하기"),
                "Manual commentary has an explicit settings action separate from model selection");
            var interval = panels[0].Children.OfType<NumberSetting>().Single();
            interval.Input.Value = 35;
            check(AppStorage.LoadLayout()?.CommentaryIntervalMinutes == 35,
                "The real automatic-comment interval control saves its selected minutes");
            var appearance = panels[1].Children.OfType<BubbleAppearanceSettings>().Single();
            appearance.BubbleWidth.Input.Value = 520;
            appearance.TextSize.Input.Value = 20;
            appearance.HeadDistance.Input.Value = 48;
            check(AppStorage.LoadLayout()?.BubbleAppearance == new BubbleAppearance(520, 20, 48)
                && AppStorage.LoadLayout()?.CommentaryIntervalMinutes == 35,
                "Actual bubble appearance controls persist independently from the automatic interval");
            var auto = panels[0].Children.OfType<System.Windows.Controls.CheckBox>().First();
            auto.IsChecked = false;
            check(!pet.FeatureMenus()[0].Checked && AppStorage.LoadLayout()?.AutoCommentary == false,
                "Turning automatic commentary off in settings updates the native-menu state and saved layout");
            pet.FeatureMenus()[0].Children![0].Action!();
            check(auto.IsChecked == true && AppStorage.LoadLayout()?.AutoCommentary == true,
                "Toggling automatic commentary in the menu immediately updates the open settings checkbox");
            var manual = panels[0].Children.OfType<ModelSettings>().Last();
            manual.Effort.SelectedIndex = manual.Effort.Items.Count - 1;
            var profile = AppStorage.LoadLayout()!;
            check(profile.ManualModel?.Reasoning == (string)((ComboBoxItem)manual.Effort.SelectedItem).Tag
                && profile.AutomaticModel == ModelProfile.Default && profile.CompletionModel == ModelProfile.Default,
                "The real settings callback persists only the selected feature profile");
            var styleCombo = panels[1].Children.OfType<StackPanel>().Single(p => p.Tag is System.Windows.Controls.ComboBox).Tag as System.Windows.Controls.ComboBox;
            styleCombo!.SelectedIndex = 1;
            check(pet.FeatureMenus()[4].Checked && AppStorage.LoadLayout()?.CompletionStyle == CompletionStyle.D,
                "The settings completion choice updates both menu checkmarks and saved style");
            pet.FeatureMenus()[3].Action!();
            check(styleCombo.SelectedIndex == 0, "Menu completion-style changes immediately synchronize the settings selector");
            var labelToggle = character.Children.OfType<System.Windows.Controls.CheckBox>().ElementAt(1);
            labelToggle.IsChecked = false;
            check(AppStorage.LoadLayout()?.ShowLabels == false, "The actual label toggle persists its value");
            var personal = panels[4].Children.OfType<PersonalizationSettings>().Single();
            var saveBar = surface.Children.OfType<Border>().Single(border => border.Child is DockPanel row && row.Children.Contains(personal.Save));
            check(saveBar.Visibility == Visibility.Collapsed && !personal.Save.IsEnabled,
                "The fixed personalization save area stays out of other tabs and starts with no pending changes");
            tabs.SelectedIndex = 4;
            personal.PetName.Text = "테스트 비서";
            check(saveBar.Visibility == Visibility.Visible && personal.Save.Parent == saveBar.Child
                && personal.Save.IsEnabled && personal.SaveHint.Text == "아직 적용 안 됨",
                "Editing shows an actionable save button and unapplied state below the tab's scrolling content");
            pet.FeatureMenus()[0].Children![0].Action!();
            check(personal.PetName.Text == "테스트 비서" && !pet.Title.Contains("테스트"),
                "Refreshing other settings preserves a custom draft without applying its name");
            personal.Save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(PersonalizationStorage.Load().Name == "테스트 비서" && pet.Title.StartsWith("테스트 비서")
                && panels[0].Children.OfType<ModelSettings>().Last().Effort.SelectedItem is ComboBoxItem item
                && AppStorage.LoadLayout()?.ManualModel?.Reasoning == (string)item.Tag
                && !personal.Save.IsEnabled && personal.SaveHint.Text == "저장했어요",
                "The real personalization save updates the pet name without altering feature model choices");
        }
        finally
        {
            pet.Close();
            if (oldLayout is null) { if (File.Exists(layoutPath)) File.Delete(layoutPath); }
            else File.WriteAllText(layoutPath, oldLayout);
            if (oldPersonalization is null) { if (File.Exists(PersonalizationStorage.FilePath)) File.Delete(PersonalizationStorage.FilePath); }
            else File.WriteAllText(PersonalizationStorage.FilePath, oldPersonalization);
        }
        var saved = new Layout(10, 20, 560, ShowLabels: false);
        check(System.Text.Json.JsonSerializer.Deserialize<Layout>(System.Text.Json.JsonSerializer.Serialize(saved)) == saved,
            "Status-label visibility persists with the other character settings");
    }

    public static int Render()
    {
        Directory.CreateDirectory(AppStorage.DataDirectory);
        var pet = new PetWindow(true);
        try
        {
            var content = pet.BuildSettingsContent();
            var tabs = content.Children.OfType<TabControl>().Single();
            var surface = new Border { Width = 560, Height = 780, Background = System.Windows.Media.Brushes.White, Child = content };
            System.Windows.Documents.TextElement.SetFontSize(surface, 13);
            System.Windows.Documents.TextElement.SetFontFamily(surface, System.Windows.SystemFonts.MessageFontFamily);
            for (int i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i;
                surface.Measure(new System.Windows.Size(560, 780)); surface.Arrange(new Rect(0, 0, 560, 780)); surface.UpdateLayout();
                var bitmap = new RenderTargetBitmap(560, 780, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(AppStorage.DataDirectory, $"settings-tab-{i + 1}.png")); encoder.Save(file);
            }
            ((ScrollViewer)((TabItem)tabs.Items[4]).Content).ScrollToEnd(); surface.UpdateLayout();
            var lower = new RenderTargetBitmap(560, 780, 96, 96, PixelFormats.Pbgra32); lower.Render(surface);
            var lowerEncoder = new PngBitmapEncoder(); lowerEncoder.Frames.Add(BitmapFrame.Create(lower));
            using var lowerFile = File.Create(Path.Combine(AppStorage.DataDirectory, "settings-personalization-bottom.png")); lowerEncoder.Save(lowerFile);
        }
        finally { pet.Close(); }
        return 0;
    }
}
