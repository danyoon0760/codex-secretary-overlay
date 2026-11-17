using System.Globalization;

namespace SecretaryOverlay;

internal sealed class BubbleTextReveal
{
    private const double GraphemesPerSecond = 65;
    private const int BatchSize = 2;
    private int[] boundaries = [];
    private int visible;
    private double progress;
    private double rate;
    private long lastAt;
    private bool hasClock;

    public string Text { get; private set; } = "";
    public string Target { get; private set; } = "";
    public bool IsComplete => visible == boundaries.Length;

    public void SetTarget(string value, long now, bool animate)
    {
        Advance(now);
        if (value == Target)
        {
            if (!animate) Complete();
            return;
        }

        bool append = value.StartsWith(Target, StringComparison.Ordinal);
        bool shortened = Target.StartsWith(value, StringComparison.Ordinal);
        int visibleLength = Text.Length;
        double pendingFraction = progress - visible;
        Target = value;
        boundaries = StringInfo.ParseCombiningCharacters(value);
        if (append || shortened)
        {
            // Display cleanup can remove a pending markup suffix (even a lone
            // colon). Keep the remaining prefix instead of replaying the body.
            // An appended combining mark or emoji joiner may extend the last
            // already-visible grapheme. Reveal that cluster whole, never cut it.
            visible = BoundaryAtOrAfter(visibleLength);
            progress = Math.Min(boundaries.Length, visible + Math.Max(0, pendingFraction));
        }
        else
        {
            visible = 0;
            progress = 0;
            Text = "";
        }

        if (!animate) { Complete(); return; }
        double remaining = boundaries.Length - progress;
        double duration = Math.Min(remaining <= 80 ? 900 : 1200, remaining / GraphemesPerSecond * 1000);
        rate = duration > 0 ? remaining / duration * 1000 : 0;
        Publish();
    }

    public string Advance(long now)
    {
        if (!hasClock) { lastAt = now; hasClock = true; return Text; }
        if (now <= lastAt) return Text;
        double elapsed = (double)now - lastAt;
        lastAt = now;
        if (IsComplete) return Text;
        progress = Math.Min(boundaries.Length, progress + elapsed * rate / 1000);
        Publish();
        return Text;
    }

    private void Publish()
    {
        if (boundaries.Length - progress < .0000001) { Complete(); return; }
        int count = Math.Max(visible, (int)(progress / BatchSize) * BatchSize);
        visible = Math.Min(count, boundaries.Length);
        Text = Target[..(visible == boundaries.Length ? Target.Length : boundaries[visible])];
    }

    private void Complete()
    {
        visible = boundaries.Length;
        progress = visible;
        rate = 0;
        Text = Target;
    }

    private int BoundaryAtOrAfter(int length)
    {
        if (length >= Target.Length) return boundaries.Length;
        int index = Array.BinarySearch(boundaries, length);
        return index >= 0 ? index : ~index;
    }
}
