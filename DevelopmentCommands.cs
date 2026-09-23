namespace SecretaryOverlay;

internal static class DevelopmentCommands
{
    public static int? Run(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest.Run();
        if (args.Contains("--render-long-bubble")) return RequestedImprovementsVerification.RenderLongBubble();
        if (args.Contains("--render-preview")) return CommentaryVerification.RenderPreview();
        if (args.Contains("--render-multi-bubble")) return MultiBubbleVerification.Render();
        if (args.Contains("--render-bubble-reflow")) return BubbleReflowPreview.Render();
        if (args.Contains("--render-overflow-bubble")) return OverflowBubblePreview.Render();
        if (args.Contains("--render-per-question")) return PerQuestionVerification.Render();
        if (args.Contains("--render-annotation-display")) return DisplayTextVerification.Render();
        if (args.Contains("--render-completion-settings")) return CompletionStyleVerification.Render();
        if (args.Contains("--render-activity-detail")) return ActivityDetailVerification.Render();
        if (args.Contains("--render-bubble-position")) return BubblePositionPreview.Render();
        if (args.Contains("--render-settings")) return SettingsVerification.Render();
        if (args.Contains("--streaming-smoke")) return StreamingVerification.SmokeAsync().GetAwaiter().GetResult();
        if (args.Contains("--streaming-image-smoke")) return StreamingVerification.ImageSmokeAsync().GetAwaiter().GetResult();
        if (args.Contains("--completion-summary-smoke")) return CompletionStyleVerification.SmokeAsync().GetAwaiter().GetResult();
        if (args.Contains("--model-selection-smoke")) return ModelMenuVerification.SmokeAsync().GetAwaiter().GetResult();
        if (args.Contains("--render-pose-comparison")) return PoseComparison.Render();
        if (args.Contains("--commentary-smoke")) return CommentaryVerification.SmokeAsync().GetAwaiter().GetResult();
        if (args.Contains("--commentary-variety-smoke")) return CommentaryVerification.SmokeAsync(true).GetAwaiter().GetResult();
        if (args.Contains("--capture-window-check")) return CaptureVerification.CheckForeground();
        return null;
    }
}
