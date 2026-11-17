using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecretaryOverlay;

// The transcript is an internal Codex format. Unknown records fail closed.
// Public final answers are routed separately for completion excerpts, never as live progress.
// Raw reasoning/content/encrypted_content and tool output are never displayed.
internal sealed class ProgressParser(string session, string expectedTurn, DateTimeOffset notBefore)
{
    private string currentTurn = "";
    private readonly HashSet<string> seen = new();
    private readonly Queue<string> seenOrder = new();
    public string Cwd { get; private set; } = "";

    public ProgressMessage? Parse(ReadOnlyMemory<byte> line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return null;
            string kind = JsonFields.String(root, "type");
            if (kind == "session_meta") { Cwd = JsonFields.String(payload, "cwd"); return null; }
            if (kind == "turn_context" || (kind == "event_msg" && JsonFields.String(payload, "type") == "task_started"))
            { currentTurn = JsonFields.String(payload, "turn_id"); if (JsonFields.String(payload, "cwd").Length > 0) Cwd = JsonFields.String(payload, "cwd"); return null; }
            if (!DateTimeOffset.TryParse(JsonFields.String(root, "timestamp"), out var timestamp) || timestamp < notBefore) return null;

            JsonElement message;
            string turn;
            string text = "";
            var updateKind = ProgressKind.Commentary;
            bool completesTurn = false;
            if (kind == "event_msg" && JsonFields.String(payload, "type") is "item_completed" or "item_started")
            {
                string sourceThread = JsonFields.String(payload, "thread_id");
                if (sourceThread.Length > 0 && sourceThread != session) return null;
                turn = JsonFields.String(payload, "turn_id");
                if (!payload.TryGetProperty("item", out message)) return null;
                bool done = JsonFields.String(payload, "type") == "item_completed";
                switch (JsonFields.String(message, "type"))
                {
                    case "AgentMessage" when JsonFields.String(message, "phase") == "commentary":
                        text = Content(message, "content", "Text"); break;
                    case "AgentMessage" when done && JsonFields.String(message, "phase") == "final_answer":
                        updateKind = ProgressKind.FinalAnswer;
                        text = Content(message, "content", "Text"); break;
                    case "Reasoning":
                        updateKind = ProgressKind.Summary;
                        if (message.TryGetProperty("summary_text", out var summaries) && summaries.ValueKind == JsonValueKind.Array)
                            text = string.Join("\n", summaries.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
                        break;
                    case "CommandExecution":
                        updateKind = ProgressKind.Activity;
                        text = ActivityDescription.Command(message, done); break;
                    case "ImageView":
                        updateKind = ProgressKind.Activity;
                        text = ActivityDescription.Image(JsonFields.String(message, "path"), done); break;
                    case "FileChange":
                        updateKind = ProgressKind.Activity;
                        if (message.TryGetProperty("changes", out var changes)) text = ActivityDescription.FileChanges(changes, done);
                        break;
                    case "McpToolCall":
                        updateKind = ProgressKind.Activity;
                        text = message.TryGetProperty("arguments", out var arguments)
                            ? ActivityDescription.FromInput(arguments, JsonFields.String(message, "tool"), done)
                            : ActivityDescription.Tool(JsonFields.String(message, "tool"), done); break;
                    default: return null;
                }
            }
            else if (kind == "response_item" && JsonFields.String(payload, "type") == "message" && JsonFields.String(payload, "role") == "assistant")
            {
                message = payload;
                turn = currentTurn;
                if (JsonFields.String(message, "phase") == "final_answer") updateKind = ProgressKind.FinalAnswer;
                else if (JsonFields.String(message, "phase") != "commentary") return null;
                text = Content(message, "content", "output_text");
            }
            else if (kind == "event_msg" && JsonFields.String(payload, "type") == "task_complete")
            {
                string sourceThread = JsonFields.String(payload, "thread_id");
                if (sourceThread.Length > 0 && sourceThread != session) return null;
                message = payload;
                turn = JsonFields.String(payload, "turn_id");
                updateKind = ProgressKind.FinalAnswer;
                text = JsonFields.String(payload, "last_agent_message");
                completesTurn = true;
            }
            else if (kind == "response_item" && JsonFields.String(payload, "type") == "reasoning")
            {
                message = payload;
                turn = currentTurn;
                updateKind = ProgressKind.Summary;
                text = Content(payload, "summary", "summary_text");
            }
            else return null;

            if (turn.Length == 0 || (expectedTurn.Length > 0 && turn != expectedTurn)) return null;
            text = text.Trim();
            if ((text.Length == 0 && !completesTurn) || text.Length > 20_000) return null;
            // Codex writes both item_completed and response_item for the same visible message.
            string key = turn + ":" + updateKind + ":" + completesTurn + ":" + (updateKind == ProgressKind.Activity ? JsonFields.String(message, "id") : "") + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            if (!seen.Add(key)) return null;
            seenOrder.Enqueue(key);
            if (seenOrder.Count > 256) seen.Remove(seenOrder.Dequeue());
            return new(session, turn, JsonFields.String(message, "id"), text, timestamp, updateKind, completesTurn);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    private static string Content(JsonElement element, string property, string type) =>
        element.TryGetProperty(property, out var content) && content.ValueKind == JsonValueKind.Array
            ? string.Concat(content.EnumerateArray().Where(x => JsonFields.String(x, "type") == type).Select(x => JsonFields.String(x, "text"))) : "";
}
