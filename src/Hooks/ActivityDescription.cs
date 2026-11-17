using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecretaryOverlay;

internal static partial class ActivityDescription
{
    public static string OneLine(string value, int max = 160)
    {
        string text = Regex.Replace(value, @"\s+", " ").Trim();
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    public static string Tool(string tool, bool done) => tool.ToLowerInvariant() switch
    {
        var n when n.Contains("apply_patch") => done ? "파일 수정 완료" : "파일 수정 중",
        var n when n.Contains("exec") || n.Contains("shell") => done ? "명령 실행 완료" : "명령 실행 중",
        var n when n.Contains("search") => done ? "검색 결과 확인 중" : "검색 중",
        var n when n.Contains("view_image") => done ? "이미지 확인함" : "이미지 확인 중",
        "" => done ? "결과 확인 중" : "작업 중",
        _ => OneLine(tool.Split(new[] { '.', '/' }).Last() + (done ? " 완료" : " 실행 중"), 100)
    };

    public static string FromHook(JsonElement hook, string tool, bool done)
    {
        if (!hook.TryGetProperty("tool_input", out var input)) return Tool(tool, done);
        if (input.ValueKind == JsonValueKind.String)
        {
            var text = input.GetString() ?? "";
            if (tool.Contains("apply_patch", StringComparison.OrdinalIgnoreCase)) return Patch(text, done);
            if (Wrapped(text, done) is { } wrapped) return wrapped;
            try { using var parsed = JsonDocument.Parse(text); return FromInput(parsed.RootElement, tool, done); }
            catch (JsonException) { return Tool(tool, done); }
        }
        return FromInput(input, tool, done);
    }

    public static string FromInput(JsonElement input, string tool, bool done)
    {
        if (input.ValueKind == JsonValueKind.String)
        {
            string text = input.GetString() ?? "";
            try { using var parsed = JsonDocument.Parse(text); return FromInput(parsed.RootElement, tool, done); }
            catch (JsonException) { return Wrapped(text, done) ?? Tool(tool, done); }
        }
        if (input.ValueKind != JsonValueKind.Object) return Tool(tool, done);
        string code = JsonFields.String(input, "code");
        if (code.Length > 0 && Wrapped(code, done) is { } wrapped) return wrapped;
        string file = JsonFields.String(input, "file_path");
        if (file.Length > 0 && (tool.Equals("Edit", StringComparison.OrdinalIgnoreCase) || tool.Equals("Write", StringComparison.OrdinalIgnoreCase)))
        {
            string before = JsonFields.String(input, "old_string");
            string after = JsonFields.String(input, "new_string");
            if (after.Length == 0) after = JsonFields.String(input, "content");
            return FileLine(file, 1, ContentLines(after), ContentLines(before), done);
        }
        if (tool.Contains("view_image", StringComparison.OrdinalIgnoreCase))
            return Image(JsonFields.String(input, "path"), done);
        string patch = JsonFields.String(input, "patch");
        if (patch.Length == 0) patch = JsonFields.String(input, "input");
        if (tool.Contains("apply_patch", StringComparison.OrdinalIgnoreCase) && patch.Length > 0) return Patch(patch, done);
        string command = JsonFields.String(input, "cmd");
        if (command.Length == 0) command = JsonFields.String(input, "command");
        if (command.Length > 0) return Command(command, done);
        string title = JsonFields.String(input, "title");
        if (title.Length > 0) return OneLine(title);
        return Tool(tool, done);
    }

    public static string Command(string command, bool done)
    {
        string label = done ? "명령 실행 완료" : "명령 실행 중";
        return command.Length == 0 ? label : label + " · " + CommandText(command);
    }

    // Preserve useful arguments but redact explicit credential values before both display and tooltip.
    private static string CommandText(string value)
    {
        value = Regex.Replace(value, "(?i)((?:--?|\\b)(?:api[-_]?key|access[-_]?token|auth[-_]?token|token|password|passwd|secret)\\b[\\\"']?\\s*(?:=|:)?\\s*)(?:\"[^\"]*\"|'[^']*'|[^\\s;&|,}]+)", "$1[비공개]");
        value = Regex.Replace(value, @"(?i)(Bearer\s+)[A-Za-z0-9._~+/=-]+", "$1[비공개]");
        return OneLine(value, 4096);
    }

    public static string Command(JsonElement item, bool done)
    {
        string command = "";
        if (item.TryGetProperty("command", out var value))
        {
            if (value.ValueKind == JsonValueKind.String) command = value.GetString() ?? "";
            else if (value.ValueKind == JsonValueKind.Array)
            {
                var args = value.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString() ?? "").ToArray();
                // Shell invocation records wrap the user's command in [shell, -Command/-c, script].
                string shell = args.Length > 0 ? Path.GetFileNameWithoutExtension(args[0].Replace('/', '\\')) : "";
                int flag = Array.FindIndex(args, a => a.Equals("-Command", StringComparison.OrdinalIgnoreCase) || a is "-c" or "-lc");
                command = shell.ToLowerInvariant() is "powershell" or "pwsh" or "bash" or "sh" or "zsh" && flag >= 0 && flag + 1 < args.Length
                    ? string.Join(" ", args.Skip(flag + 1)) : string.Join(" ", args);
            }
        }
        string status = JsonFields.String(item, "status");
        string text = Command(command, done && status is not ("inProgress" or "in_progress" or "running"));
        return done && JsonFields.String(item, "status") == "failed" ? text.Replace("명령 실행 완료", "명령 실행 실패") : text;
    }

