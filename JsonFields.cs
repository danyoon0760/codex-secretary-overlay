using System.Text.Json;

namespace SecretaryOverlay;

internal static class JsonFields
{
    // Optional metadata is a string only; do not coerce objects or numbers into display text.
    public static string String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
