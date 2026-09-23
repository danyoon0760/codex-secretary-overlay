using System.IO;

namespace SecretaryOverlay;

internal static class TemporaryFiles
{
    private static readonly TimeSpan MinimumAge = TimeSpan.FromDays(1);

    public static int CleanupStale(string? root = null, DateTime? nowUtc = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "SecretaryOverlay");
        if (!Directory.Exists(root)) return 0;
        int removed = 0;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                try
                {
                    var info = new DirectoryInfo(directory);
                    string name = info.Name.StartsWith("completion-", StringComparison.OrdinalIgnoreCase)
                        ? info.Name["completion-".Length..] : info.Name;
                    if (!Guid.TryParseExact(name, "N", out _) || info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                        || (nowUtc ?? DateTime.UtcNow) - info.LastWriteTimeUtc < MinimumAge) continue;
                    Directory.Delete(directory, true);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return removed;
    }
}
