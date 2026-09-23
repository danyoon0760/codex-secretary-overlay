using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecretaryOverlay;

internal static partial class ActivityDescription
{
    private const string JsLiteral = "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|`(?:\\\\.|[^`\\\\])*`";
    private static string? Wrapped(string code, bool done)
    {
        // Inspect literal tool arguments only. Never evaluate orchestration code or copy its output.
        var labels = new List<string>();
        foreach (Match match in Regex.Matches(code, JsLiteral + @"|//[^\r\n]*|/\*[\s\S]*?\*/|(?<tool>tools\.(?:exec_command|view_image|apply_patch))\s*\(", RegexOptions.Singleline))
        {
            if (!match.Groups["tool"].Success) continue;
            string tool = match.Groups["tool"].Value;
            string rest = code[(match.Index + match.Length)..];
            string pattern = tool.EndsWith("apply_patch") ? @"^\s*(?<value>" + JsLiteral + ")"
                : "^\\s*\\{\\s*(?:\"(?:cmd|path)\"|cmd|path)\\s*:\\s*(?<value>" + JsLiteral + ")";
            var argument = Regex.Match(rest, pattern, RegexOptions.Singleline);
            if (!argument.Success) continue;
            string literal = argument.Groups["value"].Value;
            string decoded;
            try
            {
                if (literal[0] == '"') decoded = JsonSerializer.Deserialize<string>(literal) ?? "";
                else
                {
                    // Interpolated template strings are not a known literal value.
                    if (literal[0] == '`' && literal.Contains("${")) continue;
                    decoded = literal[1..^1].Replace("\\'", "'").Replace("\\`", "`").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\\", "\\");
                }
            }
            catch (JsonException) { continue; }
            labels.Add(tool.EndsWith("apply_patch") ? Patch(decoded, done) : tool.EndsWith("view_image") ? Image(decoded, done) : Command(decoded, done));
        }
        return labels.Count == 0 ? null : done ? labels[^1] : labels[0];
    }
}
