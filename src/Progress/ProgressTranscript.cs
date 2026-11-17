using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecretaryOverlay;

internal sealed class ProgressTranscript(string path, string session, string turn, DateTimeOffset notBefore)
{
    private const int MaxLineBytes = 256 * 1024;
    private const int MaxReadPerPoll = 2 * 1024 * 1024;
    private readonly ProgressParser parser = new(session, turn, notBefore);
    private readonly MemoryStream line = new();
    private long offset = -1;
    private bool discardLine;
    public string Path { get; } = path;
    public string Session { get; } = session;
    public string Turn { get; } = turn;
    public string Cwd => parser.Cwd;

    public static bool IsAllowedPath(string path, string session) => ResolvePath(path, session) is not null;

    public static string? ResolvePath(string path, string session)
    {
        if (!Guid.TryParse(session, out _)) return null;
        try
        {
            string home = CodexPaths.Home;
            string full = NormalizeWindowsPath(path);
            string sessions = System.IO.Path.Combine(NormalizeWindowsPath(home), "sessions") + System.IO.Path.DirectorySeparatorChar;
            return full.StartsWith(sessions, StringComparison.OrdinalIgnoreCase)
                && System.IO.Path.GetFileName(full).EndsWith("-" + session + ".jsonl", StringComparison.OrdinalIgnoreCase)
                ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    private static string NormalizeWindowsPath(string path)
    {
        // Codex hooks use extended-length paths (\\?\C:\...), while CODEX_HOME usually does not.
        // Strip only the filesystem namespace, then canonicalize before checking containment.
        path = path.Replace('/', '\\');
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute transcript path is required.");
        return System.IO.Path.GetFullPath(path);
    }

    public async Task<ProgressMessage?> ReadNewAsync(CancellationToken token) =>
        (await ReadUpdatesAsync(token).ConfigureAwait(false)).LastOrDefault(x => x.Kind != ProgressKind.Activity);

    public async Task<IReadOnlyList<ProgressMessage>> ReadUpdatesAsync(CancellationToken token)
    {
        using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (offset < 0)
        {
            // Never replay the whole conversation on startup; bootstrap from a bounded recent tail.
            offset = Math.Max(0, stream.Length - MaxLineBytes);
            discardLine = offset > 0;
        }
        if (stream.Length < offset)
        {
            offset = 0;
            line.SetLength(0);
            discardLine = false;
        }
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[64 * 1024];
        var updates = new List<ProgressMessage>();
        int budget = MaxReadPerPoll;
        while (budget > 0)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, budget)), token).ConfigureAwait(false);
            if (count == 0) break;
            offset += count;
            budget -= count;
            for (int i = 0; i < count; i++)
            {
                byte value = buffer[i];
                if (value == (byte)'\n')
                {
                    if (!discardLine && line.Length > 0)
                    {
                        var update = parser.Parse(line.GetBuffer().AsMemory(0, (int)line.Length));
                        if (update is not null) updates.Add(update);
                    }
                    line.SetLength(0);
                    discardLine = false;
                }
                else if (!discardLine)
                {
                    if (line.Length >= MaxLineBytes) { line.SetLength(0); discardLine = true; }
                    else line.WriteByte(value);
                }
            }
        }
        return updates;
    }
}
