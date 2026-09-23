namespace SecretaryOverlay;

internal sealed record MenuEntry(string Label, Action? Action = null, bool Checked = false, bool Enabled = true, IReadOnlyList<MenuEntry>? Children = null)
{
    public static MenuEntry Separator { get; } = new("");
}

internal static class ModelMenus
{
    public static MenuEntry[] Choices(ModelOption[] catalog, ModelProfile selected, bool images, Action<ModelProfile> choose)
    {
        var result = catalog.Where(model => !images || model.Images).Select(model => new MenuEntry(model.Label,
            Checked: selected.Model == model.Id, Children: model.Efforts.Select(effort => new MenuEntry(ModelCatalog.EffortNames[effort],
                () => choose(new(model.Id, effort)), Checked: selected.Model == model.Id && selected.Reasoning == effort)).ToArray())).ToList();
        if (!result.Any(x => x.Checked)) result.Add(new(ModelCatalog.Describe(selected) + " (현재 선택 · 목록에 없음)", Checked: true, Enabled: false));
        return result.ToArray();
    }
}
