namespace SecretaryOverlay;

internal static class ProgressDisplayText
{
    public static bool ContainsKorean(string text) => text.Any(c => c is >= '\uAC00' and <= '\uD7A3'
        or >= '\u1100' and <= '\u11FF' or >= '\u3130' and <= '\u318F');

    public static bool ContainsKoreanContent(string text) => ContainsKorean(Clean(text));

    // Every body path shares one plain-text presentation policy.
    public static string Clean(string text) => BubbleMarkup.PlainMarkdown(BubbleMarkup.HideDirectives(text));

    public static string Finished(string state) => state switch
    {
        "Interrupt" => "작업을 멈췄어요.",
        "SessionEnd" => "수고하셨습니다.",
        _ => "이번 작업 끝났어요."
    };
}
