namespace SecretaryOverlay;

internal static class StateTurnVerification
{
    public static void Check(Action<bool, string> check)
    {
        var engine = new StateEngine();
        var changes = new List<string>();
        engine.Changed += changes.Add;
        engine.Accept(new("UserPromptSubmit", "session", Turn: "A"), 1_000);
        engine.Accept(new("PreToolUse", "session", Turn: "A"), 1_800);
        engine.Accept(new("UserPromptSubmit", "session", Turn: "B"), 1_900);
        var changeCount = changes.Count;
        check(!engine.Accept(new("Stop", "session", Turn: "A"), 2_000)
              && engine.Current == "UserPromptSubmit" && engine.Turn == "B"
              && engine.ChangedAt == 1_900 && changes.Count == changeCount,
            "A delayed stop from question A cannot finish question B or emit a pose change");
        engine.Tick(20_000);
        check(engine.Current == "UserPromptSubmit",
            "Rejecting an old stop leaves the current question working without a completion timeout");

        engine.Accept(new("Stop", "session", Turn: "B"), 21_000);
        engine.Tick(30_999);
        check(engine.Current == "Stop", "The current question's stop retains the ten-second completion pose");
        engine.Tick(31_000);
        check(engine.Current == "Idle", "The current question's completion pose ends exactly after ten seconds");

        var pending = new StateEngine();
        pending.Accept(new("UserPromptSubmit", "session", Turn: "A"), 1_000);
        pending.Accept(new("PreToolUse", "session", Turn: "A"), 1_800);
        pending.Accept(new("UserPromptSubmit", "session", Turn: "B"), 1_900);
        pending.Accept(new("PreToolUse", "session", Turn: "B"), 2_000);
        bool rejected = !pending.Accept(new("Stop", "session", Turn: "A"), 2_100);
        pending.Tick(2_700);
        check(rejected && pending.Current == "PreToolUse" && pending.Turn == "B",
            "An old stop cannot discard the current question's pending tool pose");

        pending.Accept(new("PermissionRequest", "session", Turn: "B"), 3_000);
        bool allOldRejected = true;
        foreach (var name in new[] { "PermissionRequest", "Interrupt", "SessionEnd", "PreToolUse", "PostToolUse", "PostCompact", "SubagentStop" })
            allOldRejected &= !pending.Accept(new(name, "session", Turn: "A"), 3_100);
        check(allOldRejected && pending.Current == "PermissionRequest" && pending.ChangedAt == 3_000,
            "Even urgent and tool events from an old question cannot replace the current approval pose");
        pending.Accept(new("PreToolUse", "session", Turn: "B"), 3_900);
        check(pending.Current == "PreToolUse", "A tool event for the current question still resumes approved work");

        pending.Accept(new("UserPromptSubmit", "other-session", Turn: "C"), 4_000);
        check(pending.Session == "other-session" && pending.Turn == "C"
              && !pending.Accept(new("Stop", "session", Turn: "B"), 4_100)
              && pending.Current == "UserPromptSubmit",
            "A new prompt changes the focused session and old-session completion remains ignored");

        pending.Accept(new("Stop", "session", Preview: true, Turn: "A"), 4_200);
        check(pending.Preview && pending.Current == "Stop" && pending.Session == "other-session" && pending.Turn == "C",
            "Pose previews keep their independent behavior without changing the live question identity");
        bool oldPreviewRejected = !pending.Accept(new("Stop", "other-session", Turn: "B"), 4_300);
        bool otherSessionRejected = !pending.Accept(new("PermissionRequest", "session", Turn: "C"), 4_400);
        check(oldPreviewRejected && otherSessionRejected && pending.Preview && pending.ChangedAt == 4_200,
            "Rejected live hooks cannot clear a preview or alter its display timer");
        pending.Accept(new("PreToolUse", "other-session", Turn: "C"), 5_100);
        check(!pending.Preview && pending.Current == "PreToolUse",
            "A valid live event still leaves preview mode and resumes the current question");

        var legacy = new StateEngine();
        legacy.Accept(new("UserPromptSubmit", "legacy"), 1_000);
        legacy.Accept(new("PreToolUse", "legacy"), 1_800);
        check(legacy.Accept(new("Stop", "legacy"), 1_900) && legacy.Current == "Stop" && legacy.Turn == "",
            "Legacy hooks without question IDs retain their completion behavior");
        legacy.Accept(new("UserPromptSubmit", "legacy", Turn: "known"), 2_000);
        check(legacy.Accept(new("Stop", "legacy"), 2_100) && legacy.Current == "Stop" && legacy.Turn == "known",
            "An unidentified hook remains compatible and does not erase a known question ID");
        legacy.Accept(new("UserPromptSubmit", "legacy"), 2_200);
        check(legacy.Turn.Length == 0 && legacy.Accept(new("PreToolUse", "legacy", Turn: "new"), 3_000)
              && legacy.Turn == "new" && legacy.Current == "PreToolUse",
            "An unidentified new prompt clears the prior ID and learns the new ID from a subsequent hook");

        var attached = new StateEngine();
        attached.Accept(new("PreToolUse", "attached", Turn: "active"), 1_000);
        check(attached.Session == "attached" && attached.Turn == "active"
              && !attached.Accept(new("Stop", "attached", Turn: "older"), 1_100),
            "Starting the pet during existing work establishes its question identity from the first hook");
    }
}
