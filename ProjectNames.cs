using System.IO;
using System.Text.Json;

namespace SecretaryOverlay;

internal sealed class ProjectNames
{
    private JsonDocument? state;
    private long nextRead;
    private readonly Dictionary<(string Session, string Cwd), string> resolved = new();
    public string Resolve(string session, string cwd)
    {
        if (state is null) return "";
        var key = (session, cwd);
        if (!resolved.TryGetValue(key, out var name))
            resolved[key] = name = Resolve(state.RootElement, session, cwd);
        return name;
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        if (Environment.TickCount64 < nextRead) return;
        nextRead = Environment.TickCount64 + 10_000;
        try
        {
            var fresh = await Task.Run(() =>
            {
                string home = CodexPaths.Home;
                using var file = new FileStream(Path.Combine(home, ".codex-global-state.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonDocument.Parse(file);
            }, token);
            if (token.IsCancellationRequested) { fresh.Dispose(); return; }
            state?.Dispose();
            state = fresh;
            resolved.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    internal static string Resolve(JsonElement root, string session, string cwd)
    {
        if (!root.TryGetProperty("local-projects", out var projects) || projects.ValueKind != JsonValueKind.Object) return "";
        if (root.TryGetProperty("thread-project-assignments", out var assignments) && assignments.ValueKind == JsonValueKind.Object
            && assignments.TryGetProperty(session, out var assignment))
        {
            string id = JsonFields.String(assignment, "projectId");
            if (id.Length > 0 && projects.TryGetProperty(id, out var project)) return Clean(JsonFields.String(project, "name"));
        }
        if (root.TryGetProperty("projectless-thread-ids", out var projectless) && projectless.ValueKind == JsonValueKind.Array
            && projectless.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == session)) return "";
        string best = "", name = "";
        foreach (var project in projects.EnumerateObject())
        {
            if (!project.Value.TryGetProperty("rootPaths", out var paths) || paths.ValueKind != JsonValueKind.Array) continue;
            foreach (var value in paths.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String) continue;
                string path = (value.GetString() ?? "").TrimEnd('\\', '/');
                if (path.Length <= best.Length) continue;
                if (cwd.Equals(path, StringComparison.OrdinalIgnoreCase) || cwd.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase))
                { best = path; name = JsonFields.String(project.Value, "name"); }
            }
        }
        return Clean(name);
    }

    private static string Clean(string text) => ActivityDescription.OneLine(text, 100);
}
