using System.Diagnostics;
using System.IO;
using System.Text;

namespace SecretaryOverlay;

internal static class CodexProcess
{
    public static string FindExecutable()
    {
        var custom = Environment.GetEnvironmentVariable("SECRETARY_CODEX_EXE");
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return custom;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var exe = Path.Combine(directory.Trim('"'), "codex.exe");
            if (File.Exists(exe)) return exe;
        }
        var desktopBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI", "Codex", "bin");
        if (Directory.Exists(desktopBin))
        {
            var desktopExe = Directory.EnumerateDirectories(desktopBin)
                .Select(directory => Path.Combine(directory, "codex.exe"))
                .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (desktopExe is not null) return desktopExe;
        }
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex");
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        var triple = arch == "arm64" ? "aarch64-pc-windows-msvc" : "x86_64-pc-windows-msvc";
        var candidates = new[]
        {
            Path.Combine(npm, "node_modules", "@openai", "codex-win32-" + arch, "vendor", triple, "bin", "codex.exe"),
            Path.Combine(npm, "vendor", triple, "bin", "codex.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Codex CLI를 찾지 못했어요. 설치 경로를 확인해 주세요.");
    }

    public static ProcessStartInfo CreateStartInfo(string folder)
    {
        var start = new ProcessStartInfo(FindExecutable())
        {
            WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        // Child calls must not inherit the desktop task identity or forward events into its pet.
        foreach (var key in start.Environment.Keys.Where(x => x.StartsWith("CODEX_THREAD", StringComparison.OrdinalIgnoreCase)
            || x.StartsWith("CODEX_INTERNAL", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        return start;
    }
}
