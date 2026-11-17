using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace SecretaryOverlay;

internal static class NumberSettingVerification
{
    public static void Check(Action<bool, string> check)
    {
        double value = 20;
        int writes = 0;
        NumberSetting setting = null!;
        setting = new NumberSetting("자동 한마디 간격", "분", 1, 180, () => value, next => { value = next; writes++; setting?.Sync(); });
        var other = new System.Windows.Controls.Button { Content = "다른 곳" };
        var content = new StackPanel(); content.Children.Add(setting); content.Children.Add(other);
        var window = new Window { Width = 400, Height = 180, Content = content, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout();
            bool Key(Key key)
            {
                var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                setting.ValueInput.RaiseEvent(e);
                return e.Handled;
            }
            void LoseFocus() => setting.ValueInput.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, setting.ValueInput, other)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            check(writes == 0 && setting.ValueInput.Text == "20" && setting.Input.Value == 20
                && System.Windows.Automation.AutomationProperties.GetName(setting.ValueInput) == "자동 한마디 간격 직접 입력",
                "A numeric setting opens without applying changes and exposes an accessible direct-entry field alongside its slider");
            setting.ValueInput.Text = "35";
            check(writes == 0 && value == 20 && Key(System.Windows.Input.Key.Enter) && value == 35 && writes == 1 && setting.Input.Value == 35,
                "Typing does not apply a numeric setting until Enter, which updates the same live setter and slider exactly once");
            setting.ValueInput.Text = "51"; setting.Sync();
            value = 40; setting.Sync();
            check(setting.ValueInput.Text == "51" && setting.Input.Value == 40 && writes == 1,
                "Periodic refreshes and external setting changes preserve an in-progress numeric edit without writing it back");
            check(Key(System.Windows.Input.Key.Escape) && value == 40 && writes == 1 && setting.ValueInput.Text == "40",
                "Escape cancels an edit and restores the latest applied value rather than the value from before editing");
            setting.ValueInput.Text = " 48 "; LoseFocus();
            check(value == 48 && writes == 2 && setting.ValueInput.Text == "48" && setting.Input.Value == 48,
                "Moving focus applies a valid integer and normalizes surrounding whitespace");
            setting.ValueInput.Text = "999"; Key(System.Windows.Input.Key.Enter);
            bool upperBoundApplied = value == 180 && setting.ValueInput.Text == "180" && setting.Input.Value == 180;
            setting.ValueInput.Text = "-4"; LoseFocus();
            check(upperBoundApplied && value == 1 && writes == 4 && setting.ValueInput.Text == "1" && setting.Input.Value == 1,
                "Out-of-range integers are consistently clamped to the available minimum and maximum");
            int beforeInvalid = writes;
            foreach (string invalid in new[] { "", "   ", "숫자", "2.5", "999999999999999999999999999999" })
            {
                setting.ValueInput.Text = invalid; Key(System.Windows.Input.Key.Enter);
                if (value != 1 || writes != beforeInvalid || setting.ValueInput.Text != "1")
                    throw new InvalidOperationException("Invalid numeric input changed the setting or was not restored.");
            }
            check(writes == beforeInvalid && ((string)setting.ValueInput.ToolTip).Contains("바꾸지 않았어요"),
                "Blank, non-integer and overflowing numeric input is never applied and restores the current value with an explanation");
            setting.ValueInput.Text = "77";
            setting.Input.Value = 65;
            check(value == 65 && writes == beforeInvalid + 1 && setting.ValueInput.Text == "65",
                "Moving the existing slider supersedes a pending text edit and immediately synchronizes its displayed number");
            LoseFocus(); Key(System.Windows.Input.Key.Enter);
            check(writes == beforeInvalid + 1, "Focus loss and Enter do not reapply an already accepted value");
            foreach (var range in new[] { (300d, 680d, 440d), (12d, 26d, 17d), (0d, 160d, 24d) })
            {
                double current = range.Item3;
                var appearance = new NumberSetting("모양", "", range.Item1, range.Item2, () => current, next => current = next);
                appearance.ValueInput.Text = "0";
                appearance.ValueInput.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, appearance.ValueInput, other)
                    { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                if (current != range.Item1 || appearance.Input.Value != range.Item1)
                    throw new InvalidOperationException("An appearance numeric field did not respect its own bounds.");
            }
            check(true, "Width, font size and head distance each enforce their own range, including zero distance");
        }
        finally { window.Close(); }
    }
}
