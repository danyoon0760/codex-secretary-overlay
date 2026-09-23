namespace SecretaryOverlay;

internal static class CommentaryClient
{
    public const string Model = "gpt-5.6-luna";
    public static async Task<string> GenerateAsync(string folder, IReadOnlyList<string> images, ViewingState viewing,
        IReadOnlyList<string> recentComments, ObservedWindow? window, CancellationToken token, ModelProfile? profile = null,
        PetPersonalization? personalization = null, Action<string>? progress = null, Action<string>? reasoning = null)
    {
        string prompt = CommentaryConversation.CreatePrompt(viewing, window, recentComments, personalization);
        string Display(string value)
        {
            value = ProgressDisplayText.Clean(value);
            return value.Length <= 240 ? value : value[..237] + "…";
        }
        string response = await CodexStreamingClient.GenerateAsync(folder, images, prompt, ModelProfile.Validated(profile), null,
            value => { string text = Display(value); if (text.Length > 0) progress?.Invoke(text); }, token, reasoning);
        return Display(response);
    }
}
