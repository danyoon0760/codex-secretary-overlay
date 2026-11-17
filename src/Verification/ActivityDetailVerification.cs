using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace SecretaryOverlay;

internal static class ActivityDetailVerification
{
    public static void Check(Action<bool, string> check)
    {
        const string session = "55555555-5555-4555-8555-555555555555";
        const string command = "dotnet run -c Release --no-build -- --completion-summary-smoke";
        var now = DateTimeOffset.UtcNow;
        var parser = new ProgressParser(session, "turn", now);
        ProgressMessage? Parse(object item) => parser.Parse(JsonSerializer.SerializeToUtf8Bytes(new
            { timestamp = now, type = "event_msg", payload = new { type = "item_completed", thread_id = session, turn_id = "turn", item } }));
        var result = Parse(new { type = "CommandExecution", id = "command", command = new[] { "C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe", "-Command", command }, status = "completed", stdout = "PRIVATE TOOL OUTPUT" });
        check(result?.Text == "명령 실행 완료 · " + command, "Real array-shaped PowerShell records show the full inner command and arguments");
        check(result?.Text.Contains("PRIVATE TOOL OUTPUT") == false, "Detail extraction never reads command output");
        var image = Parse(new { type = "ImageView", id = "image", path = "C:/project/completion-settings-preview.png" });
        check(image?.Kind == ProgressKind.Activity && image.Text == "이미지 확인함 · completion-settings-preview.png", "Real ImageView records display the inspected filename");
        var failed = Parse(new { type = "CommandExecution", id = "failed", command = new[] { "bash", "-lc", "dotnet test" }, status = "failed" });
        check(failed?.Text == "명령 실행 실패 · dotnet test", "A failed invocation is not labeled successful completion");
        var running = Parse(new { type = "CommandExecution", id = "running", command = new[] { "bash", "-lc", "dotnet test" }, status = "inProgress" });
        check(running?.Text == "명령 실행 중 · dotnet test", "A command that returns a running session remains in progress");
        var native = Parse(new { type = "CommandExecution", id = "native", command = new[] { "dotnet", "test", "-c", "Release" }, status = "completed" });
        check(native?.Text == "명령 실행 완료 · dotnet test -c Release", "Non-shell argument arrays preserve the command instead of treating -c as a shell flag");
        string wrapped = "text(await tools.exec_command({cmd:" + JsonSerializer.Serialize(command) + ",max_output_tokens:1000})); image((await tools.view_image({path:\"C:/project/preview.png\"})).image_url);";
        using var hook = JsonDocument.Parse(JsonSerializer.Serialize(new { tool_input = wrapped }));
        check(ActivityDescription.FromHook(hook.RootElement, "functions.exec", false) == "명령 실행 중 · " + command,
            "Pre-tool wrappers expose the first literal command while it is running");
        check(ActivityDescription.FromHook(hook.RootElement, "functions.exec", true) == "이미지 확인함 · preview.png",
            "Completed wrappers expose their last literal action without evaluating code");
        using var codeInput = JsonDocument.Parse(JsonSerializer.Serialize(new { code = wrapped }));
        check(ActivityDescription.FromInput(codeInput.RootElement, "exec", false).EndsWith(command), "Object-shaped orchestration code inputs are supported");
        using var fakeCode = JsonDocument.Parse(JsonSerializer.Serialize(new { code = "const example = " + JsonSerializer.Serialize(wrapped) + ";" }));
        check(ActivityDescription.FromInput(fakeCode.RootElement, "exec", false) == "명령 실행 중", "Tool-call examples inside string literals are not mistaken for live actions");
        using var mcp = JsonDocument.Parse(JsonSerializer.Serialize(new { path = "C:/project/screen.png" }));
        check(ActivityDescription.FromInput(mcp.RootElement, "view_image", false) == "이미지 확인 중 · screen.png", "Direct image hooks include the filename");
        string safe = ActivityDescription.Command("client --api-key SECRET --password='PASSWORD' --token TOKEN -H 'Authorization: Bearer BEARERSECRET' --verbose", false);
        check(!safe.Contains("SECRET") && !safe.Contains("PASSWORD") && !safe.Contains("TOKEN") && safe.Contains("--verbose"), "Explicit credentials are hidden while useful command flags remain visible");
        foreach (var style in new[] { CompletionStyle.B, CompletionStyle.D })
        {
            var board = new ProgressBoard { Style = style };
            var task = board.Accept(new("UserPromptSubmit", session, Turn: "turn"), "비서")!;
            board.Accept(new("PreToolUse", session, "functions.exec", Turn: "turn"), "비서");
            board.Apply(failed!);
            board.Accept(new("PostToolUse", session, "functions.exec", Turn: "turn", Detail: "명령 실행 완료"), "비서");
            check(task.Detail == failed!.Text, style + " retains the precise transcript result after the generic wrapper hook");
            board.Accept(new("PreToolUse", session, "view_image", Turn: "turn", Detail: "이미지 확인 중 · screen.png"), "비서");
            check(task.Detail == "이미지 확인 중 · screen.png", style + " updates the same detail line when the next action starts");
        }
        var card = new BubbleCard(() => { });
        string longText = "명령 실행 중 · " + command + "\n" + new string('한', 160);
        card.Update("비서", "세부 작업을 확인하고 있어요.", longText);
        card.Surface.Width = 350;
        card.Surface.Measure(new System.Windows.Size(350, double.PositiveInfinity));
        card.Surface.Arrange(new Rect(0, 0, 350, card.Surface.DesiredSize.Height));
        card.Surface.UpdateLayout();
        check(card.Detail.TextWrapping == TextWrapping.NoWrap && card.Detail.TextTrimming == TextTrimming.CharacterEllipsis
            && !card.Detail.Text.Contains('\n') && card.Detail.ActualHeight < 25, "Long multiline activity occupies exactly one ellipsized display row");
        var tip = (System.Windows.Controls.ToolTip)card.Detail.ToolTip;
        var fullText = (TextBlock)((ScrollViewer)tip.Content).Content;
        check(fullText.Text == card.Detail.Text && fullText.Text.Length > 160 && fullText.TextWrapping == TextWrapping.Wrap,
            "Hover text retains the complete detail instead of the visibly ellipsized line");
    }

