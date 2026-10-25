using System.Windows;
using Monolith.Diagnostics;
using Monolith.Hardware;
using Monolith.Lighting;
using Monolith.Shell;

namespace Monolith;

public sealed record HotkeyRow(string Keys, string Description);

public partial class MainWindow
{
    bool _loadingSettings;
    readonly HashSet<int> _failedHotkeys = [];
    IReadOnlyList<InfoSection> _systemInfo = [];

    void InitSettings()
    {
        _loadingSettings = true;
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

    // ───────────────────────── Démarrage et arrière-plan ─────────────────────────

    async void OnStartupClick(object sender, RoutedEventArgs e)
    {
        var enabled = StartupCheck.IsChecked == true;
        var ok = await Task.Run(() => LightingService.SetStartupTask(enabled));
        if (ok)
        {
            ShowToast(enabled ? "Monolith démarrera avec Windows" : "Démarrage automatique désactivé");
            return;
        }
        StartupCheck.IsChecked = !enabled;
        MessageBox.Show(this, "Impossible de modifier la tâche planifiée Windows.", "Monolith",
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
        ShowToast(_appSettings.HeatAlertsEnabled ? "Alertes de chauffe activées" : "Alertes de chauffe désactivées");
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
        _tray?.Notify("Test d'alerte de chauffe", $"Voilà à quoi ressemblera une alerte (seuils : CPU {_appSettings.CpuAlertThreshold} °C, GPU {_appSettings.GpuAlertThreshold} °C).", warning: true);

    // ───────────────────────── À propos ─────────────────────────

    void OnOpenRepository(object sender, RoutedEventArgs e) => AppInfo.OpenUrl(AppInfo.RepositoryUrl);

    void OnOpenLogs(object sender, RoutedEventArgs e) => AppInfo.OpenFolder(Log.Folder);

    void OnCleanupClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Monolith va retirer sa tâche de démarrage de Windows puis se fermer.\n\n" +
            "Voulez-vous aussi supprimer vos réglages (profils, couleurs, seuils) ?\n\n" +
            "Oui : tout supprimer      Non : garder les réglages      Annuler : ne rien faire",
            "Retirer Monolith de Windows", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
            return;
        Cleanup.Run(removeSettings: answer == MessageBoxResult.Yes);
        MessageBox.Show(this, "C'est fait. Vous pouvez maintenant supprimer le dossier de Monolith.", "Monolith",
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
        string Suffix(int key) => _failedHotkeys.Contains(key) ? " (déjà utilisé par une autre application)" : "";
        var rows = new List<HotkeyRow> { new("Ctrl+Alt+0", "Allumer / éteindre l'éclairage" + Suffix(0)) };
        var profiles = _lightingSettings.Profiles;
        for (var i = 0; i < Math.Min(9, profiles.Count); i++)
            rows.Add(new($"Ctrl+Alt+{i + 1}", $"Profil « {profiles[i].Name} »" + Suffix(i + 1)));
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
        ShowToast("Informations copiées dans le presse-papiers");
    }
}
