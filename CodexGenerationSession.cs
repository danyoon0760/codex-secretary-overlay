using System.Text.Json;

namespace SecretaryOverlay;

// Protocol state for one temporary text-generation session, independent of process I/O.
internal sealed class CodexGenerationSession(string folder, IReadOnlyList<string> images, string prompt,
    ModelProfile profile, JsonElement? schema, Action<string>? progress, Action<string>? reasoning)
{
    private AgentTextStream? stream;
    public bool Completed => stream?.Completed == true;
    public string Text => stream?.Text ?? "";

    public async Task AcceptAsync(JsonElement root, Func<object, Task> send)
    {
        if (root.TryGetProperty("id", out var id))
        {
            if (root.TryGetProperty("method", out _))
            {
                await send(new { id = id.Clone(), error = new { code = -32601, message = "Tools and approvals are unavailable in this text-only client." } });
                throw new InvalidOperationException("AI가 지원하지 않는 작업을 요청해 응답을 중단했어요.");
            }
            if (root.TryGetProperty("error", out _)) throw new InvalidOperationException("AI 연결에 실패했어요. Codex 로그인과 선택한 모델을 확인해 주세요.");
            if (!root.TryGetProperty("result", out var result)) return;
            if (id.TryGetInt32(out int request) && request == 1)
            {
                await send(CodexRequestMessages.Initialized());
                await send(CodexRequestMessages.ThreadStart(folder, profile));
            }
            else if (request == 2)
            {
                var thread = result.GetProperty("thread");
                string threadId = JsonFields.String(thread, "id");
                if (threadId.Length == 0 || !thread.TryGetProperty("ephemeral", out var ephemeral) || !ephemeral.GetBoolean())
                    throw new InvalidOperationException("임시 AI 연결을 만들지 못했어요.");
                stream = new(threadId, progress, reasoning);
                await send(CodexRequestMessages.TurnStart(threadId, prompt, images, profile, schema));
            }
        }
        else if (root.TryGetProperty("params", out var data))
        {
            stream?.Accept(JsonFields.String(root, "method"), data);
            if (Completed && string.IsNullOrWhiteSpace(Text)) throw new InvalidOperationException("AI 응답이 비어 있어요.");
        }
    }
}
