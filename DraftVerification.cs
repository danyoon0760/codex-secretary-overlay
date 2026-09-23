using System.IO;
using System.Windows;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace SecretaryOverlay;

internal static class DraftVerification
{
    public static void Check(Action<bool, string> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "pet-draft-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "draft.json");
        var store = new PersonalizationDraftStorage(path);
        var blank = new PersonalizationDraft(PetPersonalization.Default, new("", "\n ", ""));
        try
        {
            check(store.Load() is null && store.ClearMatching(blank), "An installation without a draft opens its saved personalization");
            check(store.Save(blank) && new PersonalizationDraftStorage(path).Load() == blank,
                "Draft storage preserves empty fields and raw whitespace across a fresh store instance");
            var newer = blank with { Value = blank.Value with { Name = "나중에 작성한 이름" } };
            store.Save(newer);
            check(store.ClearMatching(blank) && store.Load() == newer,
                "Clearing an earlier draft does not delete a different newer draft");
            check(store.ClearMatching(newer) && store.Load() is null, "Matching drafts are removed after being applied");
            File.WriteAllText(path, "{broken");
            check(store.Load() is null, "Malformed draft files do not block settings startup");
            check(!new PersonalizationDraftStorage(folder).Save(blank), "A draft persistence failure is reported without leaving temporary files");
            File.Delete(path);

            int applies = 0;
            bool success = false;
            var active = PetPersonalization.Default;
            bool Apply(PetPersonalization value) { applies++; if (!success) return false; active = value; return true; }
            void Click(System.Windows.Controls.Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var editor = new PersonalizationSettings(active, Apply, store);
            editor.PetName.Text = ""; editor.Commentary.Text = ""; editor.Completion.Text = "새로 적고 있는 요약 지침\n";
            editor.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            check(applies == 0 && active == PetPersonalization.Default && store.Load()?.Value.CommentaryInstructions == "",
                "Closing an editor flushes unsaved text separately without applying it");
            var reopened = new PersonalizationSettings(active, Apply, new PersonalizationDraftStorage(path));
            check(reopened.PetName.Text == "" && reopened.Commentary.Text == "" && reopened.Completion.Text == "새로 적고 있는 요약 지침\n"
                && reopened.Status.Text.Contains("작성 내용을 불러"),
                "A reopened settings editor restores its complete raw draft and identifies it as unapplied");
            Click(reopened.Save);
            check(applies == 1 && active == PetPersonalization.Default && store.Load()?.Value.CompletionInstructions == "새로 적고 있는 요약 지침\n",
                "A failed explicit Save retains the draft and leaves the active settings unchanged");
            success = true; Click(reopened.Save);
            check(applies == 2 && active.Name == PetPersonalization.DefaultName && active.CommentaryInstructions == PetPersonalization.DefaultCommentary
                && active.CompletionInstructions == "새로 적고 있는 요약 지침" && store.Load() is null,
                "Successful Save normalizes the applied settings and clears the matching raw draft");

            store.Save(blank);
            var changedSettings = new PersonalizationSettings(active, Apply, store);
            check(changedSettings.Completion.Text == active.CompletionInstructions,
                "A draft based on superseded saved settings is not restored over newly applied values");
            store.ClearMatching(blank);
            var undoEditor = new PersonalizationSettings(active, Apply, store);
            undoEditor.PetName.Text = "다른 이름"; undoEditor.FlushDraft();
            undoEditor.PetName.Text = active.Name; undoEditor.FlushDraft();
            check(store.Load() is null, "Returning all editor fields to their saved values clears an obsolete draft");
            var failingEditor = new PersonalizationSettings(active, Apply, new PersonalizationDraftStorage(folder));
            failingEditor.PetName.Text = "보관 실패"; failingEditor.FlushDraft();
            check(failingEditor.PetName.Text == "보관 실패" && failingEditor.Status.Text.Contains("임시 보관하지 못"),
                "A draft storage failure keeps the edit visible and tells the user it was not preserved");
            check(Directory.GetFiles(folder, "*.tmp").Length == 0, "Draft writes clean up their temporary files on failure");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(folder);
        }
    }
}
