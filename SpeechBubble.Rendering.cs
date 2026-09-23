using System.Windows;

namespace SecretaryOverlay;

internal sealed partial class SpeechBubble
{
    private sealed record CardContent(string Key, string Project, string Body, string Detail, string ChatTitle,
        long Identity, bool CanOpenChat, string Session);

    private List<CardContent> CollectCardContents()
    {
        // Pending AI summaries keep their task slots, but do not render unfinished content.
        var entries = tasks.Take(displayTasks ? MaximumTaskCards : 0)
            .Where(task => !task.WaitingForSummary)
            .Select(task => new CardContent(task.Key, task.Project, task.Body, task.Detail, task.ChatTitle,
                task.BubbleIdentity, task.IsCompleted || task.State == "PermissionRequest", task.Session)).ToList();
        if (entries.Count < MaximumTaskCards && ambient.Length > 0)
            entries.Add(new("ambient", "", ambient, ambientDetail, "", ambientIdentity, false, ""));
        return entries;
    }

    private BubbleCard GetOrCreateCard(string key)
    {
        if (cards.TryGetValue(key, out var card)) return card;
        card = new BubbleCard(() =>
        {
            if (key == "ambient") { ClearAmbient(); Render(); }
            else TaskDismissed?.Invoke(key);
        });
        cards.Add(key, card);
        return card;
    }

    private void Render()
    {
        var previous = CaptureCardBounds();
        var previousOrder = stack.Children.Cast<FrameworkElement>().ToArray();
        bool newPresentation = false;
        rendering = true;
        try
        {
            var entries = CollectCardContents();
            foreach (string stale in cards.Keys.Except(entries.Select(e => e.Key)).ToArray())
            { stack.Children.Remove(cards[stale].Surface); cards.Remove(stale); }
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var card = GetOrCreateCard(entry.Key);
                card.Update(entry.Project, entry.Body, entry.Detail, entry.ChatTitle);
                card.SetPetName(petName);
                card.SetFontSize(appearance.FontSize);
                card.SetNavigation(entry.Session, entry.CanOpenChat,
                    session => ChatRequested?.Invoke(session));
                newPresentation |= card.PreparePresentation(entry.Identity, now(), allowReflow());
                System.Windows.Controls.Panel.SetZIndex(card.Surface, entries.Count - i);
                card.Surface.Margin = new Thickness(0, 0, 0, i < entries.Count - 1 ? 12 : 0);
                if (stack.Children.IndexOf(card.Surface) != i)
                { stack.Children.Remove(card.Surface); stack.Children.Insert(i, card.Surface); }
            }
            UpdateOverflow();
            UpdateAutoClose();
            if (entries.Count == 0) { anchorOffset = 0; Hide(); return; }
            if (!suppressShow) Show();
        }
        finally { rendering = false; }
        bool firstChanged = previousOrder.Length > 0 && stack.Children.Count > 0 && !ReferenceEquals(previousOrder[0], stack.Children[0]);
        Relayout(previous, newPresentation || !previousOrder.SequenceEqual(stack.Children.Cast<FrameworkElement>()), firstChanged);
    }
}
