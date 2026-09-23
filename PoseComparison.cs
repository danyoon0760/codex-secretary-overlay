using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using FlowDirection = System.Windows.FlowDirection;

namespace SecretaryOverlay;

internal static class PoseComparison
{
    private static readonly string[] Files = ["idle", "start", "thinking", "working", "review", "permission", "done", "interrupt", "compact", "ready", "receive", "delegate"];
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(30, 37, 44));
    private static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(231, 234, 237));
    private static readonly Brush Head = new SolidColorBrush(Color.FromRgb(23, 117, 164));
    private static readonly Brush Shoe = new SolidColorBrush(Color.FromRgb(164, 83, 29));
    private const double ViewWidth = 593.75, ViewHeight = 898;

    public static int Render()
    {
        var assets = new PoseAssets(AppStorage.Root);
        string output = AppStorage.DataDirectory;
        Directory.CreateDirectory(output);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Paper, null, new Rect(0, 0, 1500, 1900));
            Text(dc, "전체 키 보정 · 전후 비교", 28, 24, 30);
            Text(dc, "같은 창 크기 · 가로/세로 동일 비율 보정 · 대기 자세 기준 · 원본 PNG 유지", 28, 70, 16);
            Text(dc, "파랑: 정수리 기준선     갈색: 구두 바닥선     각 칸의 왼쪽은 전 / 오른쪽은 후", 28, 97, 15);
            for (int i = 0; i < Files.Length; i++)
            {
                string file = Files[i];
                double x = i % 3 * 500, y = 140 + i / 3 * 440;
                dc.DrawRectangle(null, new Pen(Brushes.White, 2), new Rect(x + 8, y + 3, 484, 432));
                var label = StateEngine.Poses.Values.First(p => p.File == file).Label;
                Text(dc, label, x + 20, y + 12, 18);
                var old = PoseRegistration.Placement(file, ViewWidth, ViewHeight, true);
                var next = PoseRegistration.Placement(file, ViewWidth, ViewHeight);
                string adjustment = $"표시 크기 {(next.Width / old.Width - 1) * 100:+0.0;-0.0;0.0}%" + (file == "delegate" ? " · 얼굴/상체 기준" : "");
                Text(dc, adjustment, x + 20, y + 38, 13);
                Text(dc, "전", x + 116, y + 62, 14);
                Text(dc, "후", x + 366, y + 62, 14);
                DrawPose(dc, assets, file, new Point(x + 14, y + 87), .37, true, true);
                DrawPose(dc, assets, file, new Point(x + 264, y + 87), .37, false, true);
            }
        }
        Save(visual, 1500, 1900, Path.Combine(output, "pose-height-comparison.png"));

        var lineup = new DrawingVisual();
        using (var dc = lineup.RenderOpen())
        {
            dc.DrawRectangle(Paper, null, new Rect(0, 0, 1900, 1170));
            Text(dc, "전후 전체 정렬 비교", 24, 18, 28);
            Text(dc, "파랑: 정수리 / 갈색: 구두 바닥 · 서류 전달은 얼굴·상체 기준으로 맞춰 머리가 낮습니다", 24, 60, 15);
            for (int row = 0; row < 2; row++)
            {
                double y = 102 + row * 528;
                Text(dc, row == 0 ? "보정 전" : "보정 후", 24, y, 22);
                for (int i = 0; i < Files.Length; i++)
                {
                    // Transparent source margins may overlap, but character silhouettes do not.
                    double x = 20 + i * 148;
                    DrawPose(dc, assets, Files[i], new Point(x - 70, y + 36), .5, row == 0, true);
                    Text(dc, Files[i] == "delegate" ? "서류 전달 (상체 기준)" : Files[i], x + 10, y + 491, 13);
                }
            }
        }
        Save(lineup, 1900, 1170, Path.Combine(output, "pose-height-lineup.png"));
        var report = Files.Select(file =>
        {
            var old = PoseRegistration.Placement(file, ViewWidth, ViewHeight, true);
            var next = PoseRegistration.Placement(file, ViewWidth, ViewHeight);
            var points = PoseRegistration.Landmarks[file];
            return new { file, scaleBasis = file == "delegate" ? "face-and-torso" : "full-height", scaleChangePercent = (next.Width / old.Width - 1) * 100,
                before = new { x = old.Left, y = old.Top, head = old.Top + points.HeadTop * old.Width / points.Width, shoe = old.Top + points.ShoeBottom * old.Width / points.Width },
                after = new { x = next.Left, y = next.Top, head = next.Top + points.HeadTop * next.Width / points.Width, shoe = next.Top + points.ShoeBottom * next.Width / points.Width } };
        });
        File.WriteAllText(Path.Combine(output, "pose-height-registration.json"), JsonSerializer.Serialize(report, AppStorage.Json));
        RenderDelegateComparison(assets, output);
        return 0;
    }

    private static void RenderDelegateComparison(PoseAssets assets, string output)
    {
        var focus = new DrawingVisual();
        using (var dc = focus.RenderOpen())
        {
            dc.DrawRectangle(Paper, null, new Rect(0, 0, 1600, 980));
            Text(dc, "서류 전달 자세 · 얼굴과 상체 크기 보정", 28, 24, 28);
            Text(dc, "얼굴·상체에 같은 비중 / 골반 중심과 구두 바닥 정렬 / 기울기·원본 비율 유지", 28, 68, 16);
            Text(dc, "파랑: 대기 자세의 정수리 · 갈색: 구두 바닥 · 세로 점선: 골반 중심 기준", 28, 98, 14);
            for (int i = 0; i < 3; i++)
            {
                double x = 22 + i * 520;
                Text(dc, i == 0 ? "대기 · 기준" : i == 1 ? "서류 전달 · 전" : "서류 전달 · 후", x + 110, 144, 22);
                DrawPose(dc, assets, i == 0 ? "idle" : "delegate", new Point(x, 194), .8, i == 1, true);
                double center = x + ViewWidth * .8 / 2;
                dc.DrawLine(new Pen(Brushes.Gray, 1) { DashStyle = DashStyles.Dot }, new Point(center, 460), new Point(center, 780));
            }
        }
        Save(focus, 1600, 980, Path.Combine(output, "delegate-comparison.png"));

        var lineup = new DrawingVisual();
        using (var dc = lineup.RenderOpen())
        {
            dc.DrawRectangle(Paper, null, new Rect(0, 0, 1980, 1170));
            Text(dc, "서류 전달 보정 · 전체 자세 비교", 24, 18, 28);
            Text(dc, "위: 서류 전달 보정 전 / 아래: 보정 후 · 나머지 11개 자세는 동일합니다", 24, 60, 15);
            for (int row = 0; row < 2; row++)
            {
                double y = 102 + row * 528;
                Text(dc, row == 0 ? "보정 전" : "보정 후", 24, y, 22);
                for (int i = 0; i < Files.Length; i++)
                {
                    double x = 20 + i * 148;
                    DrawPose(dc, assets, Files[i], new Point(x - 70, y + 36), .5, row == 0 && Files[i] == "delegate", true);
                    Text(dc, Files[i] == "delegate" ? "서류 전달" : Files[i], x + 10, y + 491, 13);
                }
            }
        }
        Save(lineup, 1980, 1170, Path.Combine(output, "delegate-lineup.png"));
    }

    private static void DrawPose(DrawingContext dc, PoseAssets assets, string file, Point origin, double zoom, bool before, bool guides)
    {
        var rect = PoseRegistration.Placement(file, ViewWidth, ViewHeight, before);
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.PushTransform(new ScaleTransform(zoom, zoom));
        dc.DrawImage(assets[file], rect);
        if (guides)
        {
            var reference = PoseRegistration.Landmarks["idle"];
            var referenceRect = PoseRegistration.Placement("idle", ViewWidth, ViewHeight);
            double scale = referenceRect.Width / reference.Width;
            double headY = referenceRect.Top + reference.HeadTop * scale;
            double shoeY = referenceRect.Top + reference.ShoeBottom * scale;
            var headPen = new Pen(Head, 1 / zoom) { DashStyle = DashStyles.Dash };
            var shoePen = new Pen(Shoe, 1 / zoom) { DashStyle = DashStyles.Dash };
            dc.DrawLine(headPen, new Point(0, headY), new Point(ViewWidth, headY));
            dc.DrawLine(shoePen, new Point(0, shoeY), new Point(ViewWidth, shoeY));
        }
        dc.Pop(); dc.Pop();
    }

    private static void Text(DrawingContext dc, string value, double x, double y, double size) =>
        dc.DrawText(new FormattedText(value, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
            new Typeface("Malgun Gothic"), size, Ink, 1), new Point(x, y));

    private static void Save(DrawingVisual visual, int width, int height, string path)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
