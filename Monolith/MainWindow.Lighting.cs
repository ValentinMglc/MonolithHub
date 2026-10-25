using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Monolith.Hardware;
using Monolith.Lighting;

namespace Monolith;

public partial class MainWindow
{
    static readonly Color DarkLed = Color.FromRgb(0x2A, 0x2B, 0x31);
    /// <summary>Le mode direct est renvoyé régulièrement, au cas où le contrôleur reviendrait à son effet mémorisé.</summary>
    static readonly TimeSpan ReactiveRefresh = TimeSpan.FromSeconds(5);

    readonly LightingSettings _lightingSettings = LightingSettings.Load();
    readonly DispatcherTimer _lightingDebounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer _previewTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Stopwatch _previewClock = Stopwatch.StartNew();
    readonly SolidColorBrush _logoBrush = new(), _barBrush = new();
    OmenLightingDevice? _lighting;
    bool _loadingLighting;

    Rgb? _reactiveColor;
    float? _reactiveTemperature;
    readonly Stopwatch _reactiveSentAgo = new();

    void InitLighting()
    {
        LogoGlyph.Fill = _logoBrush;
        BarGlyph.Fill = _barBrush;

        Editor.Changed += (_, _) => OnManualLightingChange();
        ColdPicker.Changed += (_, _) => OnReactiveColorPicked();
        HotPicker.Changed += (_, _) => OnReactiveColorPicked();
        _lightingDebounce.Tick += (_, _) =>
        {
            _lightingDebounce.Stop();
            ApplyLighting();
        };
        _previewTimer.Tick += (_, _) => UpdatePreview();

        ReloadLightingUi();
        RefreshProfiles();
        UpdatePreview();
        // Se connecte et remet l'éclairage enregistré (notamment au démarrage de Windows).
        InitController();
    }

    /// <summary>Recharge tous les contrôles depuis les réglages (après un profil, le menu de notification…).</summary>
    void ReloadLightingUi()
    {
        _loadingLighting = true;
        MasterSwitch.IsChecked = _lightingSettings.Enabled;
        (_lightingSettings.SyncZones ? TargetBoth : TargetLogo).IsChecked = true;

        var reactive = _lightingSettings.Reactive;
        ReactiveSwitch.IsChecked = reactive.Enabled;
        CheckTag(ReactiveSourcePanel, reactive.Source.ToString());
        CheckTag(ReactiveZonesPanel, reactive.Zones.ToString());
        ColdTempSlider.Value = reactive.ColdTemperature;
        HotTempSlider.Value = reactive.HotTemperature;
        ColdPicker.SelectedColor = reactive.ColdColor;
        HotPicker.SelectedColor = reactive.HotColor;
        _loadingLighting = false;

        UpdateMasterSwitch();
        UpdateTarget();
        UpdateReactiveUi();
    }

    static void CheckTag(Panel panel, string tag)
    {
        foreach (var radio in panel.Children.OfType<RadioButton>())
            radio.IsChecked = (string)radio.Tag == tag;
    }

    // ───────────────────────── Application au contrôleur ─────────────────────────

    /// <summary>Toute modification faite à la main fait sortir du profil actif.</summary>
    void OnManualLightingChange()
    {
        if (_lightingSettings.ActiveProfile is not null)
        {
            _lightingSettings.ActiveProfile = null;
            RefreshProfiles();
        }
        ScheduleLightingUpdate();
    }

    void ScheduleLightingUpdate()
    {
        _lightingDebounce.Stop();
        _lightingDebounce.Start();
    }

    /// <summary>Enregistre et envoie tous les réglages au contrôleur.</summary>
    void ApplyLighting()
    {
        try
        {
            _lightingSettings.Save();
            RecomputeReactiveColor();
            if (_lighting is not null)
            {
                LightingService.ApplyAll(_lighting, _lightingSettings, ReactiveActive ? _reactiveColor : null);
                _reactiveSentAgo.Restart();
            }
            UpdateReactiveUi();
        }
        catch (Exception ex)
        {
            OnControllerError(ex);
        }
    }

    // ───────────────────────── Interrupteur et zones ─────────────────────────

    void OnMasterSwitchClick(object sender, RoutedEventArgs e) => SetLightingEnabled(MasterSwitch.IsChecked == true);

    void SetLightingEnabled(bool enabled)
    {
        _lightingSettings.Enabled = enabled;
        _lightingSettings.ActiveProfile = null;
        ReloadLightingUi();
        RefreshProfiles();
        ApplyLighting();
        ShowToast(enabled ? "Éclairage allumé" : "Éclairage éteint");
    }

