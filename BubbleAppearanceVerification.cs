using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace SecretaryOverlay;

internal static class BubbleAppearanceVerification
{
    public static void Check(Action<bool, string> check)
    {
        var old = JsonSerializer.Deserialize<Layout>("{\"Left\":10,\"Top\":20,\"Height\":560}")!;
        check(BubbleAppearance.Normalize(old.BubbleAppearance) == BubbleAppearance.Default && old.CommentaryIntervalMinutes == 20,
            "Older saved layouts retain the original bubble appearance and twenty-minute interval");
        var saved = old with { BubbleAppearance = new(520, 20, 48), CommentaryIntervalMinutes = 35 };
        check(JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(saved)) == saved,
            "Bubble width, typography, distance and automatic interval survive a settings round trip");
        check(BubbleAppearance.Normalize(new(double.NaN, -1, double.PositiveInfinity)) == new BubbleAppearance(440, 12, 24)
            && BubbleAppearance.Normalize(new(9999, 99, -50)) == new BubbleAppearance(680, 26, 0),
            "Invalid display values use safe defaults and supported bounds");
        bool left = BubbleSidePolicy.OnLeft(501, 500, null);
        left = BubbleSidePolicy.OnLeft(499, 500, left);
        check(left && BubbleSidePolicy.OnLeft(450, 500, left), "Crossing the screen midpoint slightly does not flip the bubble");
        left = BubbleSidePolicy.OnLeft(439, 500, left);
        check(!left && !BubbleSidePolicy.OnLeft(550, 500, left) && BubbleSidePolicy.OnLeft(561, 500, left),
            "The bubble switches sides only after crossing the sixty-unit margin in either direction");
        check(!BubbleSidePolicy.OnLeft(499, 500, null), "A new monitor chooses its side independently from the previous monitor");

        var area = SystemParameters.WorkArea;
        var owner = new Window { Width = 350, Height = 560, Left = area.Left + area.Width * .65,
            Top = area.Top + 300, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        SpeechBubble? bubble = null;
        try
        {
            owner.Show(); bubble = new SpeechBubble(owner) { Opacity = 0 };
            bubble.Say("설정을 바꿔도 같은 말풍선에서 바로 확인합니다.");
            var card = bubble.VisibleCards.Single();
            bubble.Appearance = new(520, 21, 24); bubble.UpdateLayout();
            check(ReferenceEquals(card, bubble.VisibleCards.Single()) && card.Body.FontSize == 21
                && card.PetName.FontSize > 12 && card.Body.FontFamily.Equals(PetTypography.Mixed)
                && card.Body.FontWeight == FontWeights.Normal && bubble.Width <= 520,
                "Changing appearance updates the serif body and existing labels");
            double priorLeft = bubble.Left;
            bubble.Appearance = bubble.Appearance with { HeadDistance = 64 }; bubble.UpdateLayout();
            check(Math.Abs(bubble.Left - priorLeft + 40) < 1,
                "Increasing the distance moves a left-side bubble away from the head without changing its content");
            bubble.Appearance = BubbleAppearance.Default; bubble.UpdateLayout();
            check(card.Body.FontSize == 17 && card.PetName.FontSize == 12 && card.Body.Text.StartsWith("설정을"),
                "Restoring default appearance keeps the current bubble message");
        }
        finally { bubble?.Close(); owner.Close(); }
    }
}
