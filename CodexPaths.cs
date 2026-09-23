using System.IO;

namespace SecretaryOverlay;

internal static class CodexPaths
{
    // Resolve at the point of use so an environment override is never cached inconsistently.
    public static string Home => Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
}