    public static int Render()
    {
        var rows = new StackPanel { Width = SpeechBubble.PreferredWidth };
        var examples = new[] {
            ("비서", "말풍선 위치 조정", "명령 실행 완료 · dotnet build -c Release", "프로젝트명과 채팅명을 아래 한 줄에 모았어요."),
            ("프로젝트 이름이 아주 길어지는 경우", "여러 작업의 말풍선 표시와 위치 조정을 검토하는 채팅", "이미지 확인함 · completion-settings-preview.png", "긴 이름과 세부 작업은 말줄임표로 표시해요."),
            ("", "설정 화면 개선", "편집함 · CompletionSettings.cs +3 -1", "프로젝트가 없는 채팅은 채팅명부터 보여요.")
        };
        foreach (var (project, title, detail, body) in examples)
        {
            var card = new BubbleCard(() => { }); card.Update(project, body, detail, title);
            card.Surface.Margin = new Thickness(0, 0, 0, 12); rows.Children.Add(card.Surface);
        }
        var surface = new Border { Padding = new Thickness(20), Background = new SolidColorBrush(Color.FromRgb(225, 226, 229)), Child = rows };
        double width = SpeechBubble.PreferredWidth + 40;
        surface.Measure(new System.Windows.Size(width, double.PositiveInfinity)); surface.Arrange(new Rect(0, 0, width, surface.DesiredSize.Height)); surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(AppStorage.DataDirectory);
        using var file = File.Create(Path.Combine(AppStorage.DataDirectory, "activity-detail-preview.png")); encoder.Save(file);
        return 0;
    }
}
