using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace SecretaryOverlay;

internal static class ChatNavigationVerification
{
    private const string First = "11111111-1111-4111-8111-111111111111";
    private const string Second = "22222222-2222-4222-8222-222222222222";

    public static void Check(Action<bool, string> check)
    {
        var link = ChatNavigation.Link(First);
        check(link?.AbsoluteUri == "codex://threads/" + First && link.Query.Length == 0,
            "Chat navigation targets the existing thread route without a prompt or execution parameters");
        check(ChatNavigation.Link("") is null && ChatNavigation.Link("new") is null
            && ChatNavigation.Link("codex://threads/new?prompt=run") is null
            && ChatNavigation.Link(Guid.Empty.ToString()) is null,
            "The chat button cannot launch arbitrary URLs, create a thread, or inject a message");
        ProcessStartInfo? launched = null;
        check(ChatNavigation.TryOpen(First, start => launched = start) && launched?.UseShellExecute == true
            && launched.FileName == link!.AbsoluteUri && launched.Arguments.Length == 0 && launched.ArgumentList.Count == 0,
            "Opening a chat uses the registered Windows URI handler without a command shell");
        check(!ChatNavigation.TryOpen(First, _ => throw new Win32Exception("Synthetic missing handler")),
            "An unavailable application handler becomes a recoverable navigation failure");
        CheckButton(check);
        CheckOverflow(check);
    }

    private static void CheckButton(Action<bool, string> check)
    {
        var requests = new List<string>();
        var card = new BubbleCard(() => { });
        void Click() => card.OpenChat.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        card.Update("프로젝트", "작업 결과를 정리했어요.", "확인 완료", "채팅 제목");
        card.SetNavigation(First, false, requests.Add); Click();
        check(card.OpenChat.Visibility == Visibility.Collapsed && requests.Count == 0,
            "An active task keeps the completion-only chat button hidden and cannot invoke it");
        card.SetNavigation(First, true, requests.Add); Click();
        check(card.OpenChat.Visibility == Visibility.Visible && requests.SequenceEqual(new[] { First })
            && System.Windows.Automation.AutomationProperties.GetName(card.OpenChat) == "Codex에서 이 채팅 열기",
            "The completed bubble opens exactly its own task and exposes a clear accessible button name");
        card.SetNavigation(Second, true, requests.Add); Click();
        check(requests.SequenceEqual(new[] { First, Second }), "A reused card never keeps the previous chat destination");
        card.Surface.Width = 440;
        card.Surface.Measure(new Size(440, double.PositiveInfinity));
        card.Surface.Arrange(new Rect(0, 0, 440, card.Surface.DesiredSize.Height));
        card.Surface.UpdateLayout();
        var footer = card.Footer.TranslatePoint(new Point(), card.Surface);
        var button = card.OpenChat.TranslatePoint(new Point(), card.Surface);
        check(button.X >= footer.X + card.Footer.ActualWidth && button.X + card.OpenChat.ActualWidth <= card.Surface.ActualWidth
            && card.Detail.ActualWidth > 0 && card.Footer.Children.OfType<TextBlock>().Count() == 5,
            "The small logo-link button has its own footer space and does not cover the identity or activity text");
        card.SetNavigation(First, false, requests.Add); Click();
        card.SetNavigation("new", true, requests.Add); Click();
        card.SetNavigation(First, true, null); Click();
        check(card.OpenChat.Visibility == Visibility.Collapsed && requests.Count == 2,
            "New turns, invalid destinations, and absent handlers disable stale chat-button callbacks");
    }

    private static void CheckOverflow(Action<bool, string> check)
    {
        var tasks = Enumerable.Range(1, 5).Select(i => new TaskProgress($"{i:00000000}-1111-4111-8111-111111111111")
            { ChatTitle = "작업 " + i }).ToArray();
        string selected = "";
        check(OtherTaskMenu.Create(tasks.Take(3).ToArray(), value => selected = value) is null,
            "Three or fewer tasks do not add another menu item");
        var menu = OtherTaskMenu.Create(tasks, value => selected = value)!;
        check(menu.Label == "다른 작업 보기" && menu.Children!.Select(item => item.Label).SequenceEqual(new[] { "작업 4", "작업 5" })
            && selected.Length == 0, "Only hidden overflow tasks appear, and opening their menu does not select anything");
        menu.Children![1].Action!();
        check(selected == tasks[4].Key, "Selecting an overflow row forwards the exact hidden question identity");
        tasks[0].Dismissed = true;
        tasks[4].ChatTitle = "";
        menu = OtherTaskMenu.Create(tasks, value => selected = value)!;
        check(menu.Children!.Count == 1 && menu.Children[0].Label.StartsWith("제목 없는 작업 (00000005)", StringComparison.Ordinal),
            "Dismissed tasks are excluded before overflow selection, and unnamed tasks receive an identifiable fallback");
        tasks[4].Project = "R&D\n프로젝트";
        tasks[4].ChatTitle = "확인\t완료";
        menu = OtherTaskMenu.Create(tasks, _ => { })!;
        check(menu.Children![0].Label == "R&&D 프로젝트 • 확인 완료",
            "Task menu labels keep literal ampersands and normalize line breaks without changing their action");
        tasks[4].ChatTitle = string.Concat(Enumerable.Repeat("긴 제목😀", 30));
        menu = OtherTaskMenu.Create(tasks, _ => { })!;
        check(menu.Children![0].Label.EndsWith('…') && menu.Children[0].Label.Length < 150,
            "Long overflow titles remain bounded without breaking a Unicode text element");
    }
}
