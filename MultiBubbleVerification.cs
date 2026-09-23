using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;

namespace SecretaryOverlay;

internal static class MultiBubbleVerification
{
    public static void Check(Action<bool, string> check)
    {
        var ids = Enumerable.Range(1, 4).Select(i => $"00000000-0000-4000-8000-{i:000000000000}").ToArray();
        var board = new ProgressBoard();
        foreach (var id in ids.Reverse()) board.Accept(new("UserPromptSubmit", id, Turn: "turn-a"), "비서");
        check(board.Visible.Select(t => t.Session).SequenceEqual(ids.Take(3)), "The newest three tasks are visible, newest first, with older overflow retained");
        board.Apply(new(ids[1], "turn-a", "message", "두 번째 작업의 새 메시지", DateTimeOffset.UtcNow));
        check(board.Visible[1].Session == ids[1] && board.Visible[1].Body == "두 번째 작업의 새 메시지", "A task updates in its existing slot");
        board.Dismiss(board.Latest(ids[0])!.Key);
        check(board.Visible.Select(t => t.Session).SequenceEqual(ids.Skip(1)), "Closing the first card preserves survivor order and admits the queued task");
        board.Accept(new("PreToolUse", ids[0], Turn: "turn-a"), "비서");
        check(board.Tasks.Single(t => t.Session == ids[0]).Dismissed && board.Tasks.Single(t => t.Session == ids[0]).Active, "Closing a bubble does not stop work or resurrect on the next hook");
        board.Accept(new("UserPromptSubmit", ids[0], Turn: "turn-b"), "비서");
        check(!board.Tasks.First().Dismissed && board.Tasks.First().Session == ids[0], "A new turn restores a dismissed task at the top");
        check(board.Accept(new("Stop", ids[0], Turn: "turn-a"), "") is { Active: false, Turn: "turn-a" } && board.Tasks.First().Active, "A delayed stop finishes only the older question, leaving the new question active");
        check(!board.Apply(new(ids[0], "turn-a", "old", "古い", DateTimeOffset.UtcNow)), "Old-turn text cannot replace a current task");
        board.Accept(new("PermissionRequest", ids[2], Turn: "turn-a"), "");
        board.Accept(new("PostToolUse", ids[2], Turn: "turn-a"), "");
        check(board.Tasks.Single(t => t.Session == ids[2]).Detail == "승인 기다리는 중", "Approval status survives unrelated tool completion");
        board.Accept(new("Stop", ids[1], Turn: "turn-a"), "");
        check(board.Tasks.Single(t => t.Session == ids[1]).Detail == "응답 완료" && board.HasActive, "Completion belongs to one task while the other tasks remain active");
        check(board.Tasks.Single(t => t.Session == ids[1]).Body == ProgressDisplayText.Finished("Stop"), "A completed task replaces in-progress text with a Korean completion message");
        check(!board.Apply(new(ids[1], "turn-a", "late-summary", "**Finalizing update messaging**", DateTimeOffset.UtcNow, ProgressKind.Summary))
            && board.Tasks.Single(t => t.Session == ids[1]).Body == ProgressDisplayText.Finished("Stop"), "A late reasoning heading cannot overwrite the completed task");
        check(ProgressDisplayText.Clean("**Finalizing update messaging**") == "Finalizing update messaging", "The exact reported bold-markup regression is removed");
        check(ProgressDisplayText.Clean("### **진행 상황**\n`PetWindow.cs`와 [문서](https://example.test)를 확인합니다.") == "진행 상황\nPetWindow.cs와 문서를 확인합니다.",
            "Headings, bold, inline code and links retain readable plain text");
        check(ProgressDisplayText.Clean("my_file_name.cs와 a ** b") == "my_file_name.cs와 a ** b", "Literal filename underscores and arithmetic operators survive cleanup");
        var languageTask = board.Tasks.Single(t => t.Session == ids[3]);
        board.Apply(new(ids[3], "turn-a", "ko-progress", "말풍선의 표시를 수정하고 있습니다.", DateTimeOffset.UtcNow));
        board.Apply(new(ids[3], "turn-a", "en-summary", "**Finalizing update messaging**", DateTimeOffset.UtcNow, ProgressKind.Summary));
        check(languageTask.Body == "말풍선의 표시를 수정하고 있습니다." && languageTask.Detail == "생각하는 중",
            "English reasoning preserves the Korean progress message while updating activity status");
        board.Apply(new(ids[3], "turn-a", "ko-summary", "**한국어 요약**을 확인하고 있습니다.", DateTimeOffset.UtcNow, ProgressKind.Summary));
        check(languageTask.Body == "한국어 요약을 확인하고 있습니다.", "A Korean public summary updates normally without markdown delimiters");
        board.Apply(new(ids[0], "turn-b", "en-first", "**Inspecting the screenshot**", DateTimeOffset.UtcNow, ProgressKind.Summary));
        check(board.Find(ids[0], "turn-b")?.Body == "Inspecting the screenshot", "An English-only initial summary replaces the waiting text");
        board.Apply(new(ids[3], "turn-a", "en-commentary", "Checking the result", DateTimeOffset.UtcNow));
        check(languageTask.Body == "한국어 요약을 확인하고 있습니다.", "English commentary also preserves the latest Korean progress");
        languageTask = board.Accept(new("UserPromptSubmit", ids[3], Turn: "turn-b"), "비서")!;
        board.Apply(new(ids[3], "turn-b", "en-new-turn", "Starting a new task", DateTimeOffset.UtcNow, ProgressKind.Summary));
        check(languageTask.Body == "Starting a new task", "Korean preference resets per turn without suppressing English fallback");
        board.Apply(new(ids[3], "turn-b", "ko-new-turn", "새 작업을 확인합니다.", DateTimeOffset.UtcNow));
        check(languageTask.Body == "새 작업을 확인합니다.", "Korean replaces the initial English fallback as soon as it arrives");

        var now = DateTimeOffset.UtcNow;
        var parser = new ProgressParser(ids[0], "turn-b", now);
        byte[] Item(object item) => JsonSerializer.SerializeToUtf8Bytes(new { timestamp = now, type = "event_msg", payload = new { type = "item_completed", thread_id = ids[0], turn_id = "turn-b", item } });
        var summary = parser.Parse(Item(new { type = "Reasoning", id = "reason-1", summary_text = new[] { "공개 요약입니다." }, raw_content = new[] { "PRIVATE RAW" } }));
        check(summary?.Text == "공개 요약입니다." && summary.Kind == ProgressKind.Summary, "Explicit public reasoning summaries are eligible without raw reasoning");
        check(parser.Parse(Item(new { type = "Reasoning", id = "reason-2", summary_text = Array.Empty<string>(), raw_content = new[] { "PRIVATE RAW" } })) is null,
            "Empty public summaries never fall back to raw reasoning");
        parser.Parse(JsonSerializer.SerializeToUtf8Bytes(new { type = "turn_context", payload = new { turn_id = "turn-b" } }));
        var response = parser.Parse(JsonSerializer.SerializeToUtf8Bytes(new { timestamp = now, type = "response_item", payload = new { type = "reasoning", id = "r", summary = new[] { new { type = "summary_text", text = "공개 요약입니다." } }, encrypted_content = "SECRET" } }));
        check(response is null, "The event and response copies of a public summary are deduplicated");
        var command = parser.Parse(Item(new { type = "CommandExecution", id = "command", command = "dotnet build -c Release", stdout = "SECRET OUTPUT", status = "completed" }));
        check(command?.Text == "명령 실행 완료 · dotnet build -c Release" && command.Kind == ProgressKind.Activity, "Commands include their invocation without tool output");
        using var changes = JsonDocument.Parse("{\"C:/project/App.cs\":{\"type\":\"update\",\"unified_diff\":\"@@ x @@\\n-old\\n+new\\n+extra\\n\"}}");
        check(ActivityDescription.FileChanges(changes.RootElement, true) == "편집함 · App.cs +2 -1", "File activity includes the filename and added/removed line counts");
        check(ActivityDescription.Patch("*** Begin Patch\n*** Add File: C:/project/New.cs\n+one\n+two\n*** End Patch", false) == "편집 중 · New.cs +2 -0", "Pre-tool patch activity is available before editing completes");
        check(!ActivityDescription.Command("tool --api-key SECRET", false).Contains("SECRET"), "Explicit credential values are redacted from activity and tooltip text");
        using var projects = JsonDocument.Parse("{\"local-projects\":{\"p\":{\"name\":\"비서\",\"rootPaths\":[\"C:\\\\project\"]}},\"thread-project-assignments\":{\"task\":{\"projectId\":\"p\"}},\"projectless-thread-ids\":[\"none\"]}");
        check(ProjectNames.Resolve(projects.RootElement, "task", "") == "비서", "Assigned project uses its actual display name");
        check(ProjectNames.Resolve(projects.RootElement, "none", "C:\\project") == "", "A projectless task has no invented project heading");
        check(ProjectNames.Resolve(projects.RootElement, "unknown", "C:\\project-other") == "", "Project path matching respects directory boundaries");
        using var index = new StringReader(JsonSerializer.Serialize(new { id = ids[0], thread_name = "이전 제목" }) + "\n"
            + JsonSerializer.Serialize(new { id = ids[1], thread_name = "다른 채팅" }) + "\n"
            + JsonSerializer.Serialize(new { id = ids[0], thread_name = "새 제목\n변경" }) + "\nnull\n{\"id\":");
        var titles = ChatTitles.Read(index);
        check(titles[ids[0]] == "새 제목 변경" && titles[ids[1]] == "다른 채팅" && !titles.ContainsKey(ids[2]),
            "Title index keeps the latest real name per session, tolerates partial writes, and never invents missing titles");
        var card = new BubbleCard(() => { });
        card.Update("", "메시지", "편집 중 App.cs");
        check(card.Project.Visibility == Visibility.Collapsed, "Missing project leaves no empty heading row");
        card.Update("비서", "새 메시지", "편집함 App.cs +2 -1");
        check(card.Project.Visibility == Visibility.Visible && card.Body.FontFamily.Equals(PetTypography.Mixed)
            && card.Body.FontWeight == FontWeights.Normal && card.Detail.FontWeight == FontWeights.Bold,
            "Task messages use the serif body font while activity labels remain bold");
        card.Surface.Width = 320;
        card.PointTail(true);
        card.Surface.Measure(new Size(320, double.PositiveInfinity));
        card.Surface.Arrange(new Rect(0, 0, 320, card.Surface.DesiredSize.Height));
        check(card.Surface.DesiredSize.Height > card.Body.DesiredSize.Height && double.IsPositiveInfinity(card.Body.MaxHeight), "A card keeps its full natural body height");
        card.Update(new string('가', 100), "메시지", "명령 실행 완료 · dotnet build -c Release", new string('나', 100));
        card.Surface.Width = 440;
        card.Surface.Measure(new Size(440, double.PositiveInfinity));
        card.Surface.Arrange(new Rect(0, 0, 440, card.Surface.DesiredSize.Height));
        card.Surface.UpdateLayout();
        var fields = card.Footer.Children.Cast<TextBlock>().ToArray();
        check(fields.Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text).SequenceEqual(new[] { new string('가', 100), "•", new string('나', 100), "•", "명령 실행 완료 · dotnet build -c Release" }),
            "Footer orders project, chat title, and activity with bold bullet separators");
        check(card.Project.Parent == card.Footer && card.Footer.TranslatePoint(new System.Windows.Point(), card.Surface).Y > card.Body.TranslatePoint(new System.Windows.Point(), card.Surface).Y
            && card.Footer.ActualHeight < 25 && card.Detail.ActualWidth > 150 && card.ChatTitle.TextTrimming == TextTrimming.CharacterEllipsis,
            "Long identities stay below the body on one line while reserving room for readable activity");
        check((string)card.Project.ToolTip == card.Project.Text && (string)card.ChatTitle.ToolTip == card.ChatTitle.Text,
            "Truncated project and chat labels preserve their complete names on hover");
        card.Update("", "메시지", "명령 실행 중", "채팅");
        check(fields.Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text).SequenceEqual(new[] { "채팅", "•", "명령 실행 중" }),
            "A projectless footer has no leading bullet or empty project slot");
        card.Update("비서", "메시지", "응답 완료");
        check(fields.Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text).SequenceEqual(new[] { "비서", "•", "응답 완료" }),
            "An unavailable chat title is omitted without a duplicate separator");
        card.Update("", "자동 한마디", "");
        check(card.Footer.Visibility == Visibility.Collapsed, "Ambient commentary has no empty footer row");
        CheckWindow(check, ids);
    }

    private static void CheckWindow(Action<bool, string> check, string[] ids)
    {
        // Exercise the real WPF window without showing or focusing a test UI on the desktop.
        var owner = new Window { Width = 350, Height = 560, Left = 100, Top = 300, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        SpeechBubble? window = null;
        try
        {
            owner.Show();
            window = new SpeechBubble(owner, animateReflow: () => false) { Opacity = 0 };
            var board = new ProgressBoard();
            foreach (var id in ids.Reverse()) board.Accept(new("UserPromptSubmit", id, Turn: "turn"), "비서");
            board.Tasks[1].ChatTitle = "두 번째 작업";
            window.TaskDismissed += id => { board.Dismiss(id); window.SetTasks(board.Visible); };
            window.SetTasks(board.Visible);
            check(window.VisibleCards.Count == 3, "The real bubble window renders three independent cards");
            check(!window.Say("네 번째 말풍선") && window.VisibleCards.Count == 3, "Ambient commentary cannot displace a task or create a fourth bubble");
            var second = window.VisibleCards[1];
            window.VisibleCards[0].Close.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(window.VisibleCards.Count == 3 && ReferenceEquals(second, window.VisibleCards[0]) && window.VisibleCards[0].ChatTitle.Text == "두 번째 작업", "The real close button promotes the existing second card with its own chat title");
            var first = window.VisibleCards[0];
            board.Apply(new(ids[1], "turn", "update", "본문을 바로 갱신합니다.", DateTimeOffset.UtcNow));
            window.SetTasks(board.Visible);
            check(ReferenceEquals(first, window.VisibleCards[0]) && first.Body.Text == "본문을 바로 갱신합니다.", "Incoming text updates the real card in place");
            board.Accept(new("Stop", ids[1], Turn: "turn"), "비서");
            board.Apply(new(ids[1], "turn", "final", "**말풍선 3개 표시와 글꼴 변경을 적용했어요.**", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
            window.SetTasks(board.Visible);
            check(ReferenceEquals(first, window.VisibleCards[0]) && first.Body.Text == "말풍선 3개 표시와 글꼴 변경을 적용했어요." && first.Detail.Text == "응답 완료",
                "A late completion excerpt updates the existing WPF card with plain bold text");
            foreach (var task in board.Visible) task.Body = string.Concat(Enumerable.Repeat("긴 진행 내용을 화면 안에서 확인합니다.\n", 4));
            window.SetTasks(board.Visible);
            window.UpdateLayout();
            check(window.VisibleCards.All(c => c.Body.Parent is Grid && double.IsPositiveInfinity(c.Body.MaxHeight)), "Long bodies reserve their natural layout without a ScrollViewer or height limit");
            var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(owner).Handle).WorkingArea;
            var transform = PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            double availableHeight = transform.Transform(new System.Windows.Point(screen.Right, screen.Bottom)).Y - transform.Transform(new System.Windows.Point(screen.Left, screen.Top)).Y;
            var workTop = transform.Transform(new System.Windows.Point(screen.Left, screen.Top)).Y;
            check(window.Top >= workTop + 8 && window.ActualHeight <= availableHeight - 16, "A normal three-card stack moves into view without body scrolling");
            check(window.Width == Math.Min(SpeechBubble.PreferredWidth, transform.Transform(new System.Windows.Point(screen.Right, screen.Bottom)).X - transform.Transform(new System.Windows.Point(screen.Left, screen.Top)).X - 16),
                "The live bubble window uses the wider screen-aware width");
            // With enough space above, growing the same card leaves its lower edge in place.
            owner.Top = workTop + availableHeight / 2;
            board.Visible[0].Body = "짧은 내용입니다.";
            window.SetTasks(board.Visible.Take(1).ToArray()); window.UpdateLayout();
            double bottom = window.Top + window.ActualHeight;
            double shortTop = window.Top;
            board.Visible[0].Body = "첫 번째 줄입니다.\n두 번째 줄입니다.\n세 번째 줄입니다.";
            window.SetTasks(board.Visible.Take(1).ToArray()); window.UpdateLayout();
            check(Math.Abs(window.Top + window.ActualHeight - bottom) < 1 && window.Top < shortTop,
                "A growing real WPF bubble keeps its bottom anchored and expands upward");
            var pair = board.Visible.Take(2).ToArray();
            foreach (var task in pair) task.Body = "작업 결과를 확인했어요.";
            window.SetTasks(pair); window.UpdateLayout();
            double firstBottom = window.Top + window.VisibleCards[0].Surface.ActualHeight;
            check(Math.Abs(firstBottom - bottom) < 1 && window.Top + window.ActualHeight > firstBottom + 50,
                "Adding an older card extends downward while the first card keeps its bottom anchor");
            var previousFirst = window.VisibleCards[0];
            double previousTop = window.Top;
            var newest = new TaskProgress("newest") { Body = "새 채팅입니다.\n조금 더 긴 새 말풍선입니다.", Project = "비서", Detail = "확인 중" };
            window.SetTasks(new[] { newest }.Concat(pair).ToArray()); window.UpdateLayout();
            double oldTop = window.Top + previousFirst.Surface.TranslatePoint(new System.Windows.Point(), (UIElement)window.Content).Y;
            check(ReferenceEquals(previousFirst, window.VisibleCards[1]) && Math.Abs(window.Top - previousTop) < 1
                && Math.Abs(oldTop - previousTop - window.VisibleCards[0].Surface.ActualHeight - 12) < 1,
                "A new chat reserves exactly its own height and gap while keeping the insertion slot's top in place");
            double stableTop = window.Top;
            pair[0].Body = "아래 말풍선이 길어집니다.\n다음 줄입니다.\n또 다음 줄입니다.";
            window.SetTasks(new[] { newest }.Concat(pair).ToArray()); window.UpdateLayout();
            check(Math.Abs(window.Top - stableTop) < 1, "Growing a lower card leaves the head-adjacent card in place when space permits");
            window.SetTasks(pair.Skip(1).ToArray()); window.UpdateLayout();
            check(Math.Abs(window.Top - stableTop) < 1, "Removing the top card promotes its successor into the empty top slot");
            pair[1].Body = "첫 번째 줄입니다.\n두 번째 줄입니다.\n세 번째 줄입니다.";
            window.SetTasks(pair.Skip(1).ToArray());
            owner.Top = workTop - 100;
            window.UpdateLayout();
            check(Math.Abs(window.Top - (workTop + 8)) < 1 && window.VisibleCards[0].Body.Text.Contains("세 번째"), "A bubble above the screen moves down without removing or scrolling its body");
            window.SetTasks(Array.Empty<TaskProgress>());
            check(!window.IsVisible, "No visible cards means no empty bubble window");
            long liveText = window.BeginAmbientStream();
            window.UpdateAmbientStream(liveText, "화면을 확인합니다.", "생각하는 중");
            var liveCard = window.VisibleCards.Single();
            check(liveCard.Detail.Text == "생각하는 중" && liveCard.Footer.Visibility == Visibility.Visible,
                "Public commentary reasoning uses the same WPF bubble with a visible thinking label");
            window.UpdateAmbientStream(liveText, "화면을 보고 있어요.");
            check(ReferenceEquals(liveCard, window.VisibleCards.Single()) && liveCard.Body.Text == "화면을 보고 있어요." && liveCard.Footer.Visibility == Visibility.Collapsed,
                "Commentary streaming updates the same real WPF card as text arrives");
            liveCard.Close.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            check(!window.UpdateAmbientStream(liveText, "늦은 글") && !window.IsVisible,
                "Closing a streaming commentary bubble permanently rejects later chunks from that request");
            long nextText = window.BeginAmbientStream();
            window.UpdateAmbientStream(nextText, "다음 한마디");
            window.DiscardAmbientStream(liveText);
            check(!window.UpdateAmbientStream(liveText, "이전 요청") && window.VisibleCards.Single().Body.Text == "다음 한마디",
                "A prior commentary request cannot overwrite a newer streaming request");
            window.ClearAmbient(); window.SetTasks(Array.Empty<TaskProgress>());
            check(!window.UpdateAmbientStream(nextText, "오래된 글"), "A new user prompt invalidates the previous ambient stream");
            long failed = window.BeginAmbientStream();
            window.UpdateAmbientStream(failed, "확인 중입니다.", "생각하는 중");
            window.DiscardAmbientStream(failed);
            check(!window.IsVisible && !window.UpdateAmbientStream(failed, "늦은 요약"),
                "Failed automatic commentary clears partial reasoning without reviving an obsolete stream");
        }
        finally { window?.Close(); owner.Close(); }
    }

    public static int Render()
    {
        Directory.CreateDirectory(AppStorage.DataDirectory);
        var canvas = new Canvas { Width = 900, Height = 940, Background = new SolidColorBrush(Color.FromRgb(227, 229, 232)) };
        var pet = new Image { Source = new PoseAssets(AppStorage.Root)["working"], Width = 390, Height = 770, Stretch = Stretch.Uniform };
        Canvas.SetLeft(pet, 445); Canvas.SetTop(pet, 115); canvas.Children.Add(pet);
        var rows = new StackPanel { Width = SpeechBubble.PreferredWidth };
        var examples = new[]
        {
            ("비서", CompletionExcerpt.Extract("**말풍선 3개 표시와 글꼴 변경을 적용했어요.**"), "응답 완료"),
            ("비서", "캐릭터의 구두 바닥선을 기준으로 자세별 높이를 확인하고 있습니다.", "dotnet build 실행 중"),
            ("", "화면 크기가 달라져도 세 말풍선이 겹치지 않는지 확인하고 있습니다.", "자체 검사 실행 중")
        };
        foreach (var (project, body, detail) in examples)
        {
            var card = new BubbleCard(() => { }); card.Update(project, body, detail); card.PointTail(true);
            card.Surface.Margin = new Thickness(0, 0, 0, 12); rows.Children.Add(card.Surface);
        }
        Canvas.SetLeft(rows, 65); Canvas.SetTop(rows, 55); canvas.Children.Add(rows);
        canvas.Measure(new Size(900, 940)); canvas.Arrange(new Rect(0, 0, 900, 940)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(900, 940, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppStorage.DataDirectory, "multi-bubble-preview.png")); encoder.Save(file);
        return 0;
    }
}
