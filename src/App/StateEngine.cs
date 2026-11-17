using System.Collections.ObjectModel;

namespace SecretaryOverlay;

public record PetEvent(string Event, string Session = "", string Tool = "", bool Preview = false,
    string Transcript = "", string Turn = "", string Cwd = "", string Detail = "");
public record Pose(string File, string Label, int HoldMs = 0, string? After = null, int Priority = 0);

public sealed class StateEngine
{
    private const int MinimumPoseDurationMs = 800;
    private const int ImmediatePriority = 8;

    public static IReadOnlyDictionary<string, Pose> Poses { get; } = new ReadOnlyDictionary<string, Pose>(new Dictionary<string, Pose>
    {
        ["Idle"] = new("idle", "기다리고 있어요"),
        ["SessionStart"] = new("start", "안녕하세요", 1900, "Idle", 2),
        ["UserPromptSubmit"] = new("thinking", "생각하고 있어요", 0, null, 3),
        ["PreToolUse"] = new("working", "작업하고 있어요", 0, null, 2),
        ["PostToolUse"] = new("review", "결과를 확인해요", 1200, "UserPromptSubmit", 1),
        ["PermissionRequest"] = new("permission", "확인이 필요해요", 0, null, 10),
        ["Stop"] = new("done", "이번 작업을 마쳤어요", 10000, "Idle", 8),
        ["Interrupt"] = new("interrupt", "멈췄어요", 1800, "Idle", 12),
        ["PreCompact"] = new("compact", "기억을 정리해요", 0, null, 7),
        ["PostCompact"] = new("ready", "다시 준비됐어요", 1500, "UserPromptSubmit", 7),
        ["SubagentStart"] = new("delegate", "동료에게 부탁해요", 1600, "UserPromptSubmit", 5),
        ["SubagentStop"] = new("receive", "동료의 결과를 받아요", 1600, "UserPromptSubmit", 5),
        ["SessionEnd"] = new("start", "수고하셨습니다.", 2000, "Idle", 11),
    });

    public string Current { get; private set; } = "Idle";
    public string Session { get; private set; } = "";
    public string Turn { get; private set; } = "";
    public bool Preview { get; private set; }
    public long ChangedAt { get; private set; }
    private long returnAt;
    private string? pending;
    private bool terminal;
    private bool working;
    public event Action<string>? Changed;

    public bool Accept(PetEvent ev, long now)
    {
        if (!Poses.ContainsKey(ev.Event) || ev.Event == "Idle") return false;
        if (ev.Preview)
        {
            Preview = true;
            pending = null;
            Switch(ev.Event, now);
            return true;
        }
        // Check identity before clearing previews, pending poses or terminal state.
        // A prompt selects the new focus; other hooks may only affect that focus.
        if (ev.Event != "UserPromptSubmit")
        {
            if (ev.Session.Length > 0 && Session.Length > 0 && ev.Session != Session) return false;
            if (ev.Turn.Length > 0 && Turn.Length > 0 && ev.Turn != Turn) return false;
        }
        if (Preview) ClearActivity();

        if (ev.Event == "UserPromptSubmit" || Session.Length == 0)
        {
            Session = ev.Session;
            // Missing IDs remain compatible with older hooks. An unidentified new
            // prompt must not inherit the previous question's known ID.
            Turn = ev.Turn;
            terminal = false;
        }
        else if (Turn.Length == 0 && ev.Turn.Length > 0) Turn = ev.Turn;

        // SessionStart can arrive after the prompt; it must not replace active work.
        if (working && ev.Event == "SessionStart") return false;
        if (terminal && ev.Event is "PostToolUse" or "SubagentStop" or "PostCompact") return false;
        if (IsTerminal(ev.Event))
        {
            terminal = true;
            working = false;
        }
        if (ev.Event == "PreToolUse") terminal = false;

        // A tool's approval request must stay visible until an actual resume or terminal event.
        if (Current == "PermissionRequest" && ev.Event is "PostToolUse" or "SubagentStart" or "SubagentStop") return false;
        if (ev.Event != "SessionStart" && !IsTerminal(ev.Event)) working = true;
        if (Current == ev.Event)
        {
            pending = null;
            return true;
        }

        bool immediate = Poses[ev.Event].Priority >= ImmediatePriority || ev.Event == "UserPromptSubmit";
        if (!immediate && now - ChangedAt < MinimumPoseDurationMs)
        {
            pending = ev.Event;
            return true;
        }

        pending = null;
        Switch(ev.Event, now);
        return true;
    }

    public void Tick(long now)
    {
        if (pending != null && now - ChangedAt >= MinimumPoseDurationMs)
        {
            var next = pending;
            pending = null;
            Switch(next, now);
            return;
        }
        if (returnAt > 0 && now >= returnAt && (!working || Preview))
            Switch(Poses[Current].After ?? "Idle", now);
    }

    public void Reset(long now)
    {
        ClearActivity();
        Switch("Idle", now);
    }

    private void ClearActivity()
    {
        pending = null;
        Preview = false;
        terminal = false;
        working = false;
    }

    private static bool IsTerminal(string state) => state is "Stop" or "Interrupt" or "SessionEnd";

    private void Switch(string state, long now)
    {
        Current = state;
        ChangedAt = now;
        returnAt = Poses[state].HoldMs > 0 ? now + Poses[state].HoldMs : 0;
        Changed?.Invoke(state);
    }
}