    public static string Image(string path, bool done) => (done ? "이미지 확인함" : "이미지 확인 중")
        + (path.Length > 0 ? " · " + OneLine(Path.GetFileName(path.Replace('/', '\\')), 4096) : "");

    public static string Patch(string patch, bool done)
    {
        var files = Regex.Matches(patch, @"(?m)^\*\*\* (?:Update|Add|Delete) File: (.+)\r?$");
        if (files.Count == 0) return done ? "파일 수정 완료" : "파일 수정 중";
        var (added, removed) = ChangedLines(patch);
        return FileLine(files[0].Groups[1].Value.Trim(), files.Count, added, removed, done);
    }

    private static string FileLine(string path, int count, int added, int removed, bool done) =>
        OneLine($"{(done ? "편집함" : "편집 중")} · {Path.GetFileName(path.Replace('/', '\\'))}{(count > 1 ? $" 외 {count - 1}개" : "")} +{added} -{removed}", 4096);

    private static int ContentLines(string text) => text.Length == 0 ? 0 : text.TrimEnd('\n').Split('\n').Length;

    private static (int Added, int Removed) ChangedLines(string diff)
    {
        int added = 0, removed = 0;
        foreach (string line in diff.Split('\n'))
        {
            if (line.StartsWith('+') && !line.StartsWith("+++")) added++;
            if (line.StartsWith('-') && !line.StartsWith("---")) removed++;
        }
        return (added, removed);
    }

    public static string FileChanges(JsonElement changes, bool done)
    {
        string first = "";
        int count = 0, added = 0, removed = 0;
        void Read(string path, JsonElement change)
        {
            if (first.Length == 0) first = path;
            count++;
            string kind = JsonFields.String(change, "type");
            string diff = JsonFields.String(change, "unified_diff");
            if (diff.Length == 0) diff = JsonFields.String(change, "diff");
            if (kind is "add" or "delete")
            {
                string content = JsonFields.String(change, "content");
                int lines = ContentLines(content);
                if (kind == "add") added += lines; else removed += lines;
            }
            else
            {
                var delta = ChangedLines(diff);
                added += delta.Added;
                removed += delta.Removed;
            }
        }
        if (changes.ValueKind == JsonValueKind.Object)
            foreach (var p in changes.EnumerateObject()) Read(p.Name, p.Value);
        else if (changes.ValueKind == JsonValueKind.Array)
            foreach (var change in changes.EnumerateArray()) Read(JsonFields.String(change, "path"), change);
        return count == 0 ? "파일 수정 중" : FileLine(first, count, added, removed, done);
    }
}
