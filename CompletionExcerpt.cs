using System.Text.RegularExpressions;

namespace SecretaryOverlay;

// Extract source sentences; never generate or infer a successful outcome.
internal static class CompletionExcerpt
{
    private static readonly Regex Caveat = new(
        @"못|않|실패|미완료|아직|다만|하지만|필요|남아|남았|불가|중단|미실행|미검증|검증 전|제한|\b(failed|unable|not tested|not run|however|pending)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static List<string> Sentences(string answer)
    {
        var sentences = new List<string>();
        char fence = '\0';
        foreach (string raw in BubbleMarkup.HideDirectives(answer).Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("```") || line.StartsWith("~~~"))
            {
                if (fence == '\0') fence = line[0];
                else if (fence == line[0]) fence = '\0';
                continue;
            }
            if (fence != '\0' || line.StartsWith('#') || line.StartsWith('|') || line.StartsWith('>')) continue;
            line = Regex.Replace(line, @"^(?:[-*+]\s+|\d+[.)]\s+|>\s*)", "");
            line = ProgressDisplayText.Clean(line);
            foreach (string part in Regex.Split(line, @"(?<=[.!?。])\s+"))
            {
                string sentence = part.Trim();
                if (sentence.Length < 8 || !ProgressDisplayText.ContainsKorean(sentence)) continue;
                if (Regex.IsMatch(sentence, @"^(?:네[,.!]?\s*)?(?:완료했어요|완료했습니다|작업을 마쳤어요|이번 작업 끝났어요|이번 응답을 마쳤어요)[.!]?$")) continue;
                if (!sentences.Contains(sentence)) sentences.Add(sentence);
            }
        }
        return sentences;
    }

    public static string Extract(string answer)
    {
        var sentences = Sentences(answer);
        if (sentences.Count == 0) return "";
        // Search the entire answer so a later qualification survives a short positive opening.
        var cautions = sentences.Where(s => Caveat.IsMatch(s) && !Regex.IsMatch(s, @"같은 표현|표현이 있|표현을|예를 들|문장을 우선|규칙으로")).ToArray();
        var selected = cautions.Length > 1 ? cautions.Take(2).ToArray()
            : new[] { sentences[0] }.Concat(cautions).Distinct().ToArray();
        string excerpt = string.Join(" ", selected);
        // Never cut a sentence before a negation or limitation. Long answers use an honest fallback.
        return excerpt.Length <= 1000 ? excerpt : "응답을 마쳤어요. 자세한 결과와 확인할 내용은 채팅에서 볼 수 있어요.";
    }

    public static string Detail(string answer) => Sentences(answer).FirstOrDefault(s => s.Length <= 65
        && Regex.IsMatch(s, @"검사|테스트|빌드|검증")
        && Regex.IsMatch(s, @"통과|성공|실패|못했|못 했|미실행|미검증")
        && !Regex.IsMatch(s, @"예를 들|같은 표현|표현이 있|표현을|문장을|규칙")) ?? "응답 완료";
}
