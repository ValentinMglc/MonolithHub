using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MonolithHub.Controls;

public sealed class ColorSwatches : WrapPanel
{
    string _selected = "";

    public event EventHandler? Changed;

    public string SelectedColor
    {
        get => _selected;
        set
        {
            _selected = value;
            foreach (var swatch in Children.OfType<RadioButton>())
                swatch.IsChecked = string.Equals((string)swatch.Tag, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    public ColorSwatches()
    {
        Margin = new Thickness(3, 3, 0, 0);
        var group = $"ColorSwatches-{GetHashCode()}";
        foreach (var hex in ZoneEditor.Palette)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Tag = hex,
                ToolTip = hex,
                GroupName = group,
                Width = 24,
                Height = 24,
            };
            swatch.Click += (_, _) =>
            {
                _selected = hex;
                Changed?.Invoke(this, EventArgs.Empty);
            };
            Children.Add(swatch);
        }
    }
}
