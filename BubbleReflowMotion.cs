using System.Windows;

namespace SecretaryOverlay;

internal sealed class BubbleReflowMotion
{
    internal const long DurationMs = 180;
    private readonly long startedAt;
    private Dictionary<string, Vector> offsets = new(StringComparer.Ordinal);

    public BubbleReflowMotion(long startedAt, IReadOnlyDictionary<string, Vector> offsets)
    {
        this.startedAt = startedAt;
        Rebase(offsets, startedAt);
    }

    public Vector Offset(string key, long now)
    {
        double factor = RemainingFactor(now);
        return factor > 0 && offsets.TryGetValue(key, out var offset) ? offset * factor : default;
    }

    public bool IsComplete(long now) => RemainingFactor(now) == 0;

    // A new layout can change the destination while content streams. Preserve its
    // current visual offset, but keep the original animation's completion time.
    public void Rebase(IReadOnlyDictionary<string, Vector> currentOffsets, long now)
    {
        double factor = RemainingFactor(now);
        var rebased = new Dictionary<string, Vector>(StringComparer.Ordinal);
        if (factor > 0)
            foreach (var (key, offset) in currentOffsets) rebased[key] = offset / factor;
        offsets = rebased;
    }

    private double RemainingFactor(long now)
    {
        double remaining = 1 - Math.Clamp(((double)now - startedAt) / DurationMs, 0, 1);
        return remaining * remaining * remaining;
    }
}
