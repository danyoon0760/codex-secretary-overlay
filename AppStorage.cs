using System.IO;
using System.Text.Json;

namespace SecretaryOverlay;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum CompletionStyle { B, D }
public sealed record Layout(double Left, double Top, double Height, bool Motion = true, bool AutoCommentary = true, bool ShowProgress = true,
    CompletionStyle CompletionStyle = CompletionStyle.B, ModelProfile? AutomaticModel = null, ModelProfile? ManualModel = null, ModelProfile? CompletionModel = null,
    bool ShowLabels = true, BubbleAppearance? BubbleAppearance = null, int CommentaryIntervalMinutes = 20);

internal static class AppStorage
{
    private const long MaxEventLogBytes = 1024 * 1024;
    private const long MaxErrorLogBytes = 1024 * 1024;
    public static readonly string Root = AppContext.BaseDirectory;
    public static readonly string LegacyDataDirectory = Path.Combine(Root, "data");
    public static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecretaryOverlay");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Initialize() => Initialize(DataDirectory, LegacyDataDirectory);

    internal static void Initialize(string destination, string legacy)
    {
        Directory.CreateDirectory(destination);
        // Preserve existing portable settings. Never overwrite a newer user-data copy.
        foreach (var name in new[] { "layout.json", "personalization.json", "personalization-draft.json",
            "events.jsonl", "errors.log" })
        {
            string oldPath = Path.Combine(legacy, name);
            string newPath = Path.Combine(destination, name);
            if (File.Exists(oldPath) && !File.Exists(newPath)) File.Copy(oldPath, newPath);
        }
    }

    public static Layout? LoadLayout()
    {
        try
        {
            var path = Path.Combine(DataDirectory, "layout.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<Layout>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool SaveLayout(Layout layout) => AtomicJsonFile.Save(
        Path.Combine(DataDirectory, "layout.json"), layout, out _);

    public static bool WriteStatus<T>(T status) => AtomicJsonFile.Save(
        Path.Combine(DataDirectory, "status.json"), status, out _);

    public static void AppendEvent(PetEvent ev, DateTimeOffset? at)
    {
        try
        {
            var path = Path.Combine(DataDirectory, "events.jsonl");
            Directory.CreateDirectory(DataDirectory);
            if (File.Exists(path) && new FileInfo(path).Length > MaxEventLogBytes)
                File.Move(path, path + ".previous", true);

            File.AppendAllText(path,
                JsonSerializer.Serialize(new { at, @event = ev.Event, session = ev.Session, tool = ev.Tool }) + "\n");
        }
        catch
        {
            // Event logging is best effort, just like layout and status persistence.
        }
    }

    public static void LogError(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var path = Path.Combine(DataDirectory, "errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > MaxErrorLogBytes)
                File.Move(path, path + ".previous", true);
            // Stack frames and error codes identify the failure without saving prompts or screen text.
            var details = ex.GetType().FullName + " (0x" + ex.HResult.ToString("X8") + ")" +
                (ex.InnerException is null ? "" : " inner=" + ex.InnerException.GetType().FullName) +
                Environment.NewLine + ex.StackTrace;
            File.AppendAllText(path, DateTimeOffset.Now + " " +
                details[..Math.Min(details.Length, 8192)] + Environment.NewLine);
        }
        catch
        {
            // Logging failures cannot be reported through this same log.
        }
    }
}
