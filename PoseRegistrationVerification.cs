using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
using Size = System.Windows.Size;

namespace SecretaryOverlay;

internal static class PoseRegistrationVerification
{
    public static void Check(Action<bool, string> check)
    {
        var assets = new PoseAssets(AppStorage.Root);
        check(StateEngine.Poses.Values.All(p => PoseRegistration.Landmarks.ContainsKey(p.File)), "Every pose has measured registration landmarks");
        check(PoseRegistration.Landmarks.All(p => assets[p.Key].PixelWidth == p.Value.Width && assets[p.Key].PixelHeight == p.Value.Height), "Registration landmarks match the shipped PNG dimensions");
        foreach (double height in new[] { 280.0, 560, 950 })
        {
            double width = height * .625, availableHeight = height - 52;
            var reference = PoseRegistration.Placement("idle", width, availableHeight);
            var anchor = PoseRegistration.Landmarks["idle"];
            double scale = reference.Width / anchor.Width;
            double head = reference.Top + anchor.HeadTop * scale, shoe = reference.Top + anchor.ShoeBottom * scale;
            bool aligned = true, proportional = true, fits = true;
            foreach (var (file, points) in PoseRegistration.Landmarks.Where(x => x.Key != "delegate"))
            {
                var r = PoseRegistration.Placement(file, width, availableHeight);
                double s = r.Width / points.Width;
                aligned &= Math.Abs(r.Top + points.HeadTop * s - head) < .001 && Math.Abs(r.Top + points.ShoeBottom * s - shoe) < .001
                    && Math.Abs(r.Left + r.Width / 2 - width / 2) < .001;
                proportional &= Math.Abs(r.Height / points.Height - s) < .000001;
                // Transparent padding below the shoes may extend beyond the canvas.
                fits &= r.Top >= 0 && r.Top + points.ShoeBottom * s <= availableHeight + .001;
            }
            check(aligned, $"Crown and shoe baseline align while source centering is preserved at height {height}");
            check(proportional && fits, $"Registration preserves proportions and vertical room at height {height}");
            var delivery = PoseRegistration.Placement("delegate", width, availableHeight);
            var deliveryPoints = PoseRegistration.Landmarks["delegate"];
            double deliveryScale = delivery.Width / deliveryPoints.Width;
            check(Math.Abs(deliveryScale / (delivery.Height / deliveryPoints.Height) - 1) < .000001,
                $"Delegation scales uniformly without stretching at height {height}");
            check(Math.Abs(delivery.Top + deliveryPoints.ShoeBottom * deliveryScale - shoe) < .001
                && Math.Abs(delivery.Left + PoseRegistration.DelegateBody.Pelvis.X * deliveryScale
                    - (reference.Left + PoseRegistration.IdleBody.Pelvis.X * scale)) < .001,
                $"Delegation aligns its pelvis and grounded shoe at height {height}");
            check(Math.Abs(PoseRegistration.DelegateBody.EyeSpan * deliveryScale / (PoseRegistration.IdleBody.EyeSpan * scale) - 1) < .02
                && Math.Abs(PoseRegistration.DelegateBody.TorsoLength * deliveryScale / (PoseRegistration.IdleBody.TorsoLength * scale) - 1) < .02,
                $"Delegation face and torso match the reference within two percent at height {height}");
            check(delivery.Top + deliveryPoints.HeadTop * deliveryScale > head + scale * 50,
                $"Delegation keeps its naturally lower head at height {height}");
            check(reference == PoseRegistration.Placement("idle", width, availableHeight, true), $"Idle reference remains unchanged at height {height}");
            foreach (string file in new[] { "idle" })
            {
                var oldBounds = RenderBounds(assets[file], file, width, availableHeight, true);
                var newBounds = RenderBounds(assets[file], file, width, availableHeight, false);
                check(Math.Abs(oldBounds.Left - newBounds.Left) <= 1 && Math.Abs(oldBounds.Top - newBounds.Top) <= 1
                    && Math.Abs(oldBounds.Right - newBounds.Right) <= 1 && Math.Abs(oldBounds.Bottom - newBounds.Bottom) <= 1,
                    $"Actual WPF {file} silhouette matches the original layout at height {height}");
            }
            var renderedDelivery = RenderBounds(assets["delegate"], "delegate", width, availableHeight, false);
            check(renderedDelivery.Top >= 0 && renderedDelivery.Left > 1 && renderedDelivery.Right < width - 1
                && Math.Abs(renderedDelivery.Bottom - shoe) <= 2,
                $"Rendered delegation keeps the shoes and document inside the viewport at height {height}");
        }
    }

    private static Rect RenderBounds(BitmapSource source, string file, double width, double height, bool original)
    {
        var image = new Image { Source = source, Stretch = Stretch.Uniform };
        FrameworkElement surface;
        if (original)
        {
            var grid = new Grid();
            image.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            image.VerticalAlignment = VerticalAlignment.Bottom;
            image.Margin = new Thickness(0, file == "delegate" ? 26 : 4, 0, 0);
            grid.Children.Add(image);
            surface = grid;
        }
        else
        {
            var canvas = new Canvas();
            var rect = PoseRegistration.Placement(file, width, height);
            image.Width = rect.Width; image.Height = rect.Height;
            Canvas.SetLeft(image, rect.Left); Canvas.SetTop(image, rect.Top);
            canvas.Children.Add(image);
            surface = canvas;
        }
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        int stride = bitmap.PixelWidth * 4;
        byte[] pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        int left = bitmap.PixelWidth, top = bitmap.PixelHeight, right = 0, bottom = 0;
        for (int y = 0; y < bitmap.PixelHeight; y++)
            for (int x = 0; x < bitmap.PixelWidth; x++)
                if (pixels[y * stride + x * 4 + 3] > 25)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        return new Rect(left, top, right - left, bottom - top);
    }
}
