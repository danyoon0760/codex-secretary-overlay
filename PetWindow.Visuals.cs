using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace SecretaryOverlay;

public sealed partial class PetWindow
{
    private void BuildVisualTree()
    {
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BadgeHeight) });
        layers.RenderTransform = breath;
        layers.RenderTransformOrigin = new Point(.5, 1);
        layers.ClipToBounds = false;
        layers.SizeChanged += (_, _) =>
        {
            foreach (Image image in layers.Children) PositionPose(image);
        };
        root.Children.Add(layers);
        var texts = new StackPanel();
        texts.Children.Add(label);
        texts.Children.Add(indicator);
        badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 25, 28, 37)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = texts
        };
        Grid.SetRow(badge, 1);
        root.Children.Add(badge);
        Content = root;
    }

    private void ConfigureInput()
    {
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                ShowControls();
                return;
            }
            try
            {
                DragMove();
                SaveLayout();
            }
            catch (InvalidOperationException)
            {
                // The mouse can be released before WPF starts the drag.
            }
        };
        MouseWheel += (_, e) =>
        {
            ChangeSize(e.Delta > 0 ? 1.08 : 1 / 1.08);
            e.Handled = true;
        };
        MouseRightButtonUp += (_, _) => ShowMenu();
    }
}
