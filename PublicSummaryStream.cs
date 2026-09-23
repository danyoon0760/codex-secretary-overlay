using System.Text.Json;

namespace SecretaryOverlay;

// Only the public summary channel is accepted. Raw reasoning/content is never read.
internal sealed class PublicSummaryStream(Action<string>? publish)
{
    private string item = "", text = "", delivered = "";
    private int index = -1;
    private readonly HashSet<string> finished = new();
    public int DeltaCount { get; private set; }

    public void Accept(string method, JsonElement data)
    {
        if (method == "item/reasoning/summaryTextDelta")
        {
            string id = JsonFields.String(data, "itemId");
            if (id.Length == 0 || finished.Contains(id) || !data.TryGetProperty("summaryIndex", out var part)
                || part.ValueKind != JsonValueKind.Number || !part.TryGetInt32(out int next) || next is < 0 or > 1000) return;
            if (item != id) { if (item.Length > 0) finished.Add(item); item = id; index = -1; text = ""; }
            if (next < index) return;
            if (next > index) { index = next; text = ""; }
            string delta = JsonFields.String(data, "delta");
            if (text.Length + delta.Length > 20000) return;
            text += delta; DeltaCount++; Publish(text);
        }
        else if (method == "item/completed" && data.TryGetProperty("item", out var entry)
            && JsonFields.String(entry, "type") == "reasoning")
        {
            string id = JsonFields.String(entry, "id");
            if (id.Length == 0 || finished.Contains(id)) return;
            // Some models only provide a completed public summary. Never fall back to content.
            if (entry.TryGetProperty("summary", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                var candidates = parts.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String)
                    .Select(p => p.GetString() ?? "").Where(p => Display(p).Length > 0).ToArray();
                string latest = candidates.LastOrDefault(p => ProgressDisplayText.ContainsKoreanContent(Display(p)))
                    ?? candidates.LastOrDefault() ?? "";
                Publish(latest);
            }
            finished.Add(id);
        }
    }

    private void Publish(string value)
    {
        if (value.Length > 20000) return;
        string clean = Display(value);
        if (clean.Length == 0 || clean == delivered) return;
        if (ProgressDisplayText.ContainsKoreanContent(delivered) && !ProgressDisplayText.ContainsKoreanContent(clean)) return;
        delivered = clean; publish?.Invoke(clean);
    }

    internal static string Display(string value)
    {
        value = ProgressDisplayText.Clean(value);
        if (value.Length <= 240) return value;
        int end = char.IsHighSurrogate(value[236]) ? 236 : 237;
        return value[..end].TrimEnd() + "…";
    }
}
