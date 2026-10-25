using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MonolithHub.Lighting;

namespace MonolithHub.Controls;

public partial class ZoneEditor : UserControl
{
    public static readonly string[] Palette =
    [
        "#FF0000", "#FF3B0F", "#FF7A00", "#FFAA00", "#FFD400", "#FFFF00", "#9DFF00", "#00FF2A", "#00FF95",
        "#00FFE1", "#00C8FF", "#0080FF", "#0026FF", "#7300FF", "#C800FF", "#FF00E6", "#FF0080", "#FFFFFF",
    ];

    ZoneSettings _settings = new();
    bool _loading;

    public event EventHandler? Changed;

    public ZoneEditor()
    {
        InitializeComponent();
        foreach (var hex in Palette)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Tag = hex,
                ToolTip = hex,
                GroupName = $"Swatches-{GetHashCode()}",
            };
            swatch.Click += (_, _) => SetColor(hex, updateHue: true);
            Swatches.Children.Add(swatch);
        }
    }

    public void Load(ZoneSettings settings)
    {
        _settings = settings;
        _loading = true;
        try
        {
            Check(ModePanel, settings.Mode.ToString());
            Check(SpeedPanel, settings.Speed.ToString());
            BrightnessSlider.Value = Math.Max(settings.Brightness, (byte)5);
            BrightnessText.Text = $"{settings.Brightness} %";
            SetColor(settings.Color, updateHue: true);
            UpdateSections();
        }
        finally
        {
            _loading = false;
        }
    }

    static void Check(Panel panel, string tag)
    {
        foreach (var radio in panel.Children.OfType<RadioButton>())
            radio.IsChecked = (string)radio.Tag == tag;
    }

    void SetColor(string hex, bool updateHue)
    {
        var rgb = LightingService.ParseColor(hex);
        _settings.Color = LightingService.ToHex(rgb);
        HexBox.Text = _settings.Color;
        var color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
        ColorPreview.Background = new SolidColorBrush(color);
        BrightnessEnd.Color = color;
        foreach (var swatch in Swatches.Children.OfType<RadioButton>())
            swatch.IsChecked = string.Equals((string)swatch.Tag, _settings.Color, StringComparison.OrdinalIgnoreCase);

        if (updateHue)
        {
            var wasLoading = _loading;
            _loading = true;
            HueSlider.Value = ToHue(rgb);
            _loading = wasLoading;
        }
        RaiseChanged();
    }

    void UpdateSections()
    {
        var mode = _settings.Mode;
        ColorSection.Visibility = mode is LightingMode.Off or LightingMode.ColorCycle ? Visibility.Collapsed : Visibility.Visible;
        CycleHint.Visibility = mode == LightingMode.ColorCycle ? Visibility.Visible : Visibility.Collapsed;
        BrightnessSection.Visibility = mode == LightingMode.Off ? Visibility.Collapsed : Visibility.Visible;
        SpeedSection.Visibility = mode is LightingMode.Breathing or LightingMode.Blinking or LightingMode.ColorCycle
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    void RaiseChanged()
    {
        if (!_loading)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    void OnModeChecked(object sender, RoutedEventArgs e)
    {
        _settings.Mode = Enum.Parse<LightingMode>((string)((RadioButton)sender).Tag);
        UpdateSections();
        RaiseChanged();
    }

    void OnSpeedChecked(object sender, RoutedEventArgs e)
    {
        _settings.Speed = Enum.Parse<LightingSpeed>((string)((RadioButton)sender).Tag);
        RaiseChanged();
    }

    void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _settings.Brightness = (byte)Math.Round(e.NewValue);
        BrightnessText.Text = $"{_settings.Brightness} %";
        RaiseChanged();
    }

    void OnHueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
            return;
        SetColor(LightingService.ToHex(FromHue(e.NewValue)), updateHue: false);
    }

    void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SetColor(HexBox.Text, updateHue: true);
    }

    void OnHexLostFocus(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(HexBox.Text, _settings.Color, StringComparison.OrdinalIgnoreCase))
            SetColor(HexBox.Text, updateHue: true);
    }

    static Rgb FromHue(double hue)
    {
        var x = 1 - Math.Abs(hue / 60 % 2 - 1);
        (double r, double g, double b) = (int)(hue / 60) switch
        {
            0 => (1d, x, 0d),
            1 => (x, 1d, 0d),
            2 => (0d, 1d, x),
            3 => (0d, x, 1d),
            4 => (x, 0d, 1d),
            _ => (1d, 0d, x),
        };
        return new((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    static double ToHue(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        if (delta == 0)
            return 0;
        var hue = max == r ? 60 * ((g - b) / delta % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        return hue < 0 ? hue + 360 : hue;
    }
}
