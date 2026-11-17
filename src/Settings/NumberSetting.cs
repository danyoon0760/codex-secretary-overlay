using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Slider = System.Windows.Controls.Slider;
using TextBox = System.Windows.Controls.TextBox;

namespace SecretaryOverlay;

internal sealed class NumberSetting : StackPanel
{
    internal Slider Input { get; }
    internal TextBox ValueInput { get; }
    private readonly Func<double> get;
    private readonly Action<double> set;
    private readonly string inputHelp;
    private bool syncing;
    private bool editing;

    public NumberSetting(string title, string unit, double minimum, double maximum, Func<double> get, Action<double> set)
    {
        this.get = get; this.set = set;
        Margin = new Thickness(0, 8, 0, 4);
        inputHelp = $"{minimum:0}~{maximum:0}{unit} 범위의 정수를 입력하세요. Enter 또는 다른 곳을 누르면 적용하고, Escape는 취소해요. 범위를 벗어나면 가장 가까운 값으로 맞춰요.";
        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock { Text = title + ":", VerticalAlignment = VerticalAlignment.Center });
        ValueInput = new TextBox { Width = 72, MinHeight = 28, Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right, ToolTip = inputHelp };
        row.Children.Add(ValueInput);
        if (unit.Length > 0) row.Children.Add(new TextBlock { Text = unit, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Children.Add(row);
        Input = new Slider { Minimum = minimum, Maximum = maximum, Value = get(), TickFrequency = 1,
            IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10, Margin = new Thickness(0, 6, 0, 4) };
        System.Windows.Automation.AutomationProperties.SetName(Input, title);
        System.Windows.Automation.AutomationProperties.SetName(ValueInput, title + " 직접 입력");
        System.Windows.Automation.AutomationProperties.SetHelpText(ValueInput, inputHelp);
        Children.Add(Input);
        Input.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            editing = false;
            set(e.NewValue); Sync();
        };
        ValueInput.TextChanged += (_, _) =>
        {
            if (syncing) return;
            editing = true;
            ValueInput.ToolTip = inputHelp;
        };
        ValueInput.LostKeyboardFocus += (_, _) => Commit();
        ValueInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { editing = false; Sync(); ValueInput.ToolTip = inputHelp; e.Handled = true; }
        };
        Sync();
    }

    private void Commit()
    {
        if (!editing) return;
        bool valid = long.TryParse(ValueInput.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out long value);
        editing = false;
        if (valid)
        {
            double bounded = Math.Clamp((double)value, Input.Minimum, Input.Maximum);
            if (bounded != get()) set(bounded);
        }
        Sync();
        ValueInput.ToolTip = valid ? inputHelp : "정수가 아니어서 값을 바꾸지 않았어요. " + inputHelp;
    }

    public void Sync()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            Input.Value = get();
            if (!editing) ValueInput.Text = Input.Value.ToString("0", CultureInfo.CurrentCulture);
        }
        finally { syncing = false; }
    }
}
