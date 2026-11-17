using System.Windows;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;

namespace SecretaryOverlay;

internal sealed class ModelSettings : StackPanel
{
    internal ComboBox Model { get; } = new() { MinHeight = 30 };
    internal ComboBox Effort { get; } = new() { MinHeight = 30 };
    private readonly Func<ModelProfile> current;
    private readonly Action<ModelProfile> select;
    private readonly Func<ModelOption[]> read;
    private readonly bool images;
    private ModelOption[] catalog = [];
    private bool syncing;
    private ModelProfile? displayed;

    public ModelSettings(Func<ModelProfile> current, Action<ModelProfile> select, bool images, Func<ModelOption[]>? read = null)
    {
        this.current = current; this.select = select; this.images = images; this.read = read ?? ModelCatalog.Read;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var modelColumn = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        modelColumn.Children.Add(new TextBlock { Text = "사용 모델", Margin = new Thickness(0, 0, 0, 5) });
        modelColumn.Children.Add(Model);
        var effortColumn = new StackPanel();
        effortColumn.Children.Add(new TextBlock { Text = "생각 깊이", Margin = new Thickness(0, 0, 0, 5) });
        effortColumn.Children.Add(Effort);
        Grid.SetColumn(effortColumn, 1); row.Children.Add(modelColumn); row.Children.Add(effortColumn); Children.Add(row);
        System.Windows.Automation.AutomationProperties.SetName(Model, "사용 모델");
        System.Windows.Automation.AutomationProperties.SetName(Effort, "생각 깊이");
        // Refresh the local catalog when opened, without silently changing saved settings.
        Model.DropDownOpened += (_, _) => Sync(true);
        Model.SelectionChanged += (_, _) =>
        {
            if (syncing || Model.SelectedItem is not ComboBoxItem { Tag: string id, IsEnabled: true }) return;
            var option = catalog.First(m => m.Id == id);
            string effort = option.Efforts.Contains(current().Reasoning) ? current().Reasoning
                : option.Efforts.Contains("medium") ? "medium" : option.Efforts[0];
            select(new(id, effort)); Sync();
        };
        Effort.SelectionChanged += (_, _) =>
        {
            if (syncing || Effort.SelectedItem is not ComboBoxItem { Tag: string effort, IsEnabled: true }) return;
            select(new(current().Model, effort)); Sync();
        };
        Sync(true);
    }

    public void Sync(bool refreshCatalog = false)
    {
        if (syncing) return;
        var value = current();
        if (!refreshCatalog && value == displayed) return;
        syncing = true;
        try
        {
            if (refreshCatalog) catalog = read().Where(m => !images || m.Images).ToArray();
            Model.Items.Clear(); Effort.Items.Clear();
            foreach (var model in catalog) Model.Items.Add(new ComboBoxItem { Content = model.Label, Tag = model.Id });
            var option = catalog.FirstOrDefault(m => m.Id == value.Model);
            if (option is null) Model.Items.Add(new ComboBoxItem { Content = ModelCatalog.Label(value.Model) + " (목록에 없음)", Tag = value.Model, IsEnabled = false });
            Model.SelectedItem = Model.Items.Cast<ComboBoxItem>().First(m => (string)m.Tag == value.Model);
            foreach (string effort in option?.Efforts ?? [])
                Effort.Items.Add(new ComboBoxItem { Content = ModelCatalog.EffortNames[effort], Tag = effort });
            if (!Effort.Items.Cast<ComboBoxItem>().Any(e => (string)e.Tag == value.Reasoning))
                Effort.Items.Add(new ComboBoxItem { Content = ModelCatalog.EffortNames.GetValueOrDefault(value.Reasoning, value.Reasoning) + " (현재 선택)", Tag = value.Reasoning, IsEnabled = false });
            Effort.SelectedItem = Effort.Items.Cast<ComboBoxItem>().First(e => (string)e.Tag == value.Reasoning);
            displayed = value;
        }
        finally { syncing = false; }
    }
}
