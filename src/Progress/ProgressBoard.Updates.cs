
namespace SecretaryOverlay;

internal sealed partial class ProgressBoard
{
    public bool Apply(ProgressMessage message)
    {
        if (message.Turn.Length > 0 && retiredTurns.Contains((message.Session, message.Turn))) return false;
        var task = Find(message.Session, message.Turn);
        if (task is null && Latest(message.Session) is { Turn.Length: 0 } unidentified
            && message.At >= unidentified.CreatedAt.AddSeconds(-2))
        {
            task = unidentified;
            task.Turn = message.Turn;
        }
        if (task is null || message.Turn.Length == 0) return false;
        if (message.Kind == ProgressKind.FinalAnswer) return ApplyFinalAnswer(task, message);
        // Transcript writes can land after Stop. A stale heading must not replace completion.
        if (!task.Active) return false;
        if (message.Kind == ProgressKind.Activity)
        {
            if (task.State != "PermissionRequest") { task.Detail = message.Text; task.DetailFromTranscript = true; }
        }
        else
        {
            string clean = ProgressDisplayText.Clean(message.Text);
            // Prefer actual Korean updates within this turn; the default placeholder doesn't count.
            bool korean = ProgressDisplayText.ContainsKoreanContent(clean);
            if (clean.Length > 0 && (korean || !task.HasKoreanProgress))
            {
                task.Body = clean;
                task.HasKoreanProgress |= korean;
            }
            // A newly published summary is itself useful live activity feedback.
            if (message.Kind == ProgressKind.Summary && task.State != "PermissionRequest") { task.Detail = "생각하는 중"; task.DetailFromTranscript = false; }
        }
        task.UpdatedAt = message.At;
        return true;
    }

    private bool ApplyFinalAnswer(TaskProgress task, ProgressMessage message)
    {
        if (task.State is "Interrupt" or "SessionEnd") return false;
        if (message.CompletesTurn && task.Active)
            Accept(new("Stop", task.Session, Turn: task.Turn), task.Project);
        if (message.CompletesTurn && message.Text.Length == 0) return true;
        if (task.FinalAnswer == message.Text) return message.CompletesTurn;
        task.FinalAnswer = message.Text;
        task.ResetSummary();
        string excerpt = CompletionExcerpt.Extract(message.Text);
        task.Completion = excerpt.Length > 0 ? excerpt : ProgressDisplayText.Finished("Stop");
        task.CompletionDetail = CompletionExcerpt.Detail(message.Text);
        RefreshCompletions();
        task.UpdatedAt = message.At;
        return true;
    }
}
