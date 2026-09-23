namespace SecretaryOverlay;

public sealed record BubbleAppearance(double Width = 440, double FontSize = 17, double HeadDistance = 24)
{
    public static readonly BubbleAppearance Default = new();
    public static BubbleAppearance Normalize(BubbleAppearance? value)
    {
        value ??= Default;
        return new(Bounded(value.Width, 300, 680, Default.Width),
            Bounded(value.FontSize, 12, 26, Default.FontSize), Bounded(value.HeadDistance, 0, 160, Default.HeadDistance));
    }

    private static double Bounded(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

internal static class BubbleSidePolicy
{
    // Keep the current side while the character moves around the screen midpoint.
    internal const double SwitchMargin = 60;
    public static bool OnLeft(double petCenter, double screenCenter, bool? previous) => previous switch
    {
        true => petCenter >= screenCenter - SwitchMargin,
        false => petCenter > screenCenter + SwitchMargin,
        null => petCenter > screenCenter
    };
}
