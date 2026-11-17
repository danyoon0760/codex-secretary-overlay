using System.Text.Json;

namespace SecretaryOverlay;

internal sealed class AgentTextStream(string threadId, Action<string>? progress, Action<string>? reasoning = null)
{
    private readonly PublicSummaryStream summaries = new(reasoning);
    private string turn = "", item = "", text = "";
    private readonly HashSet<string> ignored = new();
    public string Text => text;
    public bool Completed { get; private set; }
    public int DeltaCount { get; private set; }
    public int SummaryDeltaCount => summaries.DeltaCount;

    public void Accept(string method, JsonElement data)
    {
        if (JsonFields.String(data, "threadId") != threadId || Completed) return;
        if (method == "turn/started" && data.TryGetProperty("turn", out var started))
            turn = JsonFields.String(started, "id");
        if (method == "turn/completed" && data.TryGetProperty("turn", out var finished))
        {
            if (turn.Length == 0 || JsonFields.String(finished, "id") != turn) return;
            if (JsonFields.String(finished, "status") != "completed") throw new InvalidOperationException("AI 응답이 완료되지 않았어요. 다시 시도해 주세요.");
            Completed = true; return;
        }
        if (turn.Length == 0 || JsonFields.String(data, "turnId") != turn) return;
        if (text.Length == 0) summaries.Accept(method, data);
        if (method is "item/started" or "item/completed" && data.TryGetProperty("item", out var entry))
        {
            if (JsonFields.String(entry, "type") != "agentMessage") return;
            string id = JsonFields.String(entry, "id");
            if (JsonFields.String(entry, "phase") == "commentary") { ignored.Add(id); return; }
            if (id.Length == 0) return;
            if (item != id) { item = id; text = ""; }
            if (method == "item/completed") Set(JsonFields.String(entry, "text"));
        }
        if (method == "item/agentMessage/delta")
        {
            string id = JsonFields.String(data, "itemId");
            if (id.Length == 0 || ignored.Contains(id)) return;
            if (item != id) { item = id; text = ""; }
            DeltaCount++;
            Set(text + JsonFields.String(data, "delta"));
        }
    }
    private void Set(string value)
    {
        if (value.Length > 24000) throw new InvalidOperationException("AI 응답이 너무 길어 중단했어요.");
        if (value == text) return;
        text = value; progress?.Invoke(value);
    }
}
