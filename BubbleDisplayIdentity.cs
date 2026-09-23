namespace SecretaryOverlay;

// Shared ordering for task messages and ambient messages, independent of their screen slots.
internal static class BubbleDisplayIdentity
{
    private static long sequence;
    public static long Next() => Interlocked.Increment(ref sequence);
}
