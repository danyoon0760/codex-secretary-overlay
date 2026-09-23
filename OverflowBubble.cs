using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Path = System.Windows.Shapes.Path;

namespace SecretaryOverlay;

internal sealed class OverflowBubble
{
    private static readonly Brush HoverBackground = new SolidColorBrush(Color.FromRgb(239, 240, 242));
    private readonly TextBlock label = PetTypography.Text("", 16);
    private readonly Border body;
    private readonly Path tail;
    public Button Button { get; }
    public FrameworkElement Surface => Button;
    public int Count { get; private set; }
    public string Text => label.Text;

    public OverflowBubble(Action clicked)
    {
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.TextAlignment = TextAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        body = new Border
        {
            Child = label, Background = Brushes.White, BorderBrush = Brushes.Black, BorderThickness = new Thickness(2),
            Padding = new Thickness(18, 10, 18, 10), MinWidth = 184, MinHeight = 52,
            Margin = new Thickness(0, 0, 0, 10), SnapsToDevicePixels = true
        };
        tail = new Path
        {
            Data = Geometry.Parse("M 0,0 L 12,11 L 12,0"), Stroke = Brushes.Black, Fill = Brushes.White,
            StrokeThickness = 2, Width = 16, Height = 13, Stretch = Stretch.None,
            VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false
        };
        var content = new Grid(); content.Children.Add(body); content.Children.Add(tail);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        Button = new Button
        {
            Content = content, Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter },
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Margin = new Thickness(0, 8, 0, 0), Cursor = Cursors.Hand, FocusVisualStyle = null,
            FontFamily = System.Windows.SystemFonts.MessageFontFamily, FontWeight = FontWeights.Bold,
            ToolTip = "눌러서 숨겨진 작업 보기"
        };
        Button.Click += (_, _) => { if (Count > 0) clicked(); };
        Button.MouseEnter += (_, _) => SetBackground(HoverBackground);
        Button.MouseLeave += (_, _) => SetBackground(Button.IsKeyboardFocused ? HoverBackground : Brushes.White);
        Button.GotKeyboardFocus += (_, _) => SetBackground(HoverBackground);
        Button.LostKeyboardFocus += (_, _) => SetBackground(Button.IsMouseOver ? HoverBackground : Brushes.White);
        System.Windows.Automation.AutomationProperties.SetHelpText(Button, "눌러서 숨겨진 작업 보기");
        Update(0, true, 17);
    }

    public void Update(int count, bool onLeft, double fontSize, int approvalCount = 0)
    {
        Count = Math.Max(0, count);
        approvalCount = Math.Clamp(approvalCount, 0, Count);
        // Keep the action count readable even with the narrowest bubble and largest font.
        label.Text = Count == 0 ? "" : approvalCount > 0
            ? $"다른 작업 {Count}개\n승인 필요 {approvalCount}개" : $"다른 작업 {Count}개";
        Button.ToolTip = approvalCount > 0 ? "승인이 필요한 숨겨진 작업이 있어요. 눌러서 보기" : "눌러서 숨겨진 작업 보기";
        Button.Visibility = Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Button.IsEnabled = Count > 0;
        Button.HorizontalAlignment = onLeft ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        double size = double.IsFinite(fontSize) ? Math.Clamp(fontSize, 12, 26) : 17;
        double scale = size / 17;
        Button.FontSize = label.FontSize = 16 * scale;
        body.MinWidth = 184 * scale;
        body.MinHeight = 52 * scale;
        body.Padding = new Thickness(18 * scale, 10 * scale, 18 * scale, 10 * scale);
        tail.HorizontalAlignment = Button.HorizontalAlignment;
        tail.Margin = onLeft ? new Thickness(0, 0, 20, 0) : new Thickness(20, 0, 0, 0);
        tail.RenderTransform = onLeft ? Transform.Identity : new ScaleTransform(-1, 1, 8, 0);
        System.Windows.Automation.AutomationProperties.SetName(Button, label.Text);
    }

    private void SetBackground(Brush brush) { body.Background = brush; tail.Fill = brush; }
}
