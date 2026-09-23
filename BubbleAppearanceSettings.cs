using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace SecretaryOverlay;

internal sealed class BubbleAppearanceSettings : StackPanel
{
    internal NumberSetting BubbleWidth { get; }
    internal NumberSetting TextSize { get; }
    internal NumberSetting HeadDistance { get; }

    public BubbleAppearanceSettings(Func<BubbleAppearance> get, Action<BubbleAppearance> set)
    {
        BubbleWidth = new("말풍선 너비", "", 300, 680, () => get().Width, value => set(get() with { Width = value }));
        TextSize = new("글자 크기", "", 12, 26, () => get().FontSize, value => set(get() with { FontSize = value }));
        HeadDistance = new("머리와의 거리", "", 0, 160, () => get().HeadDistance, value => set(get() with { HeadDistance = value }));
        Children.Add(BubbleWidth); Children.Add(TextSize); Children.Add(HeadDistance);
        var reset = new Button { Content = "말풍선 기본값으로", HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 6, 0, 4) };
        reset.Click += (_, _) => { set(BubbleAppearance.Default); Sync(); };
        Children.Add(reset);
    }

    public void Sync() { BubbleWidth.Sync(); TextSize.Sync(); HeadDistance.Sync(); }
}
