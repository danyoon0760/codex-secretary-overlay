namespace SecretaryOverlay;

internal static class BubbleAutoCloseVerification
{
    public static void Check(Action<bool, string> check)
    {
        var policy = new BubbleAutoClosePolicy();
        check(policy.TakeExpired(0).Length == 0, "An empty lifetime policy has no expiry work");
        policy.Observe([new("active", false)], 0);
        check(policy.TakeExpired(1_000_000).Length == 0, "An active or still-revealing bubble never expires");
        policy.Observe([new("active", true)], 1_001_000);
        check(policy.TakeExpired(1_060_999).Length == 0, "A completed sole bubble stays for a full minute after readiness");
        check(policy.TakeExpired(1_061_000).SequenceEqual(["active"]),
            "A completed sole bubble expires exactly at one minute without a newest-card exception");

        var updates = new BubbleAutoClosePolicy();
        updates.Observe([new("result", true)], 1_000);
        for (long time = 2_000; time < 61_000; time += 1_000) updates.Observe([new("result", true)], time);
        check(updates.TakeExpired(61_000).SequenceEqual(["result"]),
            "Repeated observation of ready content does not postpone its existing one-minute deadline");

        var summary = new BubbleAutoClosePolicy();
        summary.Observe([new("task", true)], 0);
        summary.Observe([new("task", false)], 10_000);
        check(summary.TakeExpired(100_000).Length == 0,
            "Starting or revealing an AI completion summary cancels the earlier expiry deadline");
        summary.Observe([new("task", false)], 200_000);
        summary.Observe([new("task", true)], 210_000);
        check(summary.TakeExpired(269_999).Length == 0 && summary.TakeExpired(270_000).SequenceEqual(["task"]),
            "The minute begins anew only after the final summary has finished displaying");

        var mixed = new BubbleAutoClosePolicy();
        mixed.Observe([new("newest", true), new("working", false), new("waiting", false),
            new("overflow-done", true), new("overflow-active", false)], 1_000);
        check(mixed.TakeExpired(61_000).SequenceEqual(["newest", "overflow-done"]),
            "Top and queued completed bubbles expire while active and approval-waiting tasks remain");
        check(mixed.TakeExpired(1_000_000).Length == 0,
            "Preserved active overflow never acquires a completion timer merely from being hidden");

        var manual = new BubbleAutoClosePolicy();
        manual.Observe([new("B", true), new("A", true)], 0);
        manual.Observe([new("A", true)], 10_000);
        check(manual.TakeExpired(60_000).SequenceEqual(["A"]),
            "Manually closing another card does not protect or restart a completed card's timer");
        var promoted = new BubbleAutoClosePolicy();
        promoted.Observe([new("B", true), new("A", true)], 0);
        promoted.Observe([new("A", true), new("B", true)], 20_000);
        check(promoted.TakeExpired(60_000).ToHashSet().SetEquals(["A", "B"]),
            "Promoting an existing message changes its position without resetting either deadline");

        var newTurn = new BubbleAutoClosePolicy();
        newTurn.Observe([new("session:old", true)], 0);
        newTurn.Observe([new("session:new", false)], 10_000);
        check(newTurn.TakeExpired(100_000).Length == 0,
            "A new question identity cannot inherit the preceding completed question's timer");
        newTurn.Observe([new("session:new", true)], 101_000);
        check(newTurn.TakeExpired(160_999).Length == 0 && newTurn.TakeExpired(161_000).SequenceEqual(["session:new"]),
            "A new question receives its own full completion lifetime");

        var cleared = new BubbleAutoClosePolicy();
        cleared.Observe([new("A", true)], 0);
        cleared.Observe([], 1_000);
        check(cleared.TakeExpired(100_000).Length == 0, "Removing all observations clears all pending timers");
        cleared.Observe([new("A", true)], 101_000);
        check(cleared.TakeExpired(160_999).Length == 0 && cleared.TakeExpired(161_000).SequenceEqual(["A"]),
            "A restored observation gets a fresh timer rather than an already-expired one");
        CheckReadingPause(check);
    }

    private static void CheckReadingPause(Action<bool, string> check)
    {
        var reading = new BubbleAutoClosePolicy();
        reading.Observe([new("ready", true), new("active", false)], 1_000);
        reading.SetPaused(true, 21_000);
        reading.SetPaused(true, 30_000);
        reading.Observe([new("ready", true), new("active", false)], 70_000);
        check(reading.TakeExpired(100_000).Length == 0,
            "Hover pauses the whole completion countdown while observations and live updates continue");
        reading.SetPaused(false, 100_000);
        reading.SetPaused(false, 110_000);
        check(reading.TakeExpired(139_999).Length == 0 && reading.TakeExpired(140_000).SequenceEqual(["ready"]),
            "Leaving hover resumes the remaining forty seconds without restarting the minute");

        var completedWhileReading = new BubbleAutoClosePolicy();
        completedWhileReading.Observe([new("old", true), new("new", false)], 0);
        completedWhileReading.SetPaused(true, 10_000);
        completedWhileReading.Observe([new("old", true), new("new", true)], 30_000);
        completedWhileReading.SetPaused(false, 100_000);
        check(completedWhileReading.TakeExpired(149_999).Length == 0
              && completedWhileReading.TakeExpired(150_000).SequenceEqual(["old"]),
            "Completing another message during hover preserves the older message's remaining fifty seconds");
        check(completedWhileReading.TakeExpired(159_999).Length == 0
              && completedWhileReading.TakeExpired(160_000).SequenceEqual(["new"]),
            "A message that finishes displaying during hover gets a full unpaused minute after leaving");

        var repeated = new BubbleAutoClosePolicy();
        repeated.Observe([new("ready", true)], 0);
        repeated.SetPaused(true, 10_000); repeated.SetPaused(false, 20_000);
        repeated.SetPaused(true, 30_000); repeated.SetPaused(false, 50_000);
        check(repeated.TakeExpired(89_999).Length == 0 && repeated.TakeExpired(90_000).SequenceEqual(["ready"]),
            "Several hover pauses accumulate without losing or adding completion time");

        var canceledWhileReading = new BubbleAutoClosePolicy();
        canceledWhileReading.Observe([new("task", true)], 0);
        canceledWhileReading.SetPaused(true, 10_000);
        canceledWhileReading.Observe([new("task", false)], 20_000);
        canceledWhileReading.SetPaused(false, 30_000);
        check(canceledWhileReading.TakeExpired(100_000).Length == 0,
            "Returning to active work during hover cancels the deadline rather than resuming it");
    }
}
