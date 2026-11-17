using System.IO;
using System.Text.Json;
namespace SecretaryOverlay;

public static class SelfTest
{
    public static int Run()
    {
        var results = new List<string>();
        void Check(bool pass, string label)
        {
            if (!pass) throw new InvalidOperationException(label);
            results.Add(label);
        }
        var s = new StateEngine();
        s.Accept(new("UserPromptSubmit", "A"), 1000);
        s.Accept(new("PreToolUse", "A"), 1200);
        s.Accept(new("PostToolUse", "A"), 1300);
        Check(s.Current == "UserPromptSubmit", "Burst events coalesce without flashing");
        s.Tick(1800);
        Check(s.Current == "PostToolUse", "Latest pending event appears");
        s.Accept(new("PermissionRequest", "A"), 1850);
        Check(s.Current == "PermissionRequest", "Approval interrupts immediately");
        s.Accept(new("PostToolUse", "A"), 2000);
        s.Tick(15000);
        Check(s.Current == "PermissionRequest", "Approval does not expire or get replaced by unrelated completion");
        s.Accept(new("PreToolUse", "A"), 16000);
        s.Accept(new("Interrupt", "A"), 16010);
        s.Accept(new("PostToolUse", "A"), 17000);
        Check(s.Current == "Interrupt", "Late tool completion cannot undo interruption");
        s.Tick(18000);
        Check(s.Current == "Idle", "Interruption settles into idle");
        s.Accept(new("UserPromptSubmit", "A"), 19000);
        s.Accept(new("Stop", "B"), 20000);
        Check(s.Current == "UserPromptSubmit", "Other sessions cannot finish the focused session");
        s.Accept(new("Stop", "A"), 21000);
        s.Tick(31000);
        Check(s.Current == "Idle", "Completed turn returns to idle");
        var active = new StateEngine();
        active.Accept(new("UserPromptSubmit", "A"), 1000);
        active.Accept(new("SessionStart", "A"), 1020);
        active.Tick(10000);
        Check(active.Current == "UserPromptSubmit", "Late session start cannot return active work to idle");
        long time = 11000;
        foreach (var name in new[] { "PreToolUse", "PostToolUse", "PreCompact", "PostCompact", "SubagentStart", "SubagentStop" })
        {
            active.Accept(new(name, "A"), time);
            active.Tick(time + 60000);
            Check(active.Current == name, "Working pose persists until the next event: " + name);
            time += 61000;
        }
        active.Accept(new("PreToolUse", "A"), time);
        active.Accept(new("PostToolUse", "A"), time + 100);
        active.Accept(new("PreToolUse", "A"), time + 200);
        active.Tick(time + 10000);
        Check(active.Current == "PreToolUse", "Latest repeated action cancels a stale pending pose");
        foreach (var (name, hold) in new[] { ("Stop", 10000), ("Interrupt", 1800), ("SessionEnd", 2000) })
        {
            time += 20000;
            active.Accept(new("UserPromptSubmit", "A"), time);
            active.Accept(new(name, "A"), time + 100);
            active.Tick(time + 100 + hold - 1);
            Check(active.Current == name, "Existing end pose duration: " + name);
            active.Tick(time + 100 + hold);
            Check(active.Current == "Idle", "End pose still returns to idle: " + name);
        }
        active.Accept(new("PostToolUse", Preview: true), time + 10000);
        active.Tick(time + 11200);
        Check(active.Current == "UserPromptSubmit", "Preview keeps its existing automatic transition");
        foreach (var name in StateEngine.Poses.Keys.Where(k => k != "Idle"))
        {
            s.Accept(new(name, Preview: true), 25000);
            Check(s.Current == name, "Preview: " + name);
        }
        CheckBoundaries(Check);
        StateTurnVerification.Check(Check);
        CommentaryVerification.Check(Check);
        CommentaryIntervalVerification.Check(Check);
        CaptureVerification.Check(Check);
        ProgressVerification.Check(Check);
        ProgressPollingVerification.Check(Check);
        ProgressRetentionVerification.Check(Check);
        MultiBubbleVerification.Check(Check);
        PerQuestionVerification.Check(Check);
        DisplayTextVerification.Check(Check);
        BubbleAutoCloseVerification.Check(Check);
        BubbleLifecycleVerification.Check(Check);
        BubbleAutoCloseWindowVerification.Check(Check);
        BubbleReflowMotionVerification.Check(Check);
        BubbleReflowWindowVerification.Check(Check);
        BubbleTextRevealVerification.Check(Check);
        BubblePresentationVerification.Check(Check);
        ChatNavigationVerification.Check(Check);
        OverflowBubbleVerification.Check(Check);
        OverflowBubbleWindowVerification.Check(Check);
        CompletionVerification.Check(Check);
        CompletionStyleVerification.Check(Check);
        CompletionWaitVerification.Check(Check);
        ActivityDetailVerification.Check(Check);
        ModelMenuVerification.Check(Check);
        SettingsVerification.Check(Check);
        NumberSettingVerification.Check(Check);
        SettingsNavigationVerification.Check(Check);
        BubbleAppearanceVerification.Check(Check);
        PersonalizationVerification.Check(Check);
        DraftVerification.Check(Check);
        SmoothScrollingVerification.Check(Check);
        StreamingVerification.Check(Check);
        IntegrationBoundaryVerification.Check(Check);
        PoseRegistrationVerification.Check(Check);
        RequestedImprovementsVerification.Check(Check);
        foreach (var file in StateEngine.Poses.Values.Select(p => p.File).Distinct())
            Check(File.Exists(Path.Combine(AppStorage.Root, "assets", file + ".png")), "Asset: " + file);
        Directory.CreateDirectory(AppStorage.DataDirectory);
        File.WriteAllText(Path.Combine(AppStorage.DataDirectory, "self-test.json"), JsonSerializer.Serialize(new { ok = true, checks = results }, AppStorage.Json));
        return 0;
    }

    private static void CheckBoundaries(Action<bool, string> check)
    {
        var engine = new StateEngine();
        var changes = new List<string>();
        engine.Changed += changes.Add;
        check(!engine.Accept(new("Unknown", "A"), 0), "Unknown events are rejected");
        check(!engine.Accept(new("Idle", "A"), 0), "Idle is an internal state only");
        check(changes.Count == 0, "Rejected events do not emit state changes");

        engine.Accept(new("UserPromptSubmit", "A"), 1000);
        engine.Accept(new("PreToolUse", "A"), 1100);
        engine.Tick(1799);
        check(engine.Current == "UserPromptSubmit", "Pending pose waits the full 800 ms");
        engine.Tick(1800);
        check(engine.Current == "PreToolUse" && engine.ChangedAt == 1800,
            "Pending pose transitions exactly at 800 ms");
        check(changes.SequenceEqual(new[] { "UserPromptSubmit", "PreToolUse" }),
            "Pending events emit a change only when displayed");

        engine.Accept(new("PostToolUse", "A"), 1900);
        engine.Reset(2000);
        engine.Tick(10000);
        check(engine.Current == "Idle" && !engine.Preview, "Reset clears pending poses and preview mode");

        engine.Accept(new("Stop", "A", Preview: true), 11000);
        engine.Accept(new("UserPromptSubmit", "B"), 11100);
        engine.Tick(15000);
        check(engine.Current == "UserPromptSubmit" && !engine.Preview && engine.Session == "B",
            "A new prompt exits preview and focuses its session without a stale timeout");
        check(!engine.Accept(new("PermissionRequest", "A"), 16000),
            "Even urgent events from an unfocused session are rejected");
    }
}
