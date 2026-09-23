using System.IO;
using System.Text.Json;

namespace SecretaryOverlay;

// Codex's local title index is appendable: the last valid name for an id wins.
// Never infer a title from message contents or make an AI request for one.
internal sealed class ChatTitles(string? indexPath = null)
{
    private Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
    private long nextRead;

    public string Resolve(string session) => names.GetValueOrDefault(session, "");

    public async Task RefreshAsync(CancellationToken token)
    {
        if (Environment.TickCount64 < nextRead) return;
        nextRead = Environment.TickCount64 + 5_000;
        try
        {
            var fresh = await Task.Run(() =>
            {
                string home = CodexPaths.Home;
                using var file = new FileStream(indexPath ?? Path.Combine(home, "session_index.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (file.Length > 16 * 1024 * 1024) return null;
                using var reader = new StreamReader(file);
                return Read(reader);
            }, token);
            if (!token.IsCancellationRequested && fresh is not null) names = fresh;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static Dictionary<string, string> Read(TextReader reader)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 65536) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                string id = JsonFields.String(doc.RootElement, "id");
                string name = JsonFields.String(doc.RootElement, "thread_name");
                if (Guid.TryParse(id, out _) && !string.IsNullOrWhiteSpace(name))
                    result[id] = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
            }
            catch (JsonException) { } // A partially written last line must not hide earlier titles.
        }
        return result;
    }
}
