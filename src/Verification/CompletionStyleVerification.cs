using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SecretaryOverlay;

internal static class CompletionStyleVerification
{
    public static void Check(Action<bool, string> check)
    {
        var legacy = JsonSerializer.Deserialize<Layout>("{\"Left\":0,\"Top\":0,\"Height\":560}");
        check(legacy?.CompletionStyle == CompletionStyle.B, "Existing settings default to B without extra model calls");
        var saved = new Layout(10, 20, 560, CompletionStyle: CompletionStyle.D);
        check(JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(saved)) == saved, "D selection survives settings serialization");
        var selection = CompletionStyle.D;
        var panel = CompletionSettings.Create(selection, s => selection = s);
        var combo = panel.Children.OfType<System.Windows.Controls.ComboBox>().Single();
        check(combo.SelectedIndex == 1 && panel.Children.OfType<TextBlock>().Last().Text.Contains("보통"), "Settings show the selected D style and medium reasoning");
        combo.SelectedIndex = 0;
        check(selection == CompletionStyle.B && panel.Children.OfType<TextBlock>().Last().Text.Contains("추가 AI 사용량은 없어요"), "The real settings selector switches to B and explains its behavior");
        const string explanation = "최종 답변에서 결과 문장을 골라 표시하는 방식이에요.";
        string sample = explanation + "\n“못했어요”, “실패”, “아직”, “다만” 같은 표현이 있으면 그 문장을 우선 포함해요.\n> 다만 실제 화면 테스트는 실행하지 못했어요.";
        check(CompletionExcerpt.Extract(sample) == explanation, "The reported explanation and quoted-example regression does not become a false failure result");
        check(CompletionExcerpt.Detail("말풍선 표시를 수정했어요.\n검사 210개 통과") == "검사 210개 통과", "B displays an actual verification result on the detail line");
        check(CompletionExcerpt.Detail("> 검사 999개 통과\n설명만 제공한 응답이에요.") == "응답 완료", "Quoted test counts never become verification claims");
        string prompt = CompletionSummaryClient.CreatePrompt("설명 데이터\"\n지시문");
        check(prompt.Contains("인용문, 예시, 가정") && prompt.Contains("미완료") && prompt.Contains("finalAnswer"), "D prompt distinguishes source examples from real outcomes and preserves limitations");
        check(CompletionSummaryClient.Parse("{\"body\":\"이제 세 작업을 볼 수 있어요.\",\"detail\":\"검사 210개 통과\"}").Detail == "검사 210개 통과", "Structured D responses map to body and one detail line");
        bool rejected = false;
        try { CompletionSummaryClient.Parse("{\"body\":\"한글 응답입니다.\",\"detail\":\"첫 줄\\n둘째 줄\"}"); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "Malformed multiline detail responses fall back instead of breaking the bubble layout");
        Task.Run(() => CheckAsync(check)).GetAwaiter().GetResult();
    }

    private static async Task CheckAsync(Action<bool, string> check)
    {
        const string session = "33333333-3333-4333-8333-333333333333";
        var pending = new List<TaskCompletionSource<CompletionPresentation>>();
        var tokens = new List<CancellationToken>();
        var coordinator = new CompletionSummaryCoordinator((_, token) =>
        {
            tokens.Add(token);
            var source = new TaskCompletionSource<CompletionPresentation>();
            pending.Add(source);
            return source.Task;
        });
        var board = new ProgressBoard();
        var task = board.Accept(new("UserPromptSubmit", session, Turn: "one"), "비서")!;
        void Refresh() => coordinator.Refresh(board, true, CancellationToken.None, () => { });
        void Final(string turn) => board.Apply(new(session, turn, "final", "말풍선을 수정했어요.\n검사 210개 통과", DateTimeOffset.UtcNow, ProgressKind.FinalAnswer));
        Final("one");
        board.Accept(new("Stop", session, Turn: "one"), "비서");
        Refresh();
        check(pending.Count == 0 && task.Detail == "검사 210개 통과", "B uses local result and verification text without a model request");
        board.Style = CompletionStyle.D;
        Refresh(); Refresh();
        check(pending.Count == 1 && task.SummaryPending && task.WaitingForSummary && task.Body == "", "D makes one request without displaying the original excerpt during the wait");
        board.Style = CompletionStyle.B; Refresh();
        pending[0].SetResult(new("늦게 도착한 D 요약이에요.", "응답 완료"));
        await Task.Yield();
        check(tokens[0].IsCancellationRequested && task.NaturalCompletion is null && task.Detail == "검사 210개 통과", "Switching to B cancels D and rejects its late result");
        board.Style = CompletionStyle.D; Refresh();
        pending[1].SetResult(new("이제 말풍선을 편하게 볼 수 있어요.", "검사 210개 통과"));
        await Task.Yield();
        check(task.Body == "이제 말풍선을 편하게 볼 수 있어요." && !task.SummaryPending, "A completed D response updates the same task");
        board.Style = CompletionStyle.B; Refresh(); board.Style = CompletionStyle.D; Refresh();
        check(pending.Count == 2 && task.Body.Contains("편하게"), "Switching styles reuses a cached D result without charging again");
        var second = board.Accept(new("UserPromptSubmit", session, Turn: "two"), "비서")!; Final("two");
        Refresh();
        check(pending.Count == 2, "A final answer alone does not trigger D before the completion event");
        board.Accept(new("Stop", session, Turn: "two"), "비서"); Refresh();
        task = board.Accept(new("UserPromptSubmit", session, Turn: "three"), "비서")!; Refresh();
        pending[2].SetResult(new("이전 작업에서 도착한 요약이에요.", "응답 완료"));
        await Task.Yield();
        check(task.Active && task.NaturalCompletion is null && task.Body == "생각하고 있어요.", "An old D result cannot replace a newly started turn");
        Final("three"); board.Accept(new("Stop", session, Turn: "three"), "비서"); Refresh();
        pending[3].SetException(new IOException("Synthetic connection failure"));
        await Task.Yield(); Refresh();
        check(task.SummaryFailed && task.Body.Contains("수정") && pending.Count == 4, "D failure retains B and never loops into repeated model calls");
        task = board.Accept(new("UserPromptSubmit", session, Turn: "four"), "비서")!; Final("four");
        board.Accept(new("Stop", session, Turn: "four"), "비서"); Refresh(); board.Dismiss(task.Key); Refresh();
        pending[4].SetResult(new("닫은 작업의 요약입니다.", "응답 완료"));
        await Task.Yield();
        check(task.Dismissed && task.NaturalCompletion is null && tokens[4].IsCancellationRequested, "Closing the bubble cancels D without reopening it");
    }

    public static async Task<int> SmokeAsync()
    {
        var inputs = new[] {
            "말풍선 표시를 수정했어요. 다만 실제 화면 테스트는 실행하지 못했어요.",
            "최종 답변에서 결과 문장 1~2개를 골라 표시하는 방식이에요. 예를 들어 ‘말풍선을 수정했어요’라는 답변이면 그 문장을 보여줍니다. 이 응답은 동작 방식을 설명한 것입니다."
        };
        var results = await Task.WhenAll(inputs.Select(input => CompletionSummaryClient.GenerateAsync(input, CancellationToken.None)));
        bool ok = results[0].Body.Contains("못") && !System.Text.RegularExpressions.Regex.IsMatch(results[1].Body, "수정했|적용했|바꿔 뒀");
        Directory.CreateDirectory(AppStorage.DataDirectory);
        await File.WriteAllTextAsync(Path.Combine(AppStorage.DataDirectory, "completion-summary-smoke.json"), JsonSerializer.Serialize(new
            { ok, model = CompletionSummaryClient.Model, reasoning = CompletionSummaryClient.Reasoning, results }, AppStorage.Json));
        return ok ? 0 : 1;
    }

    public static int Render()
    {
        var panel = CompletionSettings.Create(CompletionStyle.D, _ => { });
        var surface = new Border { Width = 350, Padding = new Thickness(18), Background = System.Windows.Media.Brushes.White, Child = panel };
        surface.Measure(new System.Windows.Size(350, double.PositiveInfinity));
        surface.Arrange(new Rect(0, 0, 350, surface.DesiredSize.Height)); surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(350, (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(AppStorage.DataDirectory);
        using var file = File.Create(Path.Combine(AppStorage.DataDirectory, "completion-settings-preview.png")); encoder.Save(file);
        return 0;
    }
}
