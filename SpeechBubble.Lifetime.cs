
namespace SecretaryOverlay;

internal sealed partial class SpeechBubble
{
    private void UpdateAutoClose()
    {
        var observations = tasks.Select(t => new BubbleLifetimeObservation(TaskIdentity(t),
            ReadyForCountdown(TaskIdentity(t), t.Key, t.Body, t.CompletionReady))).ToList();
        if (ambient.Length > 0) observations.Add(new(AmbientIdentity,
            ReadyForCountdown(AmbientIdentity, "ambient", ambient, !ambientStreaming)));
        var present = observations.Select(item => item.Key).ToHashSet();
        foreach (string key in finishedPresentations.Keys.Where(key => !present.Contains(key)).ToArray()) finishedPresentations.Remove(key);
        autoClose.Observe(observations, now());
        if (observations.Count > 0) autoCloseTimer.Start(); else autoCloseTimer.Stop();
    }

    private bool ReadyForCountdown(string identity, string cardKey, string body, bool sourceFinished)
    {
        if (!sourceFinished) { finishedPresentations.Remove(identity); return false; }
        // A queued card returning to the screen must not restart an existing completion minute.
        if (finishedPresentations.TryGetValue(identity, out var finished) && finished == body) return true;
        if (cards.TryGetValue(cardKey, out var card) && !card.PresentationComplete)
        { finishedPresentations.Remove(identity); return false; }
        finishedPresentations[identity] = body;
        return true;
    }

    private static string TaskIdentity(TaskProgress task) => task.BubbleIdentity.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private string AmbientIdentity => "ambient/" + ambientIdentity;

    internal void ExpireOlderBubbles()
    {
        AdvanceReflow();
        UpdateAutoClose();
        var expired = autoClose.TakeExpired(now()).ToHashSet();
        if (expired.Count == 0) return;
        bool previousSuppression = suppressShow;
        suppressShow |= !IsVisible;
        try
        {
            var sessions = tasks.Where(t => expired.Contains(TaskIdentity(t))).Select(t => t.Key).ToArray();
            if (expired.Contains(AmbientIdentity))
            {
                // A newer request may be waiting for its first token while the old text is displayed.
                if (ambientShownVersion == ambientVersion) ambientVersion++;
                ambient = ambientDetail = "";
            }
            tasks = tasks.Where(t => !expired.Contains(TaskIdentity(t))).ToArray();
            // One batch avoids promoting and briefly rendering another expired overflow card.
            if (sessions.Length > 0) TasksExpired?.Invoke(sessions);
            suppressShow |= !IsVisible;
            Render();
        }
        finally { suppressShow = previousSuppression; }
    }
}