    void UpdateMasterSwitch()
    {
        var enabled = _lightingSettings.Enabled;
        MasterSwitchText.Text = enabled ? "Allumé" : "Éteint";
        EditorCard.IsEnabled = enabled;
        EditorCard.Opacity = enabled ? 1 : 0.45;
    }

    enum Target { Both, Logo, Bar }

    Target CurrentTarget =>
        TargetBoth.IsChecked == true ? Target.Both : TargetLogo.IsChecked == true ? Target.Logo : Target.Bar;

    void OnTargetChecked(object sender, RoutedEventArgs e)
    {
        if (_loadingLighting)
            return;
        if (CurrentTarget == Target.Both)
        {
            _lightingSettings.SyncZones = true;
        }
        else if (_lightingSettings.SyncZones)
        {
            // On part du réglage commun : rien ne change visuellement en séparant les zones.
            _lightingSettings.LightBar.CopyFrom(_lightingSettings.Logo);
            _lightingSettings.SyncZones = false;
        }
        UpdateTarget();
        OnManualLightingChange();
    }

    void UpdateTarget()
    {
        var target = CurrentTarget;
        Editor.Load(target == Target.Bar ? _lightingSettings.LightBar : _lightingSettings.Logo);
        (EditorTitle.Text, EditorSubtitle.Text) = target switch
        {
            Target.Both => ("Les deux zones", "Le logo et la barre lumineuse partagent les mêmes réglages."),
            Target.Logo => ("Logo OMEN", "Le losange lumineux en façade du boîtier."),
            _ => ("Barre lumineuse", "La barre en haut de la vitre latérale, qui éclaire l'intérieur du PC."),
        };
        LogoSelection.Opacity = target is Target.Both or Target.Logo ? 1 : 0;
        BarSelection.Opacity = target is Target.Both or Target.Bar ? 1 : 0;
    }

    void OnLogoZoneClick(object sender, MouseButtonEventArgs e) => TargetLogo.IsChecked = true;
    void OnBarZoneClick(object sender, MouseButtonEventArgs e) => TargetBar.IsChecked = true;

    // ───────────────────────── Profils ─────────────────────────

