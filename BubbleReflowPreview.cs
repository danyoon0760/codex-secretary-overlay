using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using Point = System.Windows.Point;
using SystemFonts = System.Windows.SystemFonts;

namespace SecretaryOverlay;

internal static class BubbleReflowPreview
{
    private sealed record Snapshot(string Label, BitmapSource Image, Rect Bounds);

    public static int Render()
    {
        var area = SystemParameters.WorkArea;
        if (area.IsEmpty || area.Width <= 0 || area.Height <= 0)
            throw new InvalidOperationException("말풍선 미리보기를 만들 화면 영역이 없습니다.");

        const double ownerWidth = 350, ownerHeight = 560;
        double anchor = area.Top + Math.Min(300, area.Height * .45);
        var owner = new Window
        {
            Width = ownerWidth, Height = ownerHeight,
            Left = area.Left + Math.Max(0, area.Width * .70 - ownerWidth / 2),
            Top = anchor - ownerHeight * .16,
            Opacity = 0, IsHitTestVisible = false, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize
        };
        SpeechBubble? bubble = null;
        try
        {
            owner.Show();
            long now = 0;
            bubble = new SpeechBubble(owner, () => now, () => true)
                { Opacity = 0, IsHitTestVisible = false };
            var current = Task("00000000-0000-4000-8000-000000000001", "설정 화면", "설정 항목을 확인하고 있어요.", "화면 구성 확인 중");
            var older = Task("00000000-0000-4000-8000-000000000002", "말풍선 위치", "말풍선의 위치를 조정하고 있어요.", "위치 계산 확인 중");
            var newest = Task("00000000-0000-4000-8000-000000000003", "자동 닫기", "자동 닫기 동작과\n읽는 동안의 표시를 확인하고 있어요.", "동작 확인 중");

            Snapshot Capture(string label)
            {
                bubble.AdvanceReflow();
                bubble.UpdateLayout();
                var content = (FrameworkElement)bubble.Content;
                double width = Math.Max(1, content.ActualWidth);
                double height = Math.Max(1, content.ActualHeight);
                var image = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
                image.Render(content);
                image.Freeze();
                return new(label, image, new Rect(bubble.Left, bubble.Top, width, height));
            }

            var insertion = new List<Snapshot>();
            bubble.SetTasks([current, older]);
            now = 150; bubble.AdvanceReflow();
            now = 2_000;
            insertion.Add(Capture("추가 전"));
            now = 2_100;
            bubble.SetTasks([newest, current, older]);
            now = 2_190;
            insertion.Add(Capture("이동 중 · 90ms"));
            now = 2_280; bubble.AdvanceReflow();
            now = 2_355;
            insertion.Add(Capture("새 창 등장 중 · 75ms"));
            now = 2_430; bubble.AdvanceReflow();
            now = 2_520;
            insertion.Add(Capture("본문 이어 쓰는 중"));
            now = 4_000;
            insertion.Add(Capture("표시 완료"));

            var removal = new List<Snapshot> { Capture("닫기 전") };
            now = 5_000;
            bubble.SetTasks([newest, older]);
            removal.Add(Capture("닫은 직후"));
            now = 5_090;
            removal.Add(Capture("이동 중 · 90ms"));
            now = 5_180;
            removal.Add(Capture("이동 완료 · 180ms"));

            now = 6_000;
            newest.Active = false; newest.State = "Stop";
            newest.Body = "말풍선 표시와 자동 닫기 변경을 적용했습니다.";
            newest.Detail = "작업 완료";
            bubble.SetTasks([newest, older]);
            now = 8_000;
            var completion = new List<Snapshot> { Capture("완료 · 로고와 ↗로 채팅 열기") };
            WriteContactSheet([("새 말풍선: 자리 이동 → 창 등장 → 본문 표시", insertion),
                ("가운데 말풍선 닫기", removal), ("완료 메시지", completion)]);
            return 0;
        }
        finally { bubble?.Close(); owner.Close(); }
    }

    private static TaskProgress Task(string session, string title, string body, string detail) => new(session)
        { Project = "비서", ChatTitle = title, Body = body, Detail = detail };

    private static void WriteContactSheet((string Title, List<Snapshot> Images)[] rows)
    {
        const double margin = 24, gap = 16, padding = 18, rowHeading = 36, labelHeight = 32;
        var extents = rows.Select(row => row.Images.Select(snapshot => snapshot.Bounds)
            .Aggregate(Rect.Empty, (bounds, next) => { bounds.Union(next); return bounds; })).ToArray();
        double tileWidth = Math.Ceiling(extents.Max(bounds => bounds.Width) + padding * 2);
        var rowHeights = extents.Select(bounds => Math.Ceiling(bounds.Height) + padding * 2 + labelHeight).ToArray();
        int columns = rows.Max(row => row.Images.Count);
        double width = margin * 2 + tileWidth * columns + gap * (columns - 1);
        double height = margin * 2 + rowHeights.Sum() + rowHeading * rows.Length + gap;
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(233, 235, 239)), null, new Rect(0, 0, width, height));
            double y = margin;
            for (int row = 0; row < rows.Length; row++)
            {
                Text(dc, rows[row].Title, new Point(margin, y), 18);
                y += rowHeading;
                for (int col = 0; col < rows[row].Images.Count; col++)
                {
                    double x = margin + col * (tileWidth + gap);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(249, 250, 252)), null,
                        new Rect(x, y, tileWidth, rowHeights[row]), 5, 5);
                    var snapshot = rows[row].Images[col];
                    Text(dc, snapshot.Label, new Point(x + padding, y + 8), 13);
                    // Every snapshot in a row uses the same screen origin, preserving real motion.
                    var destination = new Rect(x + padding + snapshot.Bounds.Left - extents[row].Left,
                        y + labelHeight + padding + snapshot.Bounds.Top - extents[row].Top,
                        snapshot.Bounds.Width, snapshot.Bounds.Height);
                    dc.DrawImage(snapshot.Image, destination);
                }
                y += rowHeights[row] + gap;
            }
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(AppStorage.DataDirectory);
        using var output = File.Create(Path.Combine(AppStorage.DataDirectory, "bubble-reflow-preview.png"));
        encoder.Save(output);
    }

    private static void Text(DrawingContext context, string value, Point point, double size) =>
        context.DrawText(new FormattedText(value, System.Globalization.CultureInfo.GetCultureInfo("ko-KR"),
            FlowDirection.LeftToRight, new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Bold,
                FontStretches.Normal), size, Brushes.Black, 1), point);
}
