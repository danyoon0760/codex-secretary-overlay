using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SecretaryOverlay;

internal static class BubblePositionPreview
{
    public static int Render()
    {
        var canvas = new Canvas { Width = 1000, Height = 420, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(228, 229, 232)) };
        var examples = new[] {
            ("짧은 내용", "말풍선의 폭과 위치를 수정했어요."),
            ("긴 내용", "말풍선의 아래쪽과 꼬리 위치를 기준으로 고정했어요. 내용이 길어지면 위쪽으로 늘어나고, 화면 위를 넘으면 말풍선 전체가 내려와요. 본문은 스크롤 없이 표시합니다.")
        };
        for (int i = 0; i < examples.Length; i++)
        {
            double x = 30 + 500 * i;
            var title = PetTypography.Text(examples[i].Item1, 15);
            Canvas.SetLeft(title, x); Canvas.SetTop(title, 20); canvas.Children.Add(title);
            var card = new BubbleCard(() => { }); card.Update("비서", examples[i].Item2, "표시 조정 완료"); card.PointTail(true);
            card.Surface.Width = SpeechBubble.PreferredWidth;
            card.Surface.Measure(new System.Windows.Size(SpeechBubble.PreferredWidth, double.PositiveInfinity));
            Canvas.SetLeft(card.Surface, x); Canvas.SetTop(card.Surface, 365 - card.Surface.DesiredSize.Height); canvas.Children.Add(card.Surface);
        }
        canvas.Measure(new System.Windows.Size(1000, 420)); canvas.Arrange(new Rect(0, 0, 1000, 420)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1000, 420, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(AppStorage.DataDirectory);
        using var file = File.Create(Path.Combine(AppStorage.DataDirectory, "bubble-position-preview.png")); encoder.Save(file);
        return 0;
    }
}
