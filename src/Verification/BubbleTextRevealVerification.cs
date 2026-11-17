using System.Globalization;

namespace SecretaryOverlay;

internal static class BubbleTextRevealVerification
{
    public static void Check(Action<bool, string> check)
    {
        var reveal = new BubbleTextReveal();
        check(reveal.Text == "" && reveal.Target == "" && reveal.IsComplete, "An empty text reveal starts complete without a pending frame");
        reveal.SetTarget("abcdefghij", 1000, true);
        check(reveal.Text == "" && reveal.Target == "abcdefghij" && !reveal.IsComplete,
            "A complete incoming sentence starts from empty without exposing its final text immediately");
        string early = reveal.Advance(1031);
        check(early == "ab" && !reveal.IsComplete, "Text appears in small grapheme batches at the configured reading rate");
        reveal.SetTarget("abcdefghij", 1050, true);
        check(reveal.Advance(1080) == "abcd", "Repeated delivery of the same target does not restart or delay the reveal");
        check(reveal.Advance(1030) == "abcd" && reveal.Advance(1080) == "abcd",
            "Repeated or earlier timestamps cannot move text backwards or advance it twice");
        check(reveal.Advance(2000) == "abcdefghij" && reveal.IsComplete,
            "A later frame completes all remaining text even after a long pause");

        var appending = new BubbleTextReveal();
        appending.SetTarget("abcdefghij", 0, true);
        appending.Advance(50);
        string prefix = appending.Text;
        appending.SetTarget("abcdefghijklmnop", 50, true);
        check(appending.Text == prefix && appending.Advance(80) == "abcd",
            "Appending a streamed chunk preserves the visible prefix and fractional progress without replaying earlier text");
        appending.Advance(1000);
        appending.SetTarget("abcdefghijklmnopQR", 1000, true);
        check(appending.Text == "abcdefghijklmnop" && !appending.IsComplete && appending.Advance(1100) == "abcdefghijklmnopQR",
            "A late additional chunk animates only its new suffix even after the previous target completed");
        appending.SetTarget("새로운 문장입니다.", 1200, true);
        check(appending.Text == "" && appending.Target == "새로운 문장입니다." && !appending.IsComplete,
            "Replacing a sentence clears its old text before revealing the new content");
        appending.Advance(1230);
        appending.SetTarget("새로운 문장입니다.", 1230, false);
        check(appending.Text == appending.Target && appending.IsComplete,
            "Disabling animation completes an already-running reveal immediately, including an unchanged target");
        appending.SetTarget("즉시 바꾼 문장", 1300, false);
        check(appending.Text == "즉시 바꾼 문장" && appending.IsComplete, "A new target is also immediate when animation is disabled");
        appending.SetTarget("", 1400, true);
        check(appending.Text == "" && appending.Target == "" && appending.IsComplete, "Clearing a target leaves no old text or unfinished reveal");

        var rapid = new BubbleTextReveal();
        bool madeProgressDuringArrival = false;
        for (int i = 1; i <= 200; i++)
        {
            rapid.SetTarget(new string('가', i), i, true);
            madeProgressDuringArrival |= rapid.Text.Length > 0 && i < 200;
        }
        check(madeProgressDuringArrival && rapid.Advance(1400).Length == 200 && rapid.IsComplete,
            "Very frequent input chunks neither starve the display nor leave more than 1.2 seconds of final backlog");
        foreach (int length in new[] { 80, 4000 })
        {
            var bounded = new BubbleTextReveal();
            bounded.SetTarget(new string('나', length), 0, true);
            bounded.Advance(length <= 80 ? 900 : 1200);
            check(bounded.Text.Length == length && bounded.IsComplete,
                length <= 80 ? "A normal sentence finishes within 900 ms" : "An unusually long sentence finishes within 1.2 seconds");
        }

        const string unicode = "가👩🏽‍💻e\u0301👨‍👩‍👧‍👦🇰🇷끝\r\n다음";
        var validPrefixes = StringInfo.ParseCombiningCharacters(unicode).Select(index => unicode[..index]).Append(unicode).ToHashSet();
        var graphemes = new BubbleTextReveal();
        graphemes.SetTarget(unicode, 0, true);
        bool wholeClusters = true;
        for (int time = 0; time <= 1200; time++) wholeClusters &= validPrefixes.Contains(graphemes.Advance(time));
        check(wholeClusters && graphemes.Text == unicode,
            "Every frame ends at a complete Unicode grapheme, including emoji, combining accents, flags and CRLF");
        var combining = new BubbleTextReveal();
        combining.SetTarget("e", 0, false);
        combining.SetTarget("e\u0301x", 10, true);
        check(combining.Text == "e\u0301" && combining.Advance(100) == "e\u0301x",
            "An appended combining mark extends its visible cluster without temporarily splitting the new grapheme");
        combining.SetTarget("👩", 200, false);
        combining.SetTarget("👩‍💻곁", 210, true);
        check(combining.Text == "👩‍💻" && combining.Advance(300) == "👩‍💻곁",
            "A streamed emoji joiner extends the visible emoji as one whole cluster");
    }
}
