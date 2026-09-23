namespace SecretaryOverlay;

// New questions enter at the top. Content updates keep their slot; older overflow remains available.
internal sealed partial class ProgressBoard
{
    internal const int RetainedTaskLimit = 32;
    internal const int RetiredTurnLimit = 128;
    private readonly List<TaskProgress> tasks = new();
    // Keep only identities, never completed answers or transcript readers, for late-hook rejection.
    private readonly List<(string Session, string Turn)> retiredTurns = new();
    internal int RetiredTurnCount => retiredTurns.Count;
    public IReadOnlyList<TaskProgress> Tasks => tasks;
    public TaskProgress[] Visible => tasks.Where(t => !t.Dismissed).Take(BubbleCapacity.MaximumVisible).ToArray();
    public bool HasActive => tasks.Any(t => t.Active);
    public CompletionStyle Style { get; set; }
    public TaskProgress? Latest(string session) => tasks.Where(t => t.Session == session).MaxBy(t => t.BubbleIdentity);
    public TaskProgress? Find(string session, string turn) => tasks.FirstOrDefault(t => t.Session == session && t.Turn == turn);
    public bool IsAmbiguousEvent(PetEvent ev) => !ev.Preview && ev.Turn.Length == 0
        && ev.Event is not ("UserPromptSubmit" or "SessionEnd") && tasks.Count(t => t.Session == ev.Session) > 1;
    public bool IsDuplicatePrompt(PetEvent ev) => !ev.Preview && ev.Event == "UserPromptSubmit" && ev.Turn.Length > 0
        && Find(ev.Session, ev.Turn)?.PromptReceived == true;

    public bool IsRetiredEvent(PetEvent ev) => !ev.Preview &&
        ((ev.Turn.Length > 0 && retiredTurns.Contains((ev.Session, ev.Turn))) ||
         (ev.Event != "UserPromptSubmit" && !tasks.Any(t => t.Session == ev.Session)
          && retiredTurns.Any(t => t.Session == ev.Session)));

    public void RefreshCompletions()
    {
        foreach (var task in tasks) task.RefreshCompletion(Style);
    }

    public void Dismiss(string key)
    {
        var task = tasks.FirstOrDefault(t => t.Key == key);
        if (task is not null) task.Dismissed = true;
        PruneCompleted();
    }

    public bool Promote(string key)
    {
        var task = tasks.FirstOrDefault(t => t.Key == key);
        if (task is null) return false;
        task.Dismissed = false;
        tasks.Remove(task);
        tasks.Insert(0, task);
        return true;
    }

    private void PruneCompleted()
    {
        int excess = tasks.Count - RetainedTaskLimit;
        if (excess <= 0) return;
        var visible = Visible.ToHashSet();
        var stale = tasks.Where(t => !t.Active && !visible.Contains(t))
            .OrderBy(t => t.UpdatedAt).Take(excess).ToArray();
        foreach (var task in stale)
        {
            tasks.Remove(task);
            RememberRetired(task.Session, task.Turn);
        }
    }

    private void RememberRetired(string session, string turn)
    {
        var identity = (session, turn);
        retiredTurns.Remove(identity);
        retiredTurns.Add(identity);
        if (retiredTurns.Count > RetiredTurnLimit) retiredTurns.RemoveAt(0);
    }
}
