namespace SecretaryOverlay;

// Keep the stream responsive while work is visible, then retain a cheap path for late writes.
// Times are monotonic milliseconds so a wall-clock correction cannot suspend polling.
internal sealed class ProgressPollingPolicy
{
    internal const long ActiveInterval = 150;
    internal const long DismissedInterval = 1_000;
    internal const long FinishedInterval = 5_000;
    internal const long CompletionGrace = 10_000;

    private sealed class Entry(string turn, bool active, long now)
    {
        public string Turn { get; } = turn;
        public bool Active { get; set; } = active;
        public long? FinishedAt { get; set; } = active ? null : now;
        public long? LastRead { get; set; }
        public bool Wake { get; set; } = true;
    }

    private readonly Dictionary<string, Entry> entries = new();

    // A hook can arrive while an earlier async read is still in flight. Keep the wake request
    // until the next read begins, rather than clearing it when the earlier read completes.
    public void NotifyHook(TaskProgress task, long now) => Observe(task, now).Wake = true;

    public bool TryBeginRead(TaskProgress task, long now)
    {
        var entry = Observe(task, now);
        long interval = task.Active
            ? task.Dismissed ? DismissedInterval : ActiveInterval
            : now - entry.FinishedAt!.Value < CompletionGrace ? ActiveInterval : FinishedInterval;
        if (!entry.Wake && entry.LastRead is { } last && now - last < interval) return false;
        entry.Wake = false;
        entry.LastRead = now;
        return true;
    }

    public void Remove(string session) => entries.Remove(session);

    private Entry Observe(TaskProgress task, long now)
    {
        if (!entries.TryGetValue(task.Session, out var entry) || entry.Turn != task.Turn)
            entries[task.Session] = entry = new Entry(task.Turn, task.Active, now);
        else if (entry.Active != task.Active)
        {
            entry.Active = task.Active;
            entry.FinishedAt = task.Active ? null : now;
            entry.Wake = true;
        }
        return entry;
    }
}
