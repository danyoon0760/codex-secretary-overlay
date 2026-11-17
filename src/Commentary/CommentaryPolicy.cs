namespace SecretaryOverlay;

internal sealed class CommentaryPolicy(long now)
{
    public const int DefaultIntervalMinutes = 20;
    public const int MinIntervalMinutes = 1;
    public const int MaxIntervalMinutes = 180;
    public const long IntervalMs = DefaultIntervalMinutes * 60 * 1000;
    public const long MouseRecentMs = 5 * 60 * 1000;
    public bool Enabled { get; set; } = true;
    public bool Busy { get; private set; }
    public int IntervalMinutes { get; private set; } = DefaultIntervalMinutes;
    public long NextAt { get; private set; } = now + IntervalMs;

    private long CurrentIntervalMs => IntervalMinutes * 60_000L;

    public static int NormalizeIntervalMinutes(int minutes) =>
        Math.Clamp(minutes, MinIntervalMinutes, MaxIntervalMinutes);

    public void SetIntervalMinutes(int minutes, long now)
    {
        IntervalMinutes = NormalizeIntervalMinutes(minutes);
        ResetTimer(now);
    }

    public bool CanStart(long now, bool manual, bool idle, bool available, bool mouseRecent, bool videoPlaying) =>
        !Busy && available && (manual || (Enabled && idle && now >= NextAt && (mouseRecent || videoPlaying)));

    public void Begin() => Busy = true;
    public void Finish(long now, bool success)
    {
        Busy = false;
        NextAt = now + (success ? CurrentIntervalMs : 60_000);
    }
    public void ResetTimer(long now) => NextAt = now + CurrentIntervalMs;
}
