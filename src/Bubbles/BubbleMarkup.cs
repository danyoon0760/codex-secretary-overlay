using System.Text;
using System.Text.RegularExpressions;

namespace SecretaryOverlay;

// Display-only parsing: never execute directives, follow links, or modify source transcripts.
internal static class BubbleMarkup
{
    private static readonly string[] Markers = [":codex-", "::codex-", "::code-comment", "::created-thread"];

    public static string HideDirectives(string text)
    {
        var output = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            if (text[i] != ':') { output.Append(text[i++]); continue; }
            string? marker = Markers.FirstOrDefault(m => text.AsSpan(i).StartsWith(m, StringComparison.Ordinal)
                && (m.EndsWith('-') || i + m.Length == text.Length || !NameCharacter(text[i + m.Length])));
            if (marker is null)
            {
                // Hold an incomplete name until the next chunk identifies it.
                if (text.Length - i > 1 && Markers.Any(m => m.AsSpan().StartsWith(text.AsSpan(i), StringComparison.Ordinal))) break;
                output.Append(text[i++]);
                continue;
            }
            int end = i + marker.Length;
            if (marker.EndsWith('-')) while (end < text.Length && NameCharacter(text[end])) end++;
            int next = end;
            while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
            if (next < text.Length && text[next] == '[')
            {
                int close = Closing(text, next, '[', ']', false);
                if (close < 0) break;
                end = close + 1;
                next = end;
                while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
            }
            if (next < text.Length && text[next] == '{')
            {
                int close = Closing(text, next, '{', '}', true);
                if (close < 0) break;
                end = close + 1;
            }
            if (output.Length > 0 && !char.IsWhiteSpace(output[^1])
                && end < text.Length && char.IsLetterOrDigit(text[end])) output.Append(' ');
            i = end;
        }
        return output.ToString();
    }

    public static string PlainMarkdown(string text)
    {
        var lines = new List<string>();
        char fence = '\0';
        int fenceLength = 0;
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.Trim();
            int run = line.TakeWhile(c => c == line[0] && c is '`' or '~').Count();
            if (run >= 3)
            {
                if (fence == '\0') { fence = line[0]; fenceLength = run; }
                else if (line[0] == fence && run >= fenceLength && line[run..].Trim().Length == 0) fence = '\0';
                continue;
            }
            if (fence != '\0' || Regex.IsMatch(line, @"^\[[^\]]+\]:\s*\S")) continue;
            if (Regex.IsMatch(line, @"^(?:[-*_]\s*){3,}$")) continue;
            line = Regex.Replace(line, @"^#{1,6}(?:\s+|$)|\s+#+$", "");
            line = Regex.Replace(line, @"^(?:>\s*)+", "");
            line = Regex.Replace(line, @"^(?:[-*+]\s*\[[ xX]\]\s*|[-*+](?:\s+|$)|\d+[.)]\s+)", "");
            lines.Add(line);
        }
        text = Links(string.Join('\n', lines));
        text = Regex.Replace(text, @"(?<!\*)\*\*(?=\S)(.+?)(?<=\S)\*\*(?!\*)", "$1", RegexOptions.Singleline);
        text = Regex.Replace(text, @"(?<!\w)__(?=\S)(.+?)(?<=\S)__(?!\w)", "$1", RegexOptions.Singleline);
        text = Regex.Replace(text, @"(?<![\w*])\*(?=\S)([^*\n]+?)(?<=\S)\*(?!\*)", "$1");
        text = Regex.Replace(text, @"(?<!\w)_(?=\S)([^_\n]+?)(?<=\S)_(?!\w)", "$1");
        text = Regex.Replace(text, @"`+([^`\n]+)`+", "$1");
        text = Regex.Replace(text, @"`{2,}", ""); // Empty inline-code wrappers left by hidden directives.
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string Links(string text)
    {
        var output = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            int start = text[i] == '!' && i + 1 < text.Length && text[i + 1] == '[' ? i + 1 : i;
            if (text[start] != '[') { output.Append(text[i++]); continue; }
            int labelEnd = Closing(text, start, '[', ']', false);
            if (labelEnd < 0 || labelEnd + 1 >= text.Length || text[labelEnd + 1] is not ('(' or '['))
            { output.Append(text[i++]); continue; }
            int destination = labelEnd + 1;
            char opener = text[destination];
            int end = Closing(text, destination, opener, opener == '(' ? ')' : ']', false);
            output.Append(text.AsSpan(start + 1, labelEnd - start - 1));
            // Withhold incomplete destinations, including local paths, as chunks arrive.
            if (end < 0) break;
            i = end + 1;
        }
        return output.ToString();
    }

    private static bool NameCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';

    private static int Closing(string text, int start, char open, char close, bool quoted)
    {
        int depth = 0;
        char quote = '\0';
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\') { i++; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (quoted && c is '"' or '\'') { quote = c; continue; }
            if (c == open) depth++;
            else if (c == close && --depth == 0) return i;
        }
        return -1;
    }
}
