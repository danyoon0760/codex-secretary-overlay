using System.Windows;

namespace SecretaryOverlay;

internal static class BubbleReflowMotionVerification
{
    public static void Check(Action<bool, string> check)
    {
        const long start = 1000;
        static bool Near(Vector left, Vector right) => (left - right).Length < .000001;
        var original = new Vector(-80, 160);
        var input = new Dictionary<string, Vector> { ["task"] = original };
        var motion = new BubbleReflowMotion(start, input);
        input["task"] = new Vector(900, 900);
        check(Near(motion.Offset("task", start), original) && !motion.IsComplete(start),
            "Reflow starts at its captured visual offset and does not retain the caller's mutable dictionary");
        check(Near(motion.Offset("task", start + 90), original * .125),
            "Halfway through reflow, cubic ease-out leaves one eighth of the original displacement");
        double previous = original.Length;
        bool monotonic = true;
        for (long time = start; time <= start + BubbleReflowMotion.DurationMs; time++)
        {
            var current = motion.Offset("task", time);
            monotonic &= current.Length <= previous + .000001 && current.X >= original.X && current.X <= 0
                && current.Y >= 0 && current.Y <= original.Y;
            previous = current.Length;
        }
        check(monotonic, "Reflow approaches its target monotonically on both axes without overshoot");
        check(!motion.IsComplete(start + 179) && motion.IsComplete(start + 180)
            && motion.Offset("task", start + 180) == default && motion.Offset("task", start + 900) == default,
            "Reflow reaches exact zero at its 180 ms deadline and remains complete afterwards");
        check(motion.Offset("missing", start) == default && motion.Offset("missing", start + 90) == default
            && Near(motion.Offset("task", start - 10), original),
            "Unknown cards have no offset and timestamps before the start stay at the initial position");

        var interruptedOffset = motion.Offset("task", start + 60);
        var interrupted = new BubbleReflowMotion(start + 60, new Dictionary<string, Vector> { ["task"] = interruptedOffset });
        check(Near(interrupted.Offset("task", start + 60), interruptedOffset)
            && !interrupted.IsComplete(start + 180) && interrupted.IsComplete(start + 240),
            "A new structural change can start from the current visual position with its own complete duration");

        var streaming = new BubbleReflowMotion(start, new Dictionary<string, Vector> { ["task"] = original, ["removed"] = new(50, -50) });
        var changedLayoutOffset = streaming.Offset("task", start + 60) + new Vector(12, -25);
        streaming.Rebase(new Dictionary<string, Vector> { ["task"] = changedLayoutOffset }, start + 60);
        check(Near(streaming.Offset("task", start + 60), changedLayoutOffset) && streaming.Offset("removed", start + 60) == default,
            "A text relayout preserves the supplied current visual position and discards removed card offsets");
        var sameTime = streaming.Offset("task", start + 60);
        streaming.Rebase(new Dictionary<string, Vector> { ["task"] = sameTime }, start + 60);
        check(Near(streaming.Offset("task", start + 60), sameTime),
            "Repeated relayout at the same timestamp does not introduce a positional jump");
        var almostDone = streaming.Offset("task", start + 179) + new Vector(0, 18);
        streaming.Rebase(new Dictionary<string, Vector> { ["task"] = almostDone }, start + 179);
        check(Near(streaming.Offset("task", start + 179), almostDone)
            && !streaming.IsComplete(start + 179) && streaming.IsComplete(start + 180)
            && streaming.Offset("task", start + 180) == default,
            "Even a streamed relayout immediately before the end preserves continuity without extending the original deadline");
        streaming.Rebase(new Dictionary<string, Vector> { ["task"] = new(100, 100) }, start + 180);
        check(streaming.IsComplete(start + 180) && streaming.Offset("task", start + 180) == default
            && streaming.Offset("task", start + 500) == default,
            "Rebasing an already completed motion cannot start an additional animation");
    }
}
