using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MonolithHub.Controls;
using MonolithHub.Hardware;
using MonolithHub.Shell;

namespace MonolithHub;

public partial class MainWindow
{
    static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    CancellationTokenSource? _monitorCts;
    Task _monitorTask = Task.CompletedTask;
    HardwareSnapshot? _lastSnapshot;

    readonly History _cpuHistory = new(), _gpuHistory = new(), _ramHistory = new(), _downHistory = new(), _upHistory = new();
    readonly HeatAlert _cpuAlert = new("processeur"), _gpuAlert = new("carte graphique");

    readonly Dictionary<string, SensorRow> _sensorRows = [];
    List<SensorGroup> _sensorGroups = [];

    enum DriverState { Unknown, Ok, Missing, Unresponsive, Installing }
    DriverState _driverState = DriverState.Unknown;
    bool _installingDriver;

    void StartMonitor()
    {
        _monitorCts = new CancellationTokenSource();
        var ct = _monitorCts.Token;
        _monitorTask = Task.Run(() => MonitorLoop(ct));
    }

    void StopMonitor() => _monitorCts?.Cancel();

    async Task RestartMonitorAsync()
    {
        _monitorCts?.Cancel();
        try { await _monitorTask; } catch (OperationCanceledException) { }
        StartMonitor();
    }

