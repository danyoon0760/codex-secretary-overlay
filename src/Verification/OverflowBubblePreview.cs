using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Size = System.Windows.Size;

namespace SecretaryOverlay;

internal static class OverflowBubblePreview
{
    public static int Render()
    {
        var area = SystemParameters.WorkArea;
        var owner = new Window { Width = 280, Height = 400, Left = area.Left + area.Width * .65,
            Top = area.Top + area.Height * .32, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        SpeechBubble? bubble = null;
        try
        {
            owner.Show();
            bubble = new SpeechBubble(owner, animateReflow: () => false) { Opacity = 0, IsHitTestVisible = false };
            var tasks = Enumerable.Range(1, 5).Select(i => new TaskProgress($"00000000-0000-4000-8000-{i:000000000000}")
            {
                Project = "비서", ChatTitle = new[] { "설정 화면", "말풍선 위치", "자동 닫기", "새 작업", "다른 프로젝트" }[i - 1],
                Body = new[] { "설정 화면을 정리하고 있습니다.", "말풍선 위치를 확인하고 있습니다.", "작업 상태에 따른 표시를 확인하고 있습니다.", "새 작업을 시작했습니다.", "다른 프로젝트에서도 작업하고 있습니다." }[i - 1],
                Detail = "작업 중"
            }).ToArray();
            var columns = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Background = Brushes.WhiteSmoke };
            foreach (int count in new[] { 4, 5 })
            {
                bubble.SetTasks(tasks.Take(count).ToArray()); bubble.UpdateLayout();
                var content = (FrameworkElement)bubble.Content;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var panel = new StackPanel { Margin = new Thickness(18) };
                var label = PetTypography.Text($"작업 {count}개 · 화면에는 3개 표시", 15);
                label.Margin = new Thickness(0, 0, 0, 16);
                panel.Children.Add(label);
                panel.Children.Add(new Image { Source = bitmap, Stretch = Stretch.None });
                columns.Children.Add(panel);
            }
            columns.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            columns.Arrange(new Rect(new System.Windows.Point(), columns.DesiredSize)); columns.UpdateLayout();
            var output = new RenderTargetBitmap((int)Math.Ceiling(columns.ActualWidth), (int)Math.Ceiling(columns.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            output.Render(columns);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(output));
            Directory.CreateDirectory(AppStorage.DataDirectory);
            using var file = File.Create(Path.Combine(AppStorage.DataDirectory, "overflow-bubble-preview.png")); encoder.Save(file);
            return 0;
        }
        finally { bubble?.Close(); owner.Close(); }
    }
}
