namespace SecretaryOverlay;

internal enum ModelFeature { Automatic, Manual, Completion }

public sealed partial class PetWindow
{
    internal MenuEntry[] FeatureMenus()
    {
        var catalog = ModelCatalog.Read();
        MenuEntry[] entries = [
            new("자동으로 말하기", Checked: commentary.Enabled, Children: new[] {
                new MenuEntry("자동으로 말하기", ToggleCommentary, Checked: commentary.Enabled), MenuEntry.Separator
            }.Concat(ModelMenus.Choices(catalog, automaticModel, true, profile => SetModelProfile(ModelFeature.Automatic, profile))).ToArray()),
            new("화면 보고 한마디", Children: new[] {
                new MenuEntry(commentary.Busy ? "한마디 준비 중…" : HasCommentarySlot ? "지금 말하기" : "말풍선 하나를 닫아 주세요",
                    () => _ = SpeakAsync(true), Enabled: !commentary.Busy && HasCommentarySlot), MenuEntry.Separator
            }.Concat(ModelMenus.Choices(catalog, manualModel, true, profile => SetModelProfile(ModelFeature.Manual, profile))).ToArray()),
            new("작업 상황 말풍선 표시", () => SetProgressEnabled(!showProgress), Checked: showProgress),
            new("완료 메시지: 원문에서 간추리기", () => SetCompletionStyle(CompletionStyle.B), Checked: progressBoard.Style == CompletionStyle.B),
            new("완료 메시지: AI로 요약하기", Checked: progressBoard.Style == CompletionStyle.D, Children: new[] {
                new MenuEntry("AI로 요약하기 사용", () => SetCompletionStyle(CompletionStyle.D), Checked: progressBoard.Style == CompletionStyle.D), MenuEntry.Separator
            }.Concat(ModelMenus.Choices(catalog, completionModel, false, profile => SetModelProfile(ModelFeature.Completion, profile))).ToArray())
        ];
        var others = showProgress ? OtherTaskMenu.Create(progressBoard.Tasks, ShowOtherTask) : null;
        return others is null ? entries : [.. entries, others];
    }

    private void SetModelProfile(ModelFeature feature, ModelProfile profile)
    {
        var selected = ModelProfile.Validated(profile);
        switch (feature)
        {
            case ModelFeature.Automatic: automaticModel = selected; break;
            case ModelFeature.Manual: manualModel = selected; break;
            case ModelFeature.Completion: completionModel = selected; break;
        }
        // Requests already in flight retain their captured profile; selecting does not run or enable anything.
        CompletionSettings.Sync(completionSettingsPanel, progressBoard.Style);
        SaveLayout();
        WriteStatus();
    }

    private System.Windows.Forms.ToolStripItem TrayEntry(MenuEntry entry)
    {
        if (entry.Label.Length == 0) return new System.Windows.Forms.ToolStripSeparator();
        var item = new System.Windows.Forms.ToolStripMenuItem(entry.Label) { Checked = entry.Checked, Enabled = entry.Enabled };
        if (entry.Children is not null) foreach (var child in entry.Children) item.DropDownItems.Add(TrayEntry(child));
        else if (entry.Action is not null) item.Click += (_, _) => Dispatcher.Invoke(entry.Action);
        return item;
    }
}
