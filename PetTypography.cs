using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;

namespace SecretaryOverlay;

internal static class PetTypography
{
    private static readonly string FontDirectory = new Uri(Path.Combine(AppStorage.Root, "assets", "fonts") + Path.DirectorySeparatorChar).AbsoluteUri;
    public static readonly FontFamily Korean = new(FontDirectory + "#Noto Serif KR");
    public static readonly FontFamily English = new(FontDirectory + "#Source Serif 4");
    public static readonly FontFamily Mixed = CreateMixed();

    private static FontFamily CreateMixed()
    {
        var family = new FontFamily();
        family.FamilyMaps.Add(new FontFamilyMap
        {
            Unicode = "0000-024F,1E00-1EFF,2000-206F",
            Target = FontDirectory + "#Source Serif 4"
        });
        family.FamilyMaps.Add(new FontFamilyMap { Target = FontDirectory + "#Noto Serif KR" });
        return family;
    }

    public static TextBlock Text(string value, double size = 15) => new()
    {
        Text = value, FontFamily = System.Windows.SystemFonts.MessageFontFamily, FontSize = size,
        Foreground = Brushes.Black, FontWeight = FontWeights.Bold
    };
}
