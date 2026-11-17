using System.Windows;
using Point = System.Windows.Point;

namespace SecretaryOverlay;

internal sealed record PoseLandmarks(int Width, int Height, double HeadTop, double ShoeBottom);
internal sealed record BodyLandmarks(Point LeftEye, Point RightEye, Point LeftShoulder, Point RightShoulder, Point Waist, Point Pelvis)
{
    public double EyeSpan => (RightEye - LeftEye).Length;
    public double TorsoLength => (Waist - new Point((LeftShoulder.X + RightShoulder.X) / 2, (LeftShoulder.Y + RightShoulder.Y) / 2)).Length;
}

internal static class PoseRegistration
{
    // Euclidean distances account for the tilted face and torso. Equal relative weighting
    // keeps the longer torso measurement from overwhelming the face measurement.
    internal static readonly BodyLandmarks IdleBody = new(new(480, 145), new(551, 145), new(367, 278), new(652, 281), new(512, 520), new(512, 625));
    internal static readonly BodyLandmarks DelegateBody = new(new(502, 164), new(576, 186), new(325, 244), new(624, 306), new(431, 539), new(402, 626));
    internal static double DelegateRelativeScale => (IdleBody.EyeSpan / DelegateBody.EyeSpan + IdleBody.TorsoLength / DelegateBody.TorsoLength) / 2;
    // Measured crown and lowest shoe pixels, excluding transparent source padding.
    // Scale the whole image uniformly; preserve each pose's natural horizontal offset.
    internal static readonly IReadOnlyDictionary<string, PoseLandmarks> Landmarks = new Dictionary<string, PoseLandmarks>
    {
        ["idle"] = new(1024, 1536, 7, 1526),
        ["start"] = new(941, 1672, 10, 1658),
        ["thinking"] = new(941, 1672, 11, 1667),
        ["working"] = new(1024, 1535, 13, 1529),
        ["review"] = new(1024, 1535, 12, 1523),
        ["permission"] = new(1024, 1535, 11, 1508),
        ["done"] = new(1024, 1536, 11, 1521),
        ["interrupt"] = new(1024, 1536, 12, 1511),
        ["compact"] = new(1024, 1535, 11, 1522),
        ["ready"] = new(1024, 1535, 8, 1524),
        ["receive"] = new(1024, 1536, 10, 1528),
        ["delegate"] = new(1024, 1536, 11, 1531),
    };

    public static Rect Placement(string file, double width, double height, bool before = false)
    {
        var source = Landmarks[file];
        if (before || file == "idle") return Original(source, width, height, file == "delegate" ? 26 : 4);
        var reference = Landmarks["idle"];
        var referenceRect = Original(reference, width, height, 4);
        double referenceScale = referenceRect.Width / reference.Width;
        double shoeY = referenceRect.Top + reference.ShoeBottom * referenceScale;
        if (file == "delegate")
        {
            double bodyScale = referenceScale * DelegateRelativeScale;
            double pelvisX = referenceRect.Left + IdleBody.Pelvis.X * referenceScale;
            return new Rect(pelvisX - DelegateBody.Pelvis.X * bodyScale, shoeY - source.ShoeBottom * bodyScale,
                source.Width * bodyScale, source.Height * bodyScale);
        }
        double scale = referenceScale * (reference.ShoeBottom - reference.HeadTop) / (source.ShoeBottom - source.HeadTop);
        return new Rect((width - source.Width * scale) / 2, shoeY - source.ShoeBottom * scale,
            source.Width * scale, source.Height * scale);
    }

    private static Rect Original(PoseLandmarks source, double width, double height, double topMargin)
    {
        double scale = Math.Max(0, Math.Min(width / source.Width, (height - topMargin) / source.Height));
        return new Rect((width - source.Width * scale) / 2, height - source.Height * scale,
            source.Width * scale, source.Height * scale);
    }
}
