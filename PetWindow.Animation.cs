using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace SecretaryOverlay;

public sealed partial class PetWindow
{
    private void SetPose(string state)
    {
        var pose = StateEngine.Poses[state];
        if (state != "Idle")
        {
            if (!manualRequest) commentaryRequest?.Cancel();
        }
        label.Text = pose.Label;
        UpdateIndicator();
        activeFile = pose.File;
        var next = new Image
        {
            Source = assets[pose.File],
            Stretch = Stretch.Uniform,
            Opacity = 0,
            RenderTransformOrigin = new Point(.5, 1),
            Tag = pose.File
        };
        var old = front;
        front = next;
        layers.Children.Add(next);
        PositionPose(next);
        var durationMs = motion ? (state == "Interrupt" ? 200 : 420) : 0;
        next.BeginAnimation(OpacityProperty, Fade(0, 1, durationMs));
        if (old != null)
        {
            var animation = Fade(old.Opacity, 0, durationMs);
            animation.Completed += (_, _) => layers.Children.Remove(old);
            old.BeginAnimation(OpacityProperty, animation);
        }
        // Bound the number of transitional images during bursts of urgent events.
        while (layers.Children.Count > 3) layers.Children.RemoveAt(0);
        if (motion && state is "Stop" or "SessionStart" or "SessionEnd") AnimateNod(next, state);
        WriteStatus();
    }

    private void PositionPose(Image image)
    {
        if (image.Tag is not string file || layers.ActualWidth <= 0 || layers.ActualHeight <= 0) return;
        var rect = PoseRegistration.Placement(file, layers.ActualWidth, layers.ActualHeight);
        image.Width = rect.Width;
        image.Height = rect.Height;
        Canvas.SetLeft(image, rect.Left);
        Canvas.SetTop(image, rect.Top);
    }

    private static DoubleAnimation Fade(double from, double to, int durationMs) =>
        new(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

    private static void AnimateNod(Image image, string state)
    {
        var shift = new TranslateTransform();
        image.RenderTransform = shift;
        var nod = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1000) };
        nod.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        nod.KeyFrames.Add(new EasingDoubleKeyFrame(state == "SessionEnd" ? 7 : 3, KeyTime.FromPercent(.45),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        nod.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        shift.BeginAnimation(TranslateTransform.YProperty, nod);
    }

    private void SetBreathing()
    {
        breath.BeginAnimation(ScaleTransform.ScaleYProperty, motion
            ? new DoubleAnimation(1, 1.003, TimeSpan.FromSeconds(2.7))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            }
            : null);
    }
}
