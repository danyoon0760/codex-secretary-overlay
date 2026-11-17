using System.IO;
using System.Text.Json;

namespace SecretaryOverlay;

internal sealed record PersonalizationDraft(PetPersonalization Saved, PetPersonalization Value);

// Drafts deliberately bypass normalization: an empty editor is still an unsaved edit.
internal sealed class PersonalizationDraftStorage(string? path = null)
{
    internal string FilePath { get; } = path ?? Path.Combine(AppStorage.DataDirectory, "personalization-draft.json");

    public PersonalizationDraft? Load()
    {
        try
        {
            var draft = JsonSerializer.Deserialize<PersonalizationDraft>(File.ReadAllText(FilePath));
            return draft is not null && Valid(draft.Saved) && Valid(draft.Value) ? draft : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public bool Save(PersonalizationDraft draft)
    {
        if (!Valid(draft.Saved) || !Valid(draft.Value)) return false;
        return AtomicJsonFile.Save(FilePath, draft);
    }

    public bool ClearMatching(PersonalizationDraft expected)
    {
        try
        {
            var current = JsonSerializer.Deserialize<PersonalizationDraft>(File.ReadAllText(FilePath));
            if (current == expected) File.Delete(FilePath);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    private static bool Valid(PetPersonalization? value) => value is not null
        && value.Name is not null && value.Name.Length <= PetPersonalization.NameLimit
        && value.CommentaryInstructions is not null && value.CommentaryInstructions.Length <= PetPersonalization.InstructionsLimit
        && value.CompletionInstructions is not null && value.CompletionInstructions.Length <= PetPersonalization.InstructionsLimit;
}
