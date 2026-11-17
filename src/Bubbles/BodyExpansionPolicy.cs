namespace SecretaryOverlay;

internal static class BodyExpansionPolicy
{
    // Keep ordinary chat unchanged. Long paragraphs or many explicit lines receive one link.
    public static bool ShouldCollapse(string body) => body.Length > 260 || body.Count(c => c == '\n') >= 5;
}
