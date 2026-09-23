using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Path = System.Windows.Shapes.Path;

namespace SecretaryOverlay;

internal sealed partial class BubbleCard
{
    public TextBlock PetName { get; } = PetTypography.Text(PetPersonalization.DefaultName, 12);
    public TextBlock Project { get; } = PetTypography.Text("", 12);
    public TextBlock ChatTitle { get; } = PetTypography.Text("", 12);
    public Grid Footer { get; } = new() { Margin = new Thickness(0, 13, 0, 0) };
    private readonly TextBlock projectDot = PetTypography.Text("•", 12);
    private readonly TextBlock titleDot = PetTypography.Text("•", 12);
    public TextBlock Body { get; } = PetTypography.Text("", 17);
    public TextBlock Detail { get; } = PetTypography.Text("", 12);
    public Grid Surface { get; }
    public Path Tail { get; }
    public Button Close { get; }
    internal Button More { get; } = new();
    private readonly ScrollViewer bodyScroll = new();
    private bool expanded;
    private bool longBody;
    private readonly TextBlock detailTip = PetTypography.Text("", 12);

    public BubbleCard(Action close, TextBlock? body = null)
    {
        if (body is not null) Body = body;
        Body.FontFamily = PetTypography.Mixed;
        Body.FontWeight = FontWeights.Normal;
        Body.TextWrapping = TextWrapping.Wrap;
        Body.LineHeight = 27;
        var rows = new StackPanel { Margin = new Thickness(17, 17, 17, 17) };
        PetName.TextWrapping = TextWrapping.NoWrap;
        PetName.TextTrimming = TextTrimming.CharacterEllipsis;
        PetName.Margin = new Thickness(0, 0, 20, 10);
        PetName.ToolTip = PetName.Text;
        rows.Children.Add(PetName);
        Project.TextWrapping = TextWrapping.NoWrap;
        Project.TextTrimming = TextTrimming.CharacterEllipsis;
        Project.Visibility = Visibility.Collapsed;
        Body.Margin = new Thickness(0, 0, 7, 0);
        var bodyHost = new Grid();
        bodyHost.Children.Add(Body);
        InitializePresentation(bodyHost);
        bodyScroll.Content = bodyHost;
        bodyScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        bodyScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        rows.Children.Add(bodyScroll);
        More.Background = Brushes.Transparent;
        More.Foreground = Brushes.Black;
        More.BorderThickness = new Thickness(0);
        More.Padding = new Thickness(0);
        More.Margin = new Thickness(0, 4, 0, 0);
        More.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        More.FontSize = 12;
        More.Content = "자세히 보기";
        More.Visibility = Visibility.Collapsed;
        More.Click += (_, _) => { expanded = !expanded; UpdateBodyExpansion(); };
        System.Windows.Automation.AutomationProperties.SetName(More, "말풍선 자세히 보기");
        rows.Children.Add(More);
        ChatTitle.TextWrapping = TextWrapping.NoWrap;
        ChatTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        Project.MaxWidth = 80;
        ChatTitle.MaxWidth = 132;
        Footer.Visibility = Visibility.Collapsed;
        var fields = new TextBlock[] { Project, projectDot, ChatTitle, titleDot, Detail };
        for (int i = 0; i < fields.Length; i++)
        {
            Footer.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetColumn(fields[i], i);
            Footer.Children.Add(fields[i]);
        }
        projectDot.Margin = titleDot.Margin = new Thickness(5, 0, 5, 0);
        Footer.SizeChanged += (_, e) =>
        {
            Project.MaxWidth = Math.Max(0, e.NewSize.Width * .20);
            ChatTitle.MaxWidth = Math.Max(0, e.NewSize.Width * .33);
        };
        Detail.TextTrimming = TextTrimming.CharacterEllipsis;
        Detail.TextWrapping = TextWrapping.NoWrap;
        Detail.Visibility = Visibility.Collapsed;
        detailTip.TextWrapping = TextWrapping.Wrap;
        detailTip.MaxWidth = 540;
        Detail.ToolTip = new System.Windows.Controls.ToolTip { Content = new ScrollViewer { Content = detailTip, MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        ToolTipService.SetShowDuration(Detail, 30000);
        rows.Children.Add(Footer);
        var inner = new Grid();
        inner.Children.Add(rows);
        Close = new Button
        {
            Content = PetTypography.Text("×", 14), Width = 22, Height = 22,
            Background = Brushes.White, Foreground = Brushes.Black, BorderThickness = new Thickness(0),
            Padding = new Thickness(0), HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 1, 0),
            ToolTip = "이 말풍선 닫기 (작업은 계속됩니다)", Focusable = false
        };
        System.Windows.Automation.AutomationProperties.SetName(Close, "말풍선 닫기");
        Close.Click += (_, _) => close();
        inner.Children.Add(Close);
        InitializeNavigation(inner, rows);
        Surface = new Grid { Background = Brushes.Transparent };
        Surface.Children.Add(new Border
        {
            Child = inner, Background = Brushes.White, BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 0, 12), SnapsToDevicePixels = true
        });
        Tail = new Path
        {
            Data = Geometry.Parse("M 0,0 L 14,12 L 14,0"), Stroke = Brushes.Black, Fill = Brushes.White,
            StrokeThickness = 2, Width = 18, Height = 14, Stretch = Stretch.None,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 25, 0)
        };
        Surface.Children.Add(Tail);
    }

    public void Update(string project, string body, string detail, string chatTitle = "")
    {
        body = ProgressDisplayText.Clean(body);
        project = System.Text.RegularExpressions.Regex.Replace(project, @"\s+", " ").Trim();
        chatTitle = System.Text.RegularExpressions.Regex.Replace(chatTitle, @"\s+", " ").Trim();
        Project.Text = project;
        Project.ToolTip = project;
        Project.Visibility = project.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ChatTitle.Text = chatTitle;
        ChatTitle.ToolTip = chatTitle;
        ChatTitle.Visibility = chatTitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        detail = System.Text.RegularExpressions.Regex.Replace(detail, @"\s+", " ").Trim();
        Detail.Text = detail;
        detailTip.Text = detail;
        Detail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        projectDot.Visibility = project.Length > 0 && (chatTitle.Length > 0 || detail.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
        titleDot.Visibility = chatTitle.Length > 0 && detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Footer.Visibility = project.Length + chatTitle.Length + detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Body.Text == body) return;
        Body.Text = body;
        longBody = BodyExpansionPolicy.ShouldCollapse(body);
        if (!longBody) expanded = false;
        UpdateBodyExpansion();
    }

    public void PointTail(bool onLeft)
    {
        Tail.HorizontalAlignment = onLeft ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        Tail.Margin = onLeft ? new Thickness(0, 0, 25, 0) : new Thickness(25, 0, 0, 0);
        Tail.RenderTransform = onLeft ? Transform.Identity : new ScaleTransform(-1, 1, 9, 0);
    }

    public void SetPetName(string name)
    {
        PetName.Text = name;
        PetName.ToolTip = name;
    }

    public void SetFontSize(double fontSize)
    {
        Body.FontSize = fontSize;
        Body.LineHeight = fontSize * 27 / 17;
        foreach (var label in new[] { PetName, Project, ChatTitle, Detail, detailTip, projectDot, titleDot })
            label.FontSize = fontSize * 12 / 17;
        SyncPresentationFont();
        UpdateBodyExpansion();
    }

    private void UpdateBodyExpansion()
    {
        More.Visibility = longBody ? Visibility.Visible : Visibility.Collapsed;
        More.Content = expanded ? "간략히 보기" : "자세히 보기";
        System.Windows.Automation.AutomationProperties.SetName(More, expanded ? "말풍선 간략히 보기" : "말풍선 자세히 보기");
        bodyScroll.MaxHeight = !longBody ? double.PositiveInfinity : expanded
            ? Math.Max(Body.LineHeight * 5, SystemParameters.WorkArea.Height * .60)
            : Body.LineHeight * 5;
        bodyScroll.VerticalScrollBarVisibility = expanded ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        if (!expanded) bodyScroll.ScrollToTop();
        bodyScroll.InvalidateMeasure();
        Surface.InvalidateMeasure();
    }
}
