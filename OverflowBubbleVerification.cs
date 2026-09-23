using System.Windows;
using System.Windows.Controls;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace SecretaryOverlay;

internal static class OverflowBubbleVerification
{
    public static void Check(Action<bool, string> check)
    {
        int clicks = 0;
        var bubble = new OverflowBubble(() => clicks++);
        check(bubble.Count == 0 && bubble.Text == "" && bubble.Surface.Visibility == Visibility.Collapsed,
            "The overflow indicator is absent when there are no hidden tasks");
        bubble.Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(clicks == 0, "An absent overflow indicator does not dispatch an action");
        bubble.Update(1, true, 17);
        check(bubble.Count == 1 && bubble.Text == "다른 작업 1개" && bubble.Surface.Visibility == Visibility.Visible
            && bubble.Surface.HorizontalAlignment == System.Windows.HorizontalAlignment.Right && bubble.Surface.Margin.Top == 8,
            "A single hidden task has a compact label positioned toward the character with an eight-pixel gap");
        bubble.Update(2, false, 17);
        check(bubble.Text == "다른 작업 2개" && bubble.Surface.HorizontalAlignment == System.Windows.HorizontalAlignment.Left
            && bubble.Button.FontWeight == FontWeights.Bold && bubble.Button.FontSize == 16
            && bubble.Button.FontFamily.Equals(System.Windows.SystemFonts.MessageFontFamily),
            "The indicator follows the character side and uses the B design's larger default bold Windows font");
        bubble.Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(clicks == 1 && (string)bubble.Button.ToolTip == "눌러서 숨겨진 작업 보기"
            && System.Windows.Automation.AutomationProperties.GetName(bubble.Button) == bubble.Text,
            "Clicking the visible indicator delegates once and exposes an accessible label and purpose");
        bubble.Update(123, true, 26);
        check(bubble.Count == 123 && bubble.Text == "다른 작업 123개" && Math.Abs(bubble.Button.FontSize - 26d * 16 / 17) < .001,
            "The hidden-task count and scaled label remain correct above one hundred tasks");
        var content = (Grid)bubble.Button.Content;
        var body = content.Children.OfType<Border>().Single();
        check(body.Child is TextBlock label && label.Text == bubble.Text && label.FontWeight == FontWeights.Bold
            && content.Children.Count == 2 && body.BorderThickness == new Thickness(2),
            "The compact overflow surface contains just one label and a tail, without a pet-name header or close button");
        bubble.Update(-1, false, 17);
        check(bubble.Count == 0 && bubble.Surface.Visibility == Visibility.Collapsed && !bubble.Button.IsEnabled,
            "A cleared or invalid count removes the indicator and disables its action");
    }
}
