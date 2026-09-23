using System.Text.Json;

namespace SecretaryOverlay;

internal static class IntegrationBoundaryVerification
{
    public static void Check(Action<bool, string> check)
    {
        CheckGeneration(check);
        CheckHooks(check);
    }

    private static void CheckGeneration(Action<bool, string> check)
    {
        var sent = new List<JsonElement>();
        var updates = new List<string>();
        var session = NewSession(updates.Add);
        Task Send(object message) { sent.Add(JsonSerializer.SerializeToElement(message)); return Task.CompletedTask; }
        void Accept(CodexGenerationSession target, string json)
        {
            using var doc = JsonDocument.Parse(json);
            target.AcceptAsync(doc.RootElement, Send).GetAwaiter().GetResult();
        }
        bool Rejects(CodexGenerationSession target, string json)
        {
            try { Accept(target, json); return false; }
            catch (InvalidOperationException) { return true; }
        }

        Accept(session, """{"id":1,"result":{}}""");
        check(sent.Count == 2 && JsonFields.String(sent[0], "method") == "initialized"
            && JsonFields.String(sent[1], "method") == "thread/start",
            "AI handshake acknowledges initialization before requesting a temporary thread");
        Accept(session, """{"id":2,"result":{"thread":{"id":"thread-a","ephemeral":true}}}""");
        check(sent.Count == 3 && JsonFields.String(sent[2], "method") == "turn/start"
            && JsonFields.String(sent[2].GetProperty("params"), "threadId") == "thread-a",
            "AI generation starts only after the temporary thread has been confirmed");
        Accept(session, """{"method":"turn/started","params":{"threadId":"thread-a","turn":{"id":"turn-a"}}}""");
        Accept(session, """{"method":"item/agentMessage/delta","params":{"threadId":"other","turnId":"turn-a","itemId":"answer","delta":"잘못된 응답"}}""");
        check(updates.Count == 0 && session.Text.Length == 0, "AI protocol ignores notifications from another thread");
        Accept(session, """{"method":"item/agentMessage/delta","params":{"threadId":"thread-a","turnId":"turn-a","itemId":"answer","delta":"설정을 "}}""");
        Accept(session, """{"method":"item/agentMessage/delta","params":{"threadId":"thread-a","turnId":"turn-a","itemId":"answer","delta":"변경했어요."}}""");
        check(updates.SequenceEqual(new[] { "설정을 ", "설정을 변경했어요." }) && !session.Completed,
            "AI transport extraction preserves incremental text and does not complete on a text delta");
        Accept(session, """{"method":"turn/completed","params":{"threadId":"thread-a","turn":{"id":"turn-a","status":"completed"}}}""");
        check(session.Completed && session.Text == "설정을 변경했어요.", "AI completion retains the assembled answer");

        check(Rejects(NewSession(), """{"id":2,"result":{"thread":{"id":"persistent","ephemeral":false}}}"""),
            "AI protocol refuses a persistent thread before sending user content");
        check(Rejects(NewSession(), """{"id":1,"error":{"code":-1}}"""), "AI connection errors cannot be treated as successful initialization");
        check(Rejects(NewSession(), """{"id":90,"method":"item/commandExecution/requestApproval","params":{}}""")
            && sent[^1].GetProperty("id").GetInt32() == 90 && sent[^1].GetProperty("error").GetProperty("code").GetInt32() == -32601,
            "AI tool or approval requests receive a refusal and stop generation");
        var empty = NewSession();
        Accept(empty, """{"id":2,"result":{"thread":{"id":"thread-a","ephemeral":true}}}""");
        Accept(empty, """{"method":"turn/started","params":{"threadId":"thread-a","turn":{"id":"turn-a"}}}""");
        check(Rejects(empty, """{"method":"turn/completed","params":{"threadId":"thread-a","turn":{"id":"turn-a","status":"completed"}}}"""),
            "AI empty completion remains a recoverable error after protocol extraction");
    }

    private static CodexGenerationSession NewSession(Action<string>? progress = null) =>
        new("C:\\temporary-generation", [], "요약할 내용", ModelProfile.Default, null, progress, null);

    private static void CheckHooks(Action<bool, string> check)
    {
        using var input = JsonDocument.Parse("""
            {"hook_event_name":"Stop","session_id":"session","turn_id":"turn","transcript_path":"records.jsonl",
             "cwd":"project","last_assistant_message":"private answer","tool_response":"private tool output"}
            """);
        var hook = HookEventParser.Parse(input.RootElement, true);
        check(hook is { Event: "Stop", Session: "session", Turn: "turn", Transcript: "records.jsonl", Cwd: "project", Preview: true }
            && !JsonSerializer.Serialize(hook).Contains("private"),
            "Extracted hook parsing preserves routing metadata without forwarding response bodies");
        using var unknown = JsonDocument.Parse("""{"hook_event_name":"unknown"}""");
        check(HookEventParser.Parse(unknown.RootElement, false) is null, "Unknown hook types cannot enter the transport");
        using var fields = JsonDocument.Parse("""{"text":"value","number":12,"object":{},"null":null}""");
        check(JsonFields.String(fields.RootElement, "text") == "value"
            && new[] { "number", "object", "null", "missing" }.All(name => JsonFields.String(fields.RootElement, name) == ""),
            "Shared JSON field reading does not coerce missing or non-string metadata into visible content");
    }
}
