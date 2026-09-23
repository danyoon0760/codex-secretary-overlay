using System.IO;
using System.Text.Json;

namespace SecretaryOverlay;

internal static class AtomicJsonFile
{
    // Keep validation and normalization with each settings type. This class only commits bytes.
    public static bool Save<T>(string path, T value) => Save(path, value, out _);

    public static bool Save<T>(string path, T value, out Exception? error)
    {
        error = null;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, AppStorage.Json));
            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex;
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
