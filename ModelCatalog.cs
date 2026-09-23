using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecretaryOverlay;

internal sealed record ModelOption(string Id, string Label, string[] Efforts, bool Images);

internal static class ModelCatalog
{
    public static readonly IReadOnlyDictionary<string, string> EffortNames = new Dictionary<string, string>
    {
        ["none"] = "없음", ["minimal"] = "최소", ["low"] = "낮음", ["medium"] = "보통",
        ["high"] = "높음", ["xhigh"] = "매우 높음", ["max"] = "최대", ["ultra"] = "울트라"
    };
    public static string Label(string id, string displayName = "")
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return Regex.Replace(ActivityDescription.OneLine(displayName, 100), @"^(GPT-\d+(?:\.\d+)*)-", "$1 ", RegexOptions.IgnoreCase);
        // Preserve the version without maintaining a fixed list of known generations or codenames.
        var match = Regex.Match(id, @"^gpt-(\d+(?:\.\d+)*)(?:-(.+))?$", RegexOptions.IgnoreCase);
        if (!match.Success) return id;
        string suffix = match.Groups[2].Value;
        return "GPT-" + match.Groups[1].Value + (suffix.Length > 0 ? " " + char.ToUpperInvariant(suffix[0]) + suffix[1..] : "");
    }
    public static string Describe(ModelProfile profile) => (Read().FirstOrDefault(m => m.Id == profile.Model)?.Label ?? Label(profile.Model))
        + " · " + EffortNames.GetValueOrDefault(profile.Reasoning, profile.Reasoning);

    public static ModelOption[] Read()
    {
        try
        {
            string home = CodexPaths.Home;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "models_cache.json")));
            var models = Parse(doc.RootElement);
            if (models.Length > 0) return models;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
        // Previously verified local default; never invent additional models when metadata is unavailable.
        return [new("gpt-5.6-luna", Label("gpt-5.6-luna"), ["low", "medium", "high", "xhigh", "max"], true)];
    }

    internal static ModelOption[] Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return [];
        var result = new List<ModelOption>();
        foreach (var model in models.EnumerateArray())
        {
            if (JsonFields.String(model, "visibility") != "list") continue;
            string id = JsonFields.String(model, "slug");
            if (ModelProfile.Validated(new(id)) is var valid && valid.Model != id) continue;
            if (!model.TryGetProperty("supported_reasoning_levels", out var levels) || levels.ValueKind != JsonValueKind.Array) continue;
            var efforts = levels.EnumerateArray().Select(x => JsonFields.String(x, "effort")).Where(EffortNames.ContainsKey).Distinct().ToArray();
            if (efforts.Length == 0) continue;
            bool images = model.TryGetProperty("input_modalities", out var modalities) && modalities.ValueKind == JsonValueKind.Array
                && modalities.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "image");
            if (result.All(x => x.Id != id)) result.Add(new(id, Label(id, JsonFields.String(model, "display_name")), efforts, images));
        }
        return result.ToArray();
    }
}
