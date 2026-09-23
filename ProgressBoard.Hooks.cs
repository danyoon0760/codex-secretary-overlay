
namespace SecretaryOverlay;

internal sealed partial class ProgressBoard
{
    public TaskProgress? Accept(PetEvent ev, string project)
    {
        if (ev.Preview || !Guid.TryParse(ev.Session, out _) || !StateEngine.Poses.ContainsKey(ev.Event)) return null;
        if (IsRetiredEvent(ev) || IsAmbiguousEvent(ev)) return null;
        var latest = Latest(ev.Session);
        var task = ev.Turn.Length > 0 ? Find(ev.Session, ev.Turn) : latest;
        if (ev.Event == "UserPromptSubmit")
        {
            // A duplicate hook for an identified question must not restart or reorder it.
            if (ev.Turn.Length > 0 && task?.PromptReceived == true) return task;
            if (ev.Turn.Length == 0 && task?.PromptReceived == true) task = null;
            if (task is null && latest is { Turn.Length: 0, PromptReceived: false }) task = latest;
        }
        else
        {
            // A later identified hook can bind a provisional question, but never replace a known turn.
            if (task is null && latest is { Turn.Length: 0 }) task = latest;
            if (task is null && latest is not null) return null;
        }
        if (task is null)
        {
            task = new TaskProgress(ev.Session) { QuestionNumber = (latest?.QuestionNumber ?? 0) + 1 };
            tasks.Insert(0, task);
        }
        if (ev.Event == "UserPromptSubmit") task.PromptReceived = true;
        if (ev.Turn.Length > 0) task.Turn = ev.Turn;
        if (ev.Cwd.Length > 0) task.Cwd = ev.Cwd;
        task.Project = project;
        if (task.State == "SessionEnd" && ev.Event is not ("UserPromptSubmit" or "SessionEnd")) return task;
        if (task.State == "PermissionRequest" && ev.Event is "PostToolUse" or "SubagentStart" or "SubagentStop") return task;
        if (!task.Active && ev.Event is not ("UserPromptSubmit" or "Stop" or "Interrupt" or "SessionEnd")) return task;
        task.Active = ev.Event is not ("Stop" or "Interrupt" or "SessionEnd");
        task.State = ev.Event;
        // A request that needs the user's action should enter the visible slots immediately.
        // Later questions may move it into overflow, where the approval count remains visible.
        if (ev.Event == "PermissionRequest" && !task.Dismissed && tasks.IndexOf(task) > 0)
        {
            tasks.Remove(task);
            tasks.Insert(0, task);
        }
        if (!task.Active) task.Body = ev.Event switch
        {
            "SessionEnd" => "수고하셨습니다.",
            "Stop" when task.Completion.Length > 0 => task.Completion,
            _ => ProgressDisplayText.Finished(ev.Event)
        };
        if (ev.Event == "SessionEnd") task.ResetSummary();
        if (ev.Event == "SessionEnd" && ReferenceEquals(task, latest)) EndOtherQuestions(task);
        if (ev.Event != "PostToolUse" || !task.DetailFromTranscript)
            task.Detail = HookDetail(ev);
        if (ev.Event != "PostToolUse") task.DetailFromTranscript = false;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        PruneCompleted();
        RefreshCompletions();
        return task;
    }

    private void EndOtherQuestions(TaskProgress latest)
    {
        foreach (var other in tasks.Where(t => t.Session == latest.Session && t != latest && t.Active))
        {
            other.Active = false;
            other.State = "SessionEnd";
            other.Body = "수고하셨습니다.";
            other.Detail = "채팅 종료";
            other.ResetSummary();
        }
    }
    private static string HookDetail(PetEvent ev) => ev.Detail.Length > 0 ? ev.Detail : ev.Event switch
        {
            "PreToolUse" => ActivityDescription.Tool(ev.Tool, false),
            "PostToolUse" => ActivityDescription.Tool(ev.Tool, true),
            "PermissionRequest" => "승인 기다리는 중",
            "Stop" => "응답 완료",
            "Interrupt" => "작업 중단",
            "SessionEnd" => "채팅 종료",
            "PreCompact" => "대화 맥락 정리 중",
            "PostCompact" => "대화 맥락 정리 완료",
            "SubagentStart" => "다른 에이전트에게 작업 전달 중",
            "SubagentStop" => "다른 에이전트의 결과 확인 중",
            _ => "생각하는 중"
        };
}
