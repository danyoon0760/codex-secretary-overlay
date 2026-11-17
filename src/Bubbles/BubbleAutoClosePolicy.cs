namespace SecretaryOverlay;

internal sealed record BubbleLifetimeObservation(string Key, bool Ready);

// Readiness includes both task completion and the end of its visible text reveal.
internal sealed class BubbleAutoClosePolicy
{
    internal const long DelayMs = 60_000;
    private sealed class Entry(long sequence)
    {
        public long Sequence { get; } = sequence;
        public long? Due { get; set; }
    }

    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private long nextSequence;
    private long? pauseStarted;
    private long pausedDuration;

    public void SetPaused(bool paused, long now)
    {
        if (paused)
        {
            pauseStarted ??= now;
        }
        else if (pauseStarted is { } started)
        {
            pausedDuration += Math.Max(0, now - started);
            pauseStarted = null;
        }
    }

    private long CountdownTime(long now) => (pauseStarted ?? now) - pausedDuration;

    public void Observe(IReadOnlyList<BubbleLifetimeObservation> observations, long now)
    {
        now = CountdownTime(now);
        var observed = observations.DistinctBy(observation => observation.Key, StringComparer.Ordinal).ToArray();
        var present = observed.Select(observation => observation.Key).ToHashSet(StringComparer.Ordinal);
        foreach (string key in entries.Keys.Where(key => !present.Contains(key)).ToArray()) entries.Remove(key);

        for (int i = observed.Length - 1; i >= 0; i--)
        {
            var observation = observed[i];
            if (!entries.TryGetValue(observation.Key, out var entry))
                entries.Add(observation.Key, entry = new Entry(++nextSequence));
            if (observation.Ready) entry.Due ??= now + DelayMs;
            else entry.Due = null;
        }
    }

    public string[] TakeExpired(long now)
    {
        if (pauseStarted.HasValue || entries.Count == 0) return [];
        now = CountdownTime(now);
        var expired = entries.Where(pair => pair.Value.Due is { } due && now >= due)
            .OrderByDescending(pair => pair.Value.Sequence).Select(pair => pair.Key).ToArray();
        foreach (string key in expired) entries.Remove(key);
        return expired;
    }
}