    void RefreshProfiles()
    {
        ProfilesPanel.Children.Clear();
        var profiles = _lightingSettings.Profiles;
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            var chip = new ToggleButton
            {
                Style = (Style)FindResource("ProfileChip"),
                IsChecked = profile.Name == _lightingSettings.ActiveProfile,
                Content = BuildProfileContent(profile, i),
                ContextMenu = BuildProfileMenu(profile),
            };
            chip.Click += (_, _) => ApplyProfile(profile.Name);
            ProfilesPanel.Children.Add(chip);
        }
        UpdateHotkeyList();
    }

    static UIElement BuildProfileContent(LightingProfile profile, int index)
    {
        Brush dot = !profile.Enabled || profile.Logo.Mode == LightingMode.Off
            ? new SolidColorBrush(DarkLed)
            : profile.Logo.Mode == LightingMode.ColorCycle
                ? new LinearGradientBrush(Colors.Red, Colors.Blue, 45)
                : new SolidColorBrush(ToColor(LightingService.ParseColor(profile.Logo.Color)));

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = dot, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold });

        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock
        {
            Text = index < 9 ? $"Ctrl+Alt+{index + 1}" : "Sans raccourci",
            Style = (Style)Application.Current.FindResource("Caption"),
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(18, 3, 0, 0),
        });
        return panel;
    }

    ContextMenu BuildProfileMenu(LightingProfile profile)
    {
        var replace = new MenuItem { Header = "Remplacer par le réglage actuel" };
        replace.Click += (_, _) =>
        {
            var index = _lightingSettings.Profiles.IndexOf(profile);
            _lightingSettings.Profiles[index] = LightingProfile.Capture(profile.Name, _lightingSettings);
            _lightingSettings.ActiveProfile = profile.Name;
            _lightingSettings.Save();
            RefreshProfiles();
            ShowToast($"Profil « {profile.Name} » mis à jour");
        };
        var delete = new MenuItem { Header = "Supprimer" };
        delete.Click += (_, _) =>
        {
            _lightingSettings.Profiles.Remove(profile);
            if (_lightingSettings.ActiveProfile == profile.Name)
                _lightingSettings.ActiveProfile = null;
            _lightingSettings.Save();
            RefreshProfiles();
            ShowToast($"Profil « {profile.Name} » supprimé");
        };
        return new ContextMenu { Items = { replace, delete } };
    }

    void ApplyProfile(string name)
    {
        var profile = _lightingSettings.Profiles.FirstOrDefault(p => p.Name == name);
        if (profile is null)
            return;
        profile.ApplyTo(_lightingSettings);
        ReloadLightingUi();
        RefreshProfiles();
        ApplyLighting();
        ShowToast($"Profil « {name} » appliqué");
    }

    void OnSaveProfileClick(object sender, RoutedEventArgs e)
    {
        ProfileNameEditor.Visibility = Visibility.Visible;
        SaveProfileButton.Visibility = Visibility.Collapsed;
        ProfileNameBox.Text = "";
        ProfileNameBox.Focus();
    }

    void OnCancelProfileClick(object sender, RoutedEventArgs e) => CloseProfileEditor();

    void CloseProfileEditor()
    {
        ProfileNameEditor.Visibility = Visibility.Collapsed;
        SaveProfileButton.Visibility = Visibility.Visible;
    }

    void OnProfileNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnConfirmProfileClick(sender, e);
        else if (e.Key == Key.Escape)
            CloseProfileEditor();
    }

    void OnProfileNameChanged(object sender, TextChangedEventArgs e) =>
        ProfileNameHint.Visibility = ProfileNameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void OnConfirmProfileClick(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (name.Length == 0)
            return;
        var profile = LightingProfile.Capture(name, _lightingSettings);
        var existing = _lightingSettings.Profiles.FindIndex(p => p.Name == name);
        if (existing >= 0)
            _lightingSettings.Profiles[existing] = profile;
        else
            _lightingSettings.Profiles.Add(profile);
        _lightingSettings.ActiveProfile = name;
        _lightingSettings.Save();
        CloseProfileEditor();
        RefreshProfiles();
        ShowToast($"Profil « {name} » enregistré");
    }

    // ───────────────────────── Couleur selon la température ─────────────────────────

    bool ReactiveActive => _lightingSettings.Enabled && _lightingSettings.Reactive.Enabled;

    void RecomputeReactiveColor()
    {
        _reactiveTemperature = _lastSnapshot is null
            ? null
            : LightingService.SourceTemperature(_lightingSettings.Reactive, _lastSnapshot.CpuTemp, _lastSnapshot.GpuTemp);
        _reactiveColor = _reactiveTemperature is { } t ? LightingService.ReactiveColor(_lightingSettings.Reactive, t) : null;
    }

    /// <summary>Appelé à chaque mesure : n'envoie au contrôleur que si la couleur a vraiment changé.</summary>
    void UpdateReactive(HardwareSnapshot s)
    {
        var previous = _reactiveColor;
        RecomputeReactiveColor();
        if (ReactiveActive && _reactiveColor is { } color && _lighting is not null)
        {
            var changed = previous is not { } last || Math.Abs(last.R - color.R) + Math.Abs(last.G - color.G) + Math.Abs(last.B - color.B) >= 6;
            if (changed || _reactiveSentAgo.Elapsed > ReactiveRefresh)
            {
                try
                {
                    foreach (var zone in new[] { LightingZone.Logo, LightingZone.LightBar })
                        if (LightingService.IsReactive(_lightingSettings, zone))
                            _lighting.SetDirect(zone, color, Math.Max(LightingService.Effective(_lightingSettings, zone).Brightness, (byte)10));
                    _reactiveSentAgo.Restart();
                }
                catch (Exception ex)
                {
                    OnControllerError(ex);
                }
            }
        }
        if (_uiVisible)
            UpdateReactiveUi();
    }

    void OnReactiveSwitchClick(object sender, RoutedEventArgs e) => SetReactiveEnabled(ReactiveSwitch.IsChecked == true);

    void SetReactiveEnabled(bool enabled)
    {
        _lightingSettings.Reactive.Enabled = enabled;
        ReloadLightingUi();
        ApplyLighting();
        ShowToast(enabled ? "Couleur selon la température activée" : "Couleur selon la température désactivée");
    }

    void OnReactiveOptionChecked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _loadingLighting)
            return;
        var reactive = _lightingSettings.Reactive;
        var source = ReactiveSourcePanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        var zones = ReactiveZonesPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (source is not null)
            reactive.Source = Enum.Parse<TemperatureSource>((string)source.Tag);
        if (zones is not null)
            reactive.Zones = Enum.Parse<ReactiveZones>((string)zones.Tag);
        ScheduleLightingUpdate();
    }

    void OnReactiveTempChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Pendant InitializeComponent, le Minimum du curseur déclenche cet événement : on l'ignore.
        if (!IsInitialized || _loadingLighting)
            return;
        // Garde au moins 5 °C entre « froid » et « chaud ».
        if (sender == ColdTempSlider && HotTempSlider.Value < ColdTempSlider.Value + 5)
            HotTempSlider.Value = Math.Min(HotTempSlider.Maximum, ColdTempSlider.Value + 5);
        else if (sender == HotTempSlider && ColdTempSlider.Value > HotTempSlider.Value - 5)
            ColdTempSlider.Value = Math.Max(ColdTempSlider.Minimum, HotTempSlider.Value - 5);
        _lightingSettings.Reactive.ColdTemperature = (int)ColdTempSlider.Value;
        _lightingSettings.Reactive.HotTemperature = (int)HotTempSlider.Value;
        UpdateReactiveUi();
        ScheduleLightingUpdate();
    }

    void OnReactiveColorPicked()
    {
        _lightingSettings.Reactive.ColdColor = ColdPicker.SelectedColor;
        _lightingSettings.Reactive.HotColor = HotPicker.SelectedColor;
        UpdateReactiveUi();
        ScheduleLightingUpdate();
    }

    void UpdateReactiveUi()
    {
        var reactive = _lightingSettings.Reactive;
        ReactiveBody.IsEnabled = reactive.Enabled;
        ReactiveBody.Opacity = reactive.Enabled ? 1 : 0.45;
        ReactiveColdStop.Color = ToColor(LightingService.ParseColor(reactive.ColdColor));
        ReactiveHotStop.Color = ToColor(LightingService.ParseColor(reactive.HotColor));
        ColdTempText.Text = $"{reactive.ColdTemperature} °C";
        HotTempText.Text = $"{reactive.HotTemperature} °C";
        ReactiveColdLabel.Text = $"≤ {reactive.ColdTemperature} °C";
        ReactiveHotLabel.Text = $"≥ {reactive.HotTemperature} °C";

        if (_reactiveTemperature is { } t)
        {
            var source = reactive.Source switch
            {
                TemperatureSource.Cpu => "processeur",
                TemperatureSource.Gpu => "carte graphique",
                _ => "le plus chaud",
            };
            ReactiveNowText.Text = $"Actuellement {t:0} °C ({source})";
            var f = Math.Clamp((t - reactive.ColdTemperature) / Math.Max(1.0, reactive.HotTemperature - reactive.ColdTemperature), 0, 1);
            Canvas.SetLeft(ReactiveMarker, f * Math.Max(0, ReactiveTrack.ActualWidth - ReactiveMarker.Width));
            ReactiveMarker.Fill = new SolidColorBrush(ToColor(LightingService.ReactiveColor(reactive, t)));
            ReactiveMarker.Visibility = Visibility.Visible;
        }
        else
        {
            ReactiveNowText.Text = "En attente d'une température…";
            ReactiveMarker.Visibility = Visibility.Collapsed;
        }
    }

    // ───────────────────────── Aperçu animé ─────────────────────────

    /// <summary>Simule à l'écran l'effet envoyé aux LED.</summary>
    void UpdatePreview()
    {
        var t = _previewClock.Elapsed.TotalSeconds;
        RenderLed(LightingZone.Logo, t, _logoBrush, LogoGlow, null);
        RenderLed(LightingZone.LightBar, t, _barBrush, BarGlow, BarAmbientTop);
    }

    void RenderLed(LightingZone zoneId, double t, SolidColorBrush fill, DropShadowEffect glow, GradientStop? ambient)
    {
        var zone = LightingService.Effective(_lightingSettings, zoneId);
        var period = zone.Speed switch { LightingSpeed.Slow => 4.0, LightingSpeed.Fast => 1.2, _ => 2.4 };
        var intensity = zone.Mode == LightingMode.Off ? 0 : zone.Brightness / 100.0;
        Color color;

        if (LightingService.IsReactive(_lightingSettings, zoneId) && _reactiveColor is { } reactive)
        {
            color = ToColor(reactive);
            intensity = Math.Max(zone.Brightness, (byte)10) / 100.0;
        }
        else
        {
            switch (zone.Mode)
            {
                case LightingMode.ColorCycle:
                    var colors = LightingService.CycleColors;
                    var position = t / period % colors.Count;
                    var from = colors[(int)position];
                    var to = colors[((int)position + 1) % colors.Count];
                    color = Lerp(ToColor(from), ToColor(to), position - Math.Floor(position));
                    break;
                case LightingMode.Breathing:
                    color = ToColor(LightingService.ParseColor(zone.Color));
                    intensity *= 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / period);
                    break;
                case LightingMode.Blinking:
                    color = ToColor(LightingService.ParseColor(zone.Color));
                    intensity *= t % period < period / 2 ? 1 : 0;
                    break;
                default:
                    color = ToColor(LightingService.ParseColor(zone.Color));
                    break;
            }
        }

        fill.Color = Lerp(DarkLed, color, intensity);
        glow.Color = color;
        glow.Opacity = intensity;
        if (ambient is not null)
            ambient.Color = Color.FromArgb((byte)(0x55 * intensity), color.R, color.G, color.B);
    }

    static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    static Color Lerp(Color a, Color b, double f) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));
}
