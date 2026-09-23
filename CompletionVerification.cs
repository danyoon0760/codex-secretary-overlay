using System.Text.Json;

namespace SecretaryOverlay;

internal static class CompletionVerification
{
    public static void Check(Action<bool, string> check)
    {
        const string result = "말풍선 3개 표시와 글꼴 변경을 적용했어요.";
        const string limitation = "다만 실제 화면 테스트는 실행하지 못했어요.";
        check(CompletionExcerpt.Extract(result) == result, "A concrete final sentence is used without rewriting");
        check(CompletionExcerpt.Extract("## 결과\n**" + result + "**\n\n- " + limitation) == result + " " + limitation,
            "Completion text cleans markdown and retains a qualification");
        check(CompletionExcerpt.Extract(result + " 다른 설정도 확인했어요. " + limitation) == result + " " + limitation,
            "A late limitation takes priority over an unqualified second sentence");
        check(CompletionExcerpt.Extract(result + "\n테스트가 실패했어요.\n배포도 아직 하지 못했어요.") == "테스트가 실패했어요. 배포도 아직 하지 못했어요.",
            "Multiple unresolved outcomes take priority over the success introduction");
        check(CompletionExcerpt.Extract("```csharp\n실패한 코드 예시입니다.\n```\n" + result) == result,
            "Code examples are not treated as completion outcomes");
        check(CompletionExcerpt.Extract("## 완료\n|파일|변경|\n```\n내용\n```") == "", "Unusable final answers preserve the fallback");
        check(CompletionExcerpt.Extract("이번 작업 끝났어요.\n" + result) == result, "Generic completion introductions yield to actual task content");
        check(CompletionExcerpt.Extract("[App.cs](C:/work/App.cs)를 수정했어요.") == "App.cs를 수정했어요.", "Filenames and link labels survive completion extraction");
        check(CompletionExcerpt.Extract(new string('가', 1100) + " 하지만 실행은 못했어요.").Contains("확인할 내용"),
            "An oversized sentence is not truncated into a misleading success claim");

        const string session = "11111111-1111-4111-8111-111111111111";
        var now = DateTimeOffset.UtcNow;
        var board = new ProgressBoard();
        var task = board.Accept(new("UserPromptSubmit", session, Turn: "one"), "비서")!;
        ProgressMessage Final(string text, string turn = "one") => new(session, turn, "final", text, now, ProgressKind.FinalAnswer);
        board.Apply(new(session, "one", "progress", "말풍선을 수정하고 있어요.", now));
        board.Apply(Final(result));
        check(task.Active && task.Body == "말풍선을 수정하고 있어요.", "A final answer arriving before Stop is cached without prematurely completing work");
        board.Accept(new("Stop", session, Turn: "one"), "비서");
        check(!task.Active && task.Body == result && task.Detail == "응답 완료", "Stop replaces progress with this turn's concrete result");
        check(!board.Apply(new(session, "one", "late", "늦은 진행 설명입니다.", now)) && task.Body == result,
            "Delayed commentary cannot overwrite the completion excerpt");
        board.Dismiss(task.Key);
        board.Apply(Final(result + " " + limitation));
        check(task.Dismissed && board.Visible.Length == 0 && task.Body.Contains(limitation), "Late final text does not reopen a dismissed bubble");
        board.Accept(new("SessionEnd", session, Turn: "one"), "비서");
        check(task.Body == "수고하셨습니다.", "Session end replaces the previous result with the requested farewell");
        task = board.Accept(new("UserPromptSubmit", session, Turn: "two"), "비서")!;
        check(task.Completion == "" && task.Active && !task.Dismissed, "A new question clears the previous turn's cached result");
        check(!board.Apply(Final("이전 작업 결과입니다.")) && task.FinalAnswer == "", "An ended older question rejects late answers without contaminating the new question");
        board.Accept(new("Stop", session, Turn: "two"), "비서");
        check(task.Body == ProgressDisplayText.Finished("Stop"), "Stop without a usable final answer has a readable fallback");
        board.Apply(Final(result, "two"));
        check(task.Body == result, "A final answer written after Stop replaces the temporary fallback");
        task = board.Accept(new("UserPromptSubmit", session, Turn: "three"), "비서")!;
        board.Accept(new("Interrupt", session, Turn: "three"), "비서");
        check(!board.Apply(Final(result, "three")) && task.Body == ProgressDisplayText.Finished("Interrupt"), "Late final text cannot turn an interrupted task into a completed one");

        var parser = new ProgressParser(session, "one", now);
        byte[] Item(string eventType, string turn = "one") => JsonSerializer.SerializeToUtf8Bytes(new
        {
            timestamp = now, type = "event_msg", payload = new { type = eventType, thread_id = session, turn_id = turn,
                item = new { type = "AgentMessage", phase = "final_answer", content = new[] { new { type = "Text", text = result } } } }
        });
        check(parser.Parse(Item("item_started")) is null, "Incomplete final-answer items are not harvested");
        check(parser.Parse(Item("item_completed", "old")) is null, "Final-answer parser enforces the expected turn");
        check(parser.Parse(Item("item_completed"))?.Kind == ProgressKind.FinalAnswer, "Completed public final-answer items are supported");
        parser.Parse(JsonSerializer.SerializeToUtf8Bytes(new { type = "turn_context", payload = new { turn_id = "one" } }));
        byte[] Response(string text) => JsonSerializer.SerializeToUtf8Bytes(new { timestamp = now, type = "response_item", payload = new
            { type = "message", role = "assistant", phase = "final_answer", content = new[] { new { type = "output_text", text } } } });
        check(parser.Parse(Response(result)) is null, "Duplicate final answers across transcript formats are ignored");
        check(parser.Parse(Response(limitation))?.Text == limitation, "Explicit response-item final answers retain their source text");
        var complete = JsonSerializer.SerializeToUtf8Bytes(new { timestamp = now, type = "event_msg", payload = new
            { type = "task_complete", turn_id = "one", last_agent_message = result + " " + limitation } });
        check(parser.Parse(complete)?.Kind == ProgressKind.FinalAnswer, "Task-complete final text provides a fallback when earlier records are outside the tail");
    }
}
