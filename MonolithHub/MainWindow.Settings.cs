using System.Windows;
using MonolithHub.Diagnostics;
using MonolithHub.Hardware;
using MonolithHub.Lighting;
using MonolithHub.Shell;

namespace MonolithHub;

public sealed record HotkeyRow(string Keys, string Description);

public partial class MainWindow
{
    bool _loadingSettings;
    readonly HashSet<int> _failedHotkeys = [];
    IReadOnlyList<InfoSection> _systemInfo = [];

    void InitSettings()
    {
        _loadingSettings = true;
        foreach (var radio in LanguagePanel.Children.OfType<System.Windows.Controls.RadioButton>())
            radio.IsChecked = (string)radio.Tag == _appSettings.Language;
        CloseToTrayCheck.IsChecked = _appSettings.CloseToTray;
        HeatAlertsCheck.IsChecked = _appSettings.HeatAlertsEnabled;
        CpuThresholdSlider.Value = _appSettings.CpuAlertThreshold;
        GpuThresholdSlider.Value = _appSettings.GpuAlertThreshold;
        _loadingSettings = false;
        UpdateAlertTexts();
        VersionRun.Text = AppInfo.Version;

        // schtasks prend ~100 ms : hors du thread UI. Une ancienne tâche est mise à jour vers le mode « zone de notification ».
        Task.Run(() =>
            {
                if (LightingService.MigrateLegacyStartupTask())
                    return true;
                var installed = LightingService.IsStartupTaskInstalled();
                if (installed)
                    LightingService.SetStartupTask(true);
                return installed;
            })
            .ContinueWith(t => StartupCheck.IsChecked = t.Result, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ───────────────────────── Langue ─────────────────────────

    void OnLanguageChecked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _loadingSettings)
            return;
        _appSettings.Language = (string)((FrameworkElement)sender).Tag;
        _appSettings.Save();
        Loc.Apply(_appSettings.Language);
    }

    /// <summary>
    /// Les textes du XAML suivent seuls ({l:T}) ; ici, tout ce que le code a écrit lui-même est régénéré.
    /// </summary>
    void OnLanguageChanged()
    {
        UpdateMasterSwitch();
        UpdateTarget();
        UpdateReactiveUi();
        RefreshProfiles();
        UpdateControllerUi();
        OnWindowStateChanged();

        var driver = _driverState;
        _driverState = DriverState.Unknown;
        if (driver != DriverState.Unknown)
            UpdateDriverState(driver == DriverState.Ok);

        // Capteurs et disques : noms et unités dépendent de la langue.
        _sensorRows.Clear();
        _sensorGroups = [];
        DrivesList.Items.Refresh();
        if (_lastSnapshot is not null)
            RenderUi(_lastSnapshot);
        _ = LoadSystemInfoAsync();
    }

    // ───────────────────────── Démarrage et arrière-plan ─────────────────────────