    async Task MonitorLoop(CancellationToken ct)
    {
        using var monitor = new HardwareMonitor();
        monitor.Open();

        using var timer = new PeriodicTimer(RefreshInterval);
        var hiddenTicks = 0;
        try
        {
            do
            {
                var visible = _uiVisible;
                if (!visible && hiddenTicks++ % 2 == 1)
                    continue;
                var snapshot = monitor.Sample(detailed: visible);
                await Dispatcher.InvokeAsync(() => OnSnapshot(snapshot), DispatcherPriority.Background, ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
        }
    }

    void OnSnapshot(HardwareSnapshot s)
    {
        if (_quitting)
            return;
        _lastSnapshot = s;

        _cpuHistory.Add(s.CpuLoad);
        _gpuHistory.Add(s.GpuLoad);
        _ramHistory.Add(s.RamLoad);
        _downHistory.Add(s.DownloadMbps);
        _upHistory.Add(s.UploadMbps);

        _tray?.SetTooltip($"{AppInfo.DisplayName}\nCPU {Temperature(s.CpuTemp)} · {Percent(s.CpuLoad)}\nGPU {Temperature(s.GpuTemp)} · {Percent(s.GpuLoad)}");
        CheckHeatAlerts(s);
        UpdateReactive(s);
        UpdateDriverState(s.LowLevelAccess);

        if (_uiVisible)
            RenderUi(s);
    }

    void CheckHeatAlerts(HardwareSnapshot s)
    {
        if (!_appSettings.HeatAlertsEnabled)
            return;
        if (_cpuAlert.Check(s.CpuTemp, _appSettings.CpuAlertThreshold))
            _tray?.Notify(Loc.T("Alert_CpuTitle"), Loc.F("Alert_Text", s.CpuTemp, _appSettings.CpuAlertThreshold), warning: true);
        if (_gpuAlert.Check(s.GpuTemp, _appSettings.GpuAlertThreshold))
            _tray?.Notify(Loc.T("Alert_GpuTitle"), Loc.F("Alert_Text", s.GpuTemp, _appSettings.GpuAlertThreshold), warning: true);
    }

    void RenderUi(HardwareSnapshot s)
    {
        LiveText.Text = Loc.T("Dash_Live");

        CpuNameText.Text = s.CpuName;
        CpuGauge.Value = s.CpuLoad ?? 0;
        CpuLoadText.Text = Percent(s.CpuLoad);
        SetTemperature(CpuTempText, s.CpuTemp);
        CpuClockText.Text = s.CpuClock is > 0 ? $"{s.CpuClock / 1000:0.00} GHz" : "-";
        CpuPowerText.Text = s.CpuPower is > 0 ? $"{s.CpuPower:0} W" : "-";
        CpuSpark.SetValues(_cpuHistory.Values);

        GpuNameText.Text = s.GpuName.Replace("NVIDIA ", "");
        GpuGauge.Value = s.GpuLoad ?? 0;
        GpuLoadText.Text = Percent(s.GpuLoad);
        SetTemperature(GpuTempText, s.GpuTemp);
        GpuVramText.Text = s.GpuMemoryUsedMb is > 0 ? $"{s.GpuMemoryUsedMb / 1024:0.0} / {s.GpuMemoryTotalMb / 1024:0} {Gb}" : "-";
        GpuClockText.Text = s.GpuClock is > 0 ? $"{s.GpuClock:0} MHz" : "-";
        GpuPowerText.Text = s.GpuPower is > 0 ? $"{s.GpuPower:0} W" : "-";
        GpuSpark.SetValues(_gpuHistory.Values);

        RamTotalText.Text = $"{s.RamTotalGb:0} {Gb}";
        RamGauge.Value = s.RamLoad;
        RamLoadText.Text = $"{s.RamLoad:0}%";
        RamUsedText.Text = $"{s.RamUsedGb:0.0} {Gb}";
        RamFreeText.Text = $"{s.RamTotalGb - s.RamUsedGb:0.0} {Gb}";
        RamSpark.SetValues(_ramHistory.Values);

        SetRate(DownText, DownUnit, s.DownloadMbps);
        SetRate(UpText, UpUnit, s.UploadMbps);
        var netMax = Math.Max(1, Math.Max(_downHistory.Values.Max(), _upHistory.Values.Max())) * 1.2;
        DownSpark.Maximum = UpSpark.Maximum = netMax;
        DownSpark.SetValues(_downHistory.Values);
        UpSpark.SetValues(_upHistory.Values);

        if (!ReferenceEquals(DrivesList.ItemsSource, s.Drives))
            DrivesList.ItemsSource = s.Drives;
        if (s.TopProcesses is { } processes)
        {
            var maxCpu = Math.Max(5, processes.Count > 0 ? processes.Max(p => p.CpuPercent) : 0);
            ProcessList.ItemsSource = processes
                .Select(p => new ProcessRow(p.Name, p.CpuPercent / maxCpu * 100, $"{p.CpuPercent:0.0} %",
                    p.RamMb >= 1024 ? $"{p.RamMb / 1024:0.0} {Gb}" : $"{p.RamMb:0} {Loc.T("Unit_MB")}"))
                .ToList();
        }

        MiniCpuBar.Value = s.CpuLoad ?? 0;
        MiniCpuText.Text = JoinInline(Percent(s.CpuLoad), s.CpuTemp is > 0 ? $"{s.CpuTemp:0} °C" : null);
        MiniGpuBar.Value = s.GpuLoad ?? 0;
        MiniGpuText.Text = JoinInline(Percent(s.GpuLoad), s.GpuTemp is > 0 ? $"{s.GpuTemp:0} °C" : null);
        MiniRamBar.Value = s.RamLoad;
        MiniRamText.Text = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} {Gb}";

        if (SensorsPage.Visibility == Visibility.Visible || _sensorGroups.Count == 0)
            RenderSensors(s.Sensors);
    }

    void SetTemperature(TextBlock text, float? value)
    {
        text.Text = Temperature(value);
        text.Foreground = (Brush)FindResource(value switch
        {
            null or <= 0 => "SubtleTextBrush",
            >= 85 => "AccentBrush",
            >= 70 => "WarnBrush",
            _ => "GoodBrush",
        });
    }

    static void SetRate(Run value, Run unit, double mbps)
    {
        (value.Text, unit.Text) = mbps switch
        {
            >= 1000 => ((mbps / 1000).ToString("0.0"), "Gb/s"),
            >= 1 => (mbps.ToString(mbps < 10 ? "0.0" : "0"), "Mb/s"),
            _ => ((mbps * 1000).ToString("0"), "kb/s"),
        };
    }

    static string Gb => Loc.T("Unit_GB");
    static string Percent(float? value) => value is null ? "-" : $"{value:0}%";
    static string Temperature(float? value) => value is > 0 ? $"{value:0} °C" : "-";
    static string JoinInline(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    void UpdateDriverState(bool lowLevelAccess)
    {
        var state = lowLevelAccess ? DriverState.Ok
            : _installingDriver ? DriverState.Installing
            : PawnIoDriver.IsInstalled() ? DriverState.Unresponsive
            : DriverState.Missing;
        if (state == _driverState)
            return;
        _driverState = state;

        DriverBanner.Visibility = state == DriverState.Ok ? Visibility.Collapsed : Visibility.Visible;
        (DriverBannerTitle.Text, DriverBannerText.Text, DriverBannerButton.Content) = state switch
        {
            DriverState.Missing => (Loc.T("Driver_MissingTitle"), Loc.T("Driver_MissingText"), Loc.T("Driver_Install")),
            DriverState.Unresponsive => (Loc.T("Driver_UnresponsiveTitle"), Loc.T("Driver_UnresponsiveText"), Loc.T("Driver_Recheck")),
            DriverState.Installing => (Loc.T("Driver_InstallingTitle"), Loc.T("Driver_InstallingText"), Loc.T("Driver_Wait")),
            _ => ("", "", ""),
        };
        DriverBannerButton.IsEnabled = PawnButton.IsEnabled = state != DriverState.Installing;

        PawnDot.Fill = (Brush)FindResource(state == DriverState.Ok ? "GoodBrush" : "WarnBrush");
        PawnStatusText.Text = state switch
        {
            DriverState.Ok => Loc.T("Pawn_Ok"),
            DriverState.Missing => Loc.T("Pawn_Missing"),
            DriverState.Unresponsive => Loc.T("Pawn_Unresponsive"),
            _ => Loc.T("Pawn_Installing"),
        };
        PawnButton.Content = state == DriverState.Missing ? Loc.T("Pawn_InstallShort") : Loc.T("Driver_Recheck");
    }

    async void OnDriverAction(object sender, RoutedEventArgs e)
    {
        if (_driverState == DriverState.Missing)
        {
            _installingDriver = true;
            UpdateDriverState(false);
            var installed = await PawnIoDriver.InstallAsync();
            _installingDriver = false;
            if (!installed)
            {
                OpenUrl(PawnIoDriver.Website);
                ShowToast(Loc.T("Toast_PawnFailed"));
            }
            else
            {
                ShowToast(Loc.T("Toast_PawnInstalled"));
            }
        }
        else
        {
            ShowToast(Loc.T("Toast_SensorsRestarted"));
        }
        _driverState = DriverState.Unknown;
        await RestartMonitorAsync();
    }

    static readonly string[] SectionOrder =
        ["Temperature", "Load", "Clock", "Power", "Fan", "Control", "Voltage", "Current", "Data", "SmallData", "Throughput", "Energy"];

    void RenderSensors(IReadOnlyList<SensorReading> readings)
    {
        if (readings.DistinctBy(r => r.Id).Count() != _sensorRows.Count || readings.Any(r => !_sensorRows.ContainsKey(r.Id)))
        {
            _sensorRows.Clear();
            _sensorGroups = readings
                .DistinctBy(r => r.Id)
                .GroupBy(r => (r.Hardware, r.HardwareKind))
                .Select(g => new SensorGroup(TranslateSensorName(g.Key.Hardware), HardwareGlyph(g.Key.HardwareKind), g
                    .GroupBy(r => r.Type)
                    .OrderBy(t => Array.IndexOf(SectionOrder, t.Key) is var i and >= 0 ? i : 99)
                    .Select(t => new SensorSection(SectionTitle(t.Key),
                        t.Select(r => _sensorRows[r.Id] = new SensorRow(TranslateSensorName(r.Name))).ToList()))
                    .ToList()))
                .ToList();
            SensorGroups.ItemsSource = _sensorGroups;
        }

        var availabilityChanged = false;
        foreach (var reading in readings)
        {
            var row = _sensorRows[reading.Id];
            var hasValue = reading.Value is { } v && !float.IsNaN(v);
            availabilityChanged |= row.HasValue != hasValue;
            row.HasValue = hasValue;
            row.Value = FormatSensor(reading.Type, reading.Value);
            row.Min = FormatSensor(reading.Type, reading.Min);
            row.Max = FormatSensor(reading.Type, reading.Max);
        }
        if (availabilityChanged)
            ApplySensorFilter();
    }

    void OnSensorSearchChanged(object sender, TextChangedEventArgs e)
    {
        SensorSearchHint.Visibility = SensorSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplySensorFilter();
    }

    void ApplySensorFilter()
    {
        var query = SensorSearch.Text.Trim();
        bool Match(string text) => query.Length == 0 || text.Contains(query, StringComparison.CurrentCultureIgnoreCase);

        foreach (var group in _sensorGroups)
        {
            var groupMatch = Match(group.Hardware);
            foreach (var section in group.Sections)
            {
                var sectionMatch = groupMatch || Match(section.Title);
                foreach (var row in section.Rows)
                    row.IsVisible = row.HasValue && (sectionMatch || Match(row.Name));
                section.IsVisible = section.Rows.Any(r => r.IsVisible);
            }
            group.IsVisible = group.Sections.Any(s => s.IsVisible);
        }
        SensorEmpty.Visibility = _sensorGroups.Count > 0 && !_sensorGroups.Any(g => g.IsVisible)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    static string TranslateSensorName(string name) => !Loc.IsFrench ? name : name
        .Replace("Distance to TjMax", "marge avant TjMax")
        .Replace("Core Average", "Moyenne des cœurs")
        .Replace("Core Max", "Cœur le plus chaud")
        .Replace("CPU Package", "Package CPU")
        .Replace("CPU Total", "Total")
        .Replace("CPU Core", "Cœur")
        .Replace("GPU Core", "Cœur GPU")
        .Replace("GPU Hot Spot", "Point chaud GPU")
        .Replace("GPU Memory Controller", "Contrôleur mémoire GPU")
        .Replace("GPU Memory", "Mémoire GPU")
        .Replace("GPU Video Engine", "Moteur vidéo")
        .Replace("GPU Bus", "Bus PCIe")
        .Replace("GPU Fan", "Ventilateur GPU")
        .Replace("GPU Package", "Carte graphique")
        .Replace("Virtual Memory", "Mémoire virtuelle")
        .Replace("Total Memory", "Mémoire physique")
        .Replace("Memory Used", "Mémoire utilisée")
        .Replace("Memory Available", "Mémoire disponible")
        .Replace("Memory", "Mémoire")
        .Replace(" Used", " utilisée")
        .Replace(" Free", " libre")
        .Replace(" Total", " totale")
        .Replace("Bus Speed", "Fréquence du bus")
        .Replace("Thread", "thread");

    static string HardwareGlyph(string kind) => kind switch
    {
        "Cpu" => "",
        "GpuNvidia" or "GpuAmd" or "GpuIntel" => "",
        "Memory" => "",
        "Storage" => "",
        "Network" => "",
        _ => "",
    };

    static string SectionTitle(string type) => type switch
    {
        "Temperature" or "Load" or "Clock" or "Power" or "Voltage" or "Current" or "Fan" or "Control" or "Throughput" or "Energy"
            => Loc.T("Sec_" + type),
        "Data" or "SmallData" => Loc.T("Sec_Data"),
        _ => type.ToUpperInvariant(),
    };

    static string FormatSensor(string type, float? value)
    {
        if (value is null || float.IsNaN(value.Value))
            return "-";
        var v = value.Value;
        return type switch
        {
            "Temperature" => $"{v:0.0} °C",
            "Load" or "Control" or "Level" => $"{v:0.0} %",
            "Clock" => $"{v:0} MHz",
            "Power" => $"{v:0.0} W",
            "Voltage" => $"{v:0.000} V",
            "Current" => $"{v:0.00} A",
            "Fan" => $"{v:0} {Loc.T("Unit_Rpm")}",
            "Data" => $"{v:0.0} {Loc.T("Unit_GB")}",
            "SmallData" => $"{v:0} {Loc.T("Unit_MB")}",
            "Throughput" => $"{v / 1024:0.0} {Loc.T("Unit_KBps")}",
            "Energy" => $"{v:0} mWh",
            _ => $"{v:0.##}",
        };
    }
}
