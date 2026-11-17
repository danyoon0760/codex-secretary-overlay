using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SecretaryOverlay;

internal sealed class PoseAssets
{
    private const int BytesPerPixel = 4;
    private const byte HitTestAlphaThreshold = 25;
    private readonly Dictionary<string, (BitmapSource Image, byte[] Pixels)> assets = new();

    public PoseAssets(string root)
    {
        foreach (var file in StateEngine.Poses.Values.Select(p => p.File).Distinct())
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.Combine(root, "assets", file + ".png"));
            bitmap.EndInit();
            bitmap.Freeze();

            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * BytesPerPixel];
            converted.CopyPixels(pixels, converted.PixelWidth * BytesPerPixel, 0);
            assets[file] = (converted, pixels);
        }
    }

    public BitmapSource this[string file] => assets[file].Image;

    public bool IsOpaque(string file, int x, int y)
    {
        var (image, pixels) = assets[file];
        return x >= 0 && y >= 0 && x < image.PixelWidth && y < image.PixelHeight &&
            pixels[(y * image.PixelWidth + x) * BytesPerPixel + 3] > HitTestAlphaThreshold;
    }
}
