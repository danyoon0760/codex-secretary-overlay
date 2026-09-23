using System.IO;
using System.Text;
using System.Text.Json;

namespace SecretaryOverlay;

internal static class ProgressVerification
{
    private const string Session = "11111111-1111-4111-8111-111111111111";
    private const string Turn = "22222222-2222-4222-8222-222222222222";

    private static byte[] Message(DateTimeOffset at, string text, string phase = "commentary", string turn = Turn, string session = Session, string type = "AgentMessage") =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            timestamp = at, type = "event_msg",
            payload = new { type = "item_completed", thread_id = session, turn_id = turn, item = new
            { type, id = "message-" + text.Length, phase, content = new[] { new { type = "Text", text } } } }
        });

    public static void Check(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var parser = new ProgressParser(Session, Turn, now);
        const string message = "지정한 글꼴은 앱에 함께 넣어 표시하겠습니다. English stays intact.";
        var result = parser.Parse(Message(now, message));
        check(result?.Text == message && result.Session == Session && result.Turn == Turn, "Public progress text is relayed verbatim with its task identity");
        check(parser.Parse(Message(now, message)) is null, "Repeated transcript records do not repeat the bubble");
        check(parser.Parse(Message(now, "Hidden reasoning", "analysis")) is null, "Internal reasoning is never displayed");
        check(parser.Parse(Message(now, "Final response", "final_answer"))?.Kind == ProgressKind.FinalAnswer, "Public final answers are routed separately from progress");
        check(parser.Parse(Message(now, "Unknown phase", "")) is null, "Messages with no public commentary phase fail closed");
        var tool = parser.Parse(Message(now, "Tool output", type: "CommandExecution"));
        check(tool?.Kind == ProgressKind.Activity && !tool.Text.Contains("Tool output"), "Only a derived activity label is read from tools, never their output");
        check(parser.Parse(Message(now, "Another task", session: "other")) is null, "Other task records cannot leak into the focused pet");
        check(parser.Parse(Message(now, "An old turn", turn: "previous")) is null, "Late messages from an older turn are rejected");
        check(parser.Parse(Message(now.AddMinutes(-1), "Old history")) is null, "Attaching never replays old conversation history");
        check(parser.Parse(Encoding.UTF8.GetBytes("{broken")) is null, "Malformed transcript entries are harmless");
        parser.Parse(JsonSerializer.SerializeToUtf8Bytes(new { type = "turn_context", payload = new { turn_id = Turn } }));
        byte[] Response(string role, string text) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            timestamp = now, type = "response_item", payload = new
            { type = "message", role, phase = "commentary", content = new[] { new { type = "output_text", text } } }
        });
        check(parser.Parse(Response("assistant", message)) is null, "item_completed and response_item copies are deduplicated");
        check(parser.Parse(Response("assistant", "새 진행 설명"))?.Text == "새 진행 설명", "Explicit public response_item commentary is supported");
        check(parser.Parse(Response("user", "User text")) is null, "User messages are never relayed as assistant progress");
        check(parser.Parse(Message(now, new string('x', 20_001))) is null, "Unexpected oversized messages are bounded");
        var legacy = JsonSerializer.Deserialize<Layout>("{\"Left\":0,\"Top\":0,\"Height\":560}");
        check(legacy?.ShowProgress == true, "Existing settings enable progress without migration");
        var hook = new PetEvent("PreToolUse", Session, "test", Transcript: "C:/example.jsonl", Turn: Turn);
        check(JsonSerializer.Deserialize<PetEvent>(JsonSerializer.Serialize(hook)) == hook, "Hook routing metadata survives named-pipe serialization");
        check(!ProgressTranscript.IsAllowedPath("C:/Windows/win.ini", Session), "Transcript reader rejects unrelated files");
        check(!ProgressTranscript.IsAllowedPath("relative.jsonl", Session), "Transcript reader rejects relative paths");
        CheckPaths(check);
        CheckTailAsync(check, now).GetAwaiter().GetResult();
    }

    private static void CheckPaths(Action<bool, string> check)
    {
        var originalHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            const string home = @"C:\ProgressPathTest\.codex";
            Environment.SetEnvironmentVariable("CODEX_HOME", home);
            string filename = "rollout-2026-09-22T02-00-00-" + Session + ".jsonl";
            string path = Path.Combine(home, "sessions", "2026", "09", "22", filename);
            check(ProgressTranscript.ResolvePath(path, Session) == path, "Regular Codex session paths resolve");
            check(ProgressTranscript.ResolvePath(@"\\?\" + path, Session) == path, "Extended Windows hook paths resolve to the same transcript");
            check(ProgressTranscript.ResolvePath(path.Replace('\\', '/'), Session) == path, "Slash variants resolve to the same transcript");
            check(!ProgressTranscript.IsAllowedPath(path, Turn), "A valid path cannot select another session's transcript");
            check(!ProgressTranscript.IsAllowedPath(Path.Combine(home, "sessions-other", filename), Session), "Lookalike session directories are rejected");
            check(!ProgressTranscript.IsAllowedPath(@"\\?\" + Path.Combine(home, "sessions", "..", filename), Session), "Extended paths cannot escape the sessions directory");
            check(!ProgressTranscript.IsAllowedPath(@"\\?\relative\" + filename, Session), "Extended prefix does not make a relative path eligible");
            Environment.SetEnvironmentVariable("CODEX_HOME", @"\\?\" + home);
            check(ProgressTranscript.ResolvePath(path, Session) == path, "An extended CODEX_HOME works with a regular hook path");
            const string networkHome = @"\\server\share\.codex";
            Environment.SetEnvironmentVariable("CODEX_HOME", networkHome);
            string networkPath = Path.Combine(networkHome, "sessions", filename);
            check(ProgressTranscript.ResolvePath(@"\\?\UNC\" + networkPath[2..], Session) == networkPath, "Extended UNC paths keep the configured home boundary");
        }
        finally { Environment.SetEnvironmentVariable("CODEX_HOME", originalHome); }
    }

    private static async Task CheckTailAsync(Action<bool, string> check, DateTimeOffset now)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SecretaryProgressTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "test.jsonl");
        try
        {
            await File.WriteAllTextAsync(file, Encoding.UTF8.GetString(Message(now.AddMinutes(-1), "history")) + "\n");
            var tail = new ProgressTranscript(file, Session, Turn, now);
            check(await tail.ReadNewAsync(CancellationToken.None) is null, "Startup tail discards historical commentary");
            var next = Message(now, "한글 스트리밍 경계 확인");
            int split = next.Length / 2;
            using (var write = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                await write.WriteAsync(next.AsMemory(0, split));
            check(await tail.ReadNewAsync(CancellationToken.None) is null, "Partial JSON lines wait for completion");
            using (var write = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            { await write.WriteAsync(next.AsMemory(split)); await write.WriteAsync(new byte[] { 10 }); }
            check((await tail.ReadNewAsync(CancellationToken.None))?.Text == "한글 스트리밍 경계 확인", "Split UTF-8 transcript reads preserve complete Korean text");
            check(await tail.ReadNewAsync(CancellationToken.None) is null, "Unchanged transcript is not replayed");
            await File.AppendAllTextAsync(file, new string('x', 300_000) + "\n" + Encoding.UTF8.GetString(Message(now, "큰 도구 출력 뒤 진행 설명")) + "\n");
            check((await tail.ReadNewAsync(CancellationToken.None))?.Text == "큰 도구 출력 뒤 진행 설명", "Oversized tool records are skipped without losing the next progress message");
            await File.WriteAllTextAsync(file, Encoding.UTF8.GetString(Message(now, "파일 재작성 후 진행 설명")) + "\n");
            check((await tail.ReadNewAsync(CancellationToken.None))?.Text == "파일 재작성 후 진행 설명", "Transcript truncation resumes safely");
        }
        finally { Directory.Delete(folder, true); }
    }
}
