using System.Text;

namespace SecretaryOverlay;

internal static class PartialSummary
{
    // Only a top-level JSON string value is eligible. Never show braces, keys, or unfinished escapes.
    public static string Body(string json)
    {
        int position = 0;
        void White() { while (position < json.Length && char.IsWhiteSpace(json[position])) position++; }
        White(); if (position >= json.Length || json[position++] != '{') return "";
        while (position < json.Length)
        {
            White();
            if (!ReadString(json, ref position, out string key)) return "";
            White(); if (position >= json.Length || json[position++] != ':') return "";
            White();
            bool complete = ReadString(json, ref position, out string value);
            if (key == "body") return value.TrimEnd();
            if (!complete) return "";
            White(); if (position >= json.Length || json[position++] != ',') return "";
        }
        return "";
    }
    private static bool ReadString(string text, ref int position, out string value)
    {
        value = "";
        if (position >= text.Length || text[position++] != '"') return false;
        var result = new StringBuilder();
        while (position < text.Length)
        {
            char c = text[position++];
            if (c == '"') { value = result.ToString(); return true; }
            if (c == '\\')
            {
                if (position >= text.Length) break;
                char escape = text[position++];
                if (escape == 'u')
                {
                    if (position + 4 > text.Length) break;
                    if (!ushort.TryParse(text.AsSpan(position, 4), System.Globalization.NumberStyles.HexNumber, null, out ushort code)) return false;
                    result.Append((char)code); position += 4;
                }
                else if (escape is '"' or '\\' or '/') result.Append(escape);
                else if (escape is 'n' or 'r' or 't' or 'b' or 'f') result.Append(escape switch { 'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', _ => '\f' });
                else return false;
            }
            else if (c < ' ') return false;
            else result.Append(c);
        }
        if (result.Length > 0 && char.IsHighSurrogate(result[^1])) result.Length--;
        value = result.ToString();
        return false;
    }
}
