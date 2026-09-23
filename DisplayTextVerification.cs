using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SecretaryOverlay;

internal static class DisplayTextVerification
{
    private const string Directive = ":codex-annotation{index=\"1\"}";
    private const string ReportedBody = "말풍선이 ‘자리 확보 → 등장 → 글 표시’ 순서로 나오도록 구현하겠습니다. 작업 중에는 유지하고 완료 후에는 위치와 관계없이 1분 뒤 닫히게 하며, 채팅 열기 버튼과 종료 인사도 추가하겠습니다. 4개 이상일 때의 표시 대기 방식까지 반영하고, 먼저 복구용 백업을 만들겠습니다.";

    public static void Check(Action<bool, string> check)
    {
        ReadableBubbleVerification.Check(check);
        string input = ReportedBody + " " + Directive;
        check(ProgressDisplayText.Clean(input) == ReportedBody,
            "The reported raw annotation is hidden while preserving the reported sentence");
        check(ProgressDisplayText.Clean("첫째 " + Directive + " 둘째 :codex-annotation{index=\"12\"}") == "첫째 둘째",
            "Multiple annotations disappear without joining neighboring words");
        check(ProgressDisplayText.Clean("**완료** :codex-annotation\n{ index = '2' }") == "완료",
            "Annotations with wrapping and spaced attributes are hidden beside markdown");
        check(ProgressDisplayText.Clean(ProgressDisplayText.Clean(input)) == ProgressDisplayText.Clean(input),
            "Repeated display cleanup remains stable");
        const string literal = "C:\\work\\my_file.cs, https://example.test:443/path, a ** b, 상태: 완료";
        check(ProgressDisplayText.Clean(literal) == literal,
            "Annotation cleanup preserves normal paths, URLs, colons and arithmetic");
        check(ProgressDisplayText.Clean("설명 :ordinary-name") == "설명 :ordinary-name",
            "Similar ordinary text is not interpreted as an annotation");
        check(ProgressDisplayText.Clean("본문 :codex-annotation{index=\"bad\"}") == "본문",
            "An invalid annotation cannot expose its internal attributes or invent a number");

        var card = new BubbleCard(() => { });
        bool chunksClean = true;
        for (int i = 2; i < Directive.Length; i++)
        {
            card.Update("비서", ReportedBody + " " + Directive[..i], "생각하는 중", "Manage multiple tasks");
            chunksClean &= card.Body.Text == ReportedBody;
        }
        check(chunksClean, "Every incomplete annotation chunk is withheld from the real bubble body");
        var reveal = new BubbleTextReveal();
        reveal.SetTarget(ReportedBody, 0, false);
        bool prefixRetained = true;
        for (int i = 1; i <= Directive.Length; i++)
        {
            long time = i * 1000;
            reveal.SetTarget(ProgressDisplayText.Clean(ReportedBody + " " + Directive[..i]), time, true);
            prefixRetained &= reveal.Text.StartsWith(ReportedBody, StringComparison.Ordinal);
            reveal.Advance(time + 900);
        }
        check(prefixRetained && reveal.Text == ReportedBody,
            "Every stream boundary, including the initial colon, retains the already-visible body");
        card.Update("비서", input, "생각하는 중", "Manage multiple tasks");
        card.PreparePresentation(1, 0, false);
        check(card.DisplayedText == ReportedBody,
            "The real bubble displays only the body without references or raw directive syntax");
        card.Update("비서", input + " 다음 설명입니다.", "생각하는 중");
        check(card.Body.Text == ReportedBody + " 다음 설명입니다.",
            "Text arriving after the reference continues normally");
        check(PublicSummaryStream.Display("검토 중입니다. " + Directive) == "검토 중입니다.",
            "Public summary display uses the same reference-hiding policy");
        check(!ProgressDisplayText.ContainsKoreanContent("Checking the layout. " + Directive)
            && ProgressDisplayText.ContainsKoreanContent("화면을 확인합니다. " + Directive),
            "Hidden directives do not change the detected progress language");

        const string session = "22222222-2222-4222-8222-222222222222";
        var board = new ProgressBoard();
        var task = board.Accept(new("UserPromptSubmit", session, Turn: "one"), "비서")!;
        board.Apply(new(session, "one", "first", "Checking layout. " + Directive, DateTimeOffset.UtcNow));
        board.Apply(new(session, "one", "second", "Running tests.", DateTimeOffset.UtcNow));
        check(task.Body == "Running tests." && !task.HasKoreanProgress,
            "An English update with a reference does not prevent the next English update");

        var summaries = new List<string>();
        var stream = new PublicSummaryStream(summaries.Add);
        stream.Accept("item/reasoning/summaryTextDelta", JsonSerializer.SerializeToElement(new
            { itemId = "one", summaryIndex = 0, delta = "Checking layout. " + Directive }));
        stream.Accept("item/reasoning/summaryTextDelta", JsonSerializer.SerializeToElement(new
            { itemId = "two", summaryIndex = 0, delta = "Running tests." }));
        check(summaries.SequenceEqual(new[] { "Checking layout.", "Running tests." }),
            "Public summary streaming keeps updating after a hidden reference");
        bool rejected = false;
        try { CompletionSummaryClient.Parse(JsonSerializer.Serialize(new { body = "Done. " + Directive, detail = "응답 완료" })); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "An English completion cannot pass Korean validation by attaching a reference label");
        check(CompletionExcerpt.Extract("말풍선 표시를 수정했습니다. " + Directive) == "말풍선 표시를 수정했습니다."
            && CompletionExcerpt.Detail("검사 657개 통과 " + Directive) == "검사 657개 통과",
            "Result extraction still omits reference metadata without changing the reported outcome");
    }

    public static int Render()
    {
        var card = new BubbleCard(() => { });
        card.Update("비서", "**말풍선 표시를 정리했습니다.** " + Directive
            + "\n[설정 파일](C:/private/folder/settings.json)에서 내용을 확인할 수 있습니다.\n"
            + string.Join('\n', ReadableBubbleVerification.Directives), "검사 완료", "말풍선 개선");
        card.SetNavigation("11111111-1111-4111-8111-111111111111", true, _ => { });
        var surface = card.Surface;
        surface.Width = SpeechBubble.PreferredWidth;
        surface.Measure(new System.Windows.Size(surface.Width, double.PositiveInfinity));
        surface.Arrange(new Rect(0, 0, surface.Width, surface.DesiredSize.Height));
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
            (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(AppStorage.DataDirectory);
        using var file = File.Create(Path.Combine(AppStorage.DataDirectory, "annotation-display-preview.png"));
        encoder.Save(file);
        return 0;
    }
}
