using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SecretaryOverlay;

internal static class ChatNavigation
{
    // Verified against the installed OpenAI.Codex package's windows.protocol registration
    // and its bootstrap threads route, which opens an existing localConversation.
    public static Uri? Link(string session) => Guid.TryParse(session, out var id) && id != Guid.Empty
        ? new Uri("codex://threads/" + id.ToString("D")) : null;

    public static bool TryOpen(string session, Action<ProcessStartInfo>? launch = null)
    {
        var link = Link(session);
        if (link is null) return false;
        try
        {
            var start = new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true };
            if (launch is null) Process.Start(start);
            else launch(start);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        { return false; }
    }
}

internal static class OtherTaskMenu
{
    public static MenuEntry? Create(IReadOnlyList<TaskProgress> tasks, Action<string> select)
    {
        var overflow = tasks.Where(task => !task.Dismissed).Skip(BubbleCapacity.MaximumVisible).ToArray();
        if (overflow.Length == 0) return null;
        return new MenuEntry("다른 작업 보기", Children: overflow.Select(task =>
            new MenuEntry(Label(task, tasks.Count(t => !t.Dismissed && t.Session == task.Session) > 1), () => select(task.Key))).ToArray());
    }

    private static string Label(TaskProgress task, bool multipleQuestions)
    {
        string Clean(string value) => Regex.Replace(new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()), @"\s+", " ").Trim();
        string title = Clean(task.ChatTitle);
        if (title.Length == 0)
        {
            string identity = Guid.TryParse(task.Session, out var id) ? id.ToString("N")[..8] : "이름 없음";
            title = "제목 없는 작업 (" + identity + ")";
        }
        string project = Clean(task.Project);
        string label = project.Length > 0 ? project + " • " + title : title;
        if (task.State == "PermissionRequest") label = "승인 필요 · " + label;
        var text = new StringInfo(label);
        if (text.LengthInTextElements > 60) label = text.SubstringByTextElements(0, 59) + "…";
        if (multipleQuestions) label += " • 질문 " + task.QuestionNumber;
        return label.Replace("&", "&&"); // Both native and tray menus otherwise treat '&' as an accelerator.
    }
}