    async void OnStartupClick(object sender, RoutedEventArgs e)
    {
        var enabled = StartupCheck.IsChecked == true;
        var ok = await Task.Run(() => LightingService.SetStartupTask(enabled));
        if (ok)
        {
            ShowToast(Loc.T(enabled ? "Toast_StartupOn" : "Toast_StartupOff"));
            return;
        }
        StartupCheck.IsChecked = !enabled;
        MessageBox.Show(this, Loc.T("Error_Schtasks"), AppInfo.DisplayName,
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    void OnCloseToTrayClick(object sender, RoutedEventArgs e)
    {
        _appSettings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        _appSettings.Save();
    }

    // ───────────────────────── Alertes de chauffe ─────────────────────────

    void OnHeatAlertsClick(object sender, RoutedEventArgs e)
    {
        _appSettings.HeatAlertsEnabled = HeatAlertsCheck.IsChecked == true;
        _appSettings.Save();
        UpdateAlertTexts();
        ShowToast(Loc.T(_appSettings.HeatAlertsEnabled ? "Toast_AlertsOn" : "Toast_AlertsOff"));
    }

    void OnThresholdChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Pendant InitializeComponent, le Minimum du curseur déclenche cet événement : on l'ignore.
        if (!IsInitialized || _loadingSettings)
            return;
        _appSettings.CpuAlertThreshold = (int)CpuThresholdSlider.Value;
        _appSettings.GpuAlertThreshold = (int)GpuThresholdSlider.Value;
        _appSettings.Save();
        UpdateAlertTexts();
    }

    void UpdateAlertTexts()
    {
        CpuThresholdText.Text = $"{_appSettings.CpuAlertThreshold} °C";
        GpuThresholdText.Text = $"{_appSettings.GpuAlertThreshold} °C";
        HeatAlertsBody.IsEnabled = _appSettings.HeatAlertsEnabled;
        HeatAlertsBody.Opacity = _appSettings.HeatAlertsEnabled ? 1 : 0.45;
    }

    void OnTestAlertClick(object sender, RoutedEventArgs e) =>
        _tray?.Notify(Loc.T("Alert_TestTitle"), Loc.F("Alert_TestText", _appSettings.CpuAlertThreshold, _appSettings.GpuAlertThreshold), warning: true);

    // ───────────────────────── À propos ─────────────────────────

    void OnOpenRepository(object sender, RoutedEventArgs e) => AppInfo.OpenUrl(AppInfo.RepositoryUrl);

    void OnOpenLogs(object sender, RoutedEventArgs e) => AppInfo.OpenFolder(Log.Folder);

    void OnCleanupClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Loc.T("Cleanup_Confirm"), Loc.T("Cleanup_Title"),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
            return;
        Cleanup.Run(removeSettings: answer == MessageBoxResult.Yes);
        MessageBox.Show(this, Loc.T("Cleanup_Done"), AppInfo.DisplayName,
            MessageBoxButton.OK, MessageBoxImage.Information);
        Quit();
    }

    // ───────────────────────── Raccourcis clavier ─────────────────────────

    void RegisterHotkeys()
    {
        if (_hotkeys is null)
            return;
        for (var i = 0; i < 9; i++)
        {
            var index = i;
            if (!_hotkeys.RegisterCtrlAlt((char)('1' + i), () => ApplyProfileAt(index)))
                _failedHotkeys.Add(i + 1);
        }
        if (!_hotkeys.RegisterCtrlAlt('0', () => SetLightingEnabled(!_lightingSettings.Enabled)))
            _failedHotkeys.Add(0);
        UpdateHotkeyList();
    }

    void ApplyProfileAt(int index)
    {
        if (index < _lightingSettings.Profiles.Count)
            ApplyProfile(_lightingSettings.Profiles[index].Name);
    }

    void UpdateHotkeyList()
    {
        string Suffix(int key) => _failedHotkeys.Contains(key) ? Loc.T("Hotkey_InUse") : "";
        var rows = new List<HotkeyRow> { new("Ctrl+Alt+0", Loc.T("Hotkey_Toggle") + Suffix(0)) };
        var profiles = _lightingSettings.Profiles;
        for (var i = 0; i < Math.Min(9, profiles.Count); i++)
            rows.Add(new($"Ctrl+Alt+{i + 1}", Loc.F("Hotkey_Profile", profiles[i].Name) + Suffix(i + 1)));
        HotkeyList.ItemsSource = rows;
    }

    // ───────────────────────── Système ─────────────────────────

    async Task LoadSystemInfoAsync()
    {
        _systemInfo = await Task.Run(SystemInfo.Read);

        // Répartition en deux colonnes équilibrées par nombre de lignes.
        List<InfoSection> left = [], right = [];
        int leftRows = 0, rightRows = 0;
        foreach (var section in _systemInfo)
        {
            if (leftRows <= rightRows) { left.Add(section); leftRows += section.Rows.Count + 2; }
            else { right.Add(section); rightRows += section.Rows.Count + 2; }
        }
        SystemLeft.ItemsSource = left;
        SystemRight.ItemsSource = right;

        var model = _systemInfo.FirstOrDefault()?.Rows.FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(model))
            ModelText.Text = model.Replace("OMEN by HP ", "");
    }

    void OnCopySystemInfo(object sender, RoutedEventArgs e)
    {
        var text = string.Join("\n\n", _systemInfo.Select(s =>
            s.Title.ToUpperInvariant() + "\n" + string.Join("\n", s.Rows.Select(r => r.Label.Length > 0 ? $"{r.Label} : {r.Value}" : r.Value))));
        Clipboard.SetText(text);
        ShowToast(Loc.T("Toast_Copied"));
    }
}
