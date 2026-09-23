using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace SecretaryOverlay;

internal sealed partial class BubbleCard
{
    public Button OpenChat { get; } = new()
    {
        Width = 64, Height = 36, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(8, 3, 0, 0),
        Background = Brushes.White, Foreground = Brushes.Black, BorderThickness = new Thickness(0),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        Visibility = Visibility.Collapsed, Focusable = false, ToolTip = "Codex에서 이 채팅 열기"
    };
    private string navigationSession = "";
    private Action<string>? openChat;

    private void InitializeNavigation(Grid inner, StackPanel rows)
    {
        OpenChat.Content = PetTypography.Text("Codex ↗", 12);
        System.Windows.Automation.AutomationProperties.SetName(OpenChat, "Codex에서 이 채팅 열기");
        OpenChat.Click += (_, _) =>
        {
            if (OpenChat.Visibility == Visibility.Visible && OpenChat.IsEnabled) openChat?.Invoke(navigationSession);
        };
        // Keep the footer's text columns intact; the destination button has its own reserved space.
        rows.Children.Remove(Footer);
        var footerRow = new Grid();
        footerRow.ColumnDefinitions.Add(new ColumnDefinition());
        footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerRow.Children.Add(Footer);
        Grid.SetColumn(OpenChat, 1); footerRow.Children.Add(OpenChat); rows.Children.Add(footerRow);
    }

    public void SetNavigation(string session, bool canOpen, Action<string>? open)
    {
        bool available = canOpen && open is not null && ChatNavigation.Link(session) is not null;
        navigationSession = available ? session : "";
        openChat = available ? open : null;
        OpenChat.IsEnabled = available;
        OpenChat.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }

}
