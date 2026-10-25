using System.Diagnostics;
using System.Globalization;

namespace MonolithHub.Lighting;

public static class LightingService
{
    public static readonly IReadOnlyList<Rgb> CycleColors =
    [
        new(0xFF, 0x00, 0x00),
        new(0x00, 0xFF, 0x00),
        new(0x00, 0x00, 0xFF),
        new(0xB0, 0x00, 0xFF),
    ];

    const string TaskName = "MonolithHub";
    static readonly string[] LegacyTaskNames = ["Monolith", "OmenLite - Eclairage"];

    static readonly ZoneSettings OffSettings = new() { Mode = LightingMode.Off };

    public static ZoneSettings Effective(LightingSettings settings, LightingZone zone) =>
        !settings.Enabled ? OffSettings
        : zone == LightingZone.Logo || settings.SyncZones ? settings.Logo
        : settings.LightBar;

    public static bool IsReactive(LightingSettings settings, LightingZone zone) =>
        settings.Enabled && settings.Reactive.Enabled && settings.Reactive.Covers(zone);

    public static void Apply(OmenLightingDevice device, LightingZone zone, ZoneSettings settings)
    {
        var colors = settings.Mode == LightingMode.ColorCycle ? CycleColors : [ParseColor(settings.Color)];
        device.SetZone(zone, settings.Mode, colors, settings.Brightness, settings.Speed);
    }

    public static void ApplyAll(OmenLightingDevice device, LightingSettings settings, Rgb? reactiveColor = null)
    {
        foreach (var zone in new[] { LightingZone.Logo, LightingZone.LightBar })
        {
            var zoneSettings = Effective(settings, zone);
            if (reactiveColor is { } color && IsReactive(settings, zone))
                device.SetDirect(zone, color, Math.Max(zoneSettings.Brightness, (byte)10));
            else
                Apply(device, zone, zoneSettings);
        }
    }

    public static bool ApplySaved(LightingSettings settings)
    {
        using var device = OmenLightingDevice.TryOpen();
        if (device is null)
            return false;
        ApplyAll(device, settings);
        return true;
    }

    public static float? SourceTemperature(ReactiveSettings reactive, float? cpu, float? gpu)
    {
        cpu = cpu is > 0 ? cpu : null;
        gpu = gpu is > 0 ? gpu : null;
        return reactive.Source switch
        {
            TemperatureSource.Cpu => cpu ?? gpu,
            TemperatureSource.Gpu => gpu ?? cpu,
            _ => cpu is null ? gpu : gpu is null ? cpu : Math.Max(cpu.Value, gpu.Value),
        };
    }

    public static Rgb ReactiveColor(ReactiveSettings reactive, float temperature)
    {
        var span = Math.Max(1, reactive.HotTemperature - reactive.ColdTemperature);
        var f = Math.Clamp((temperature - reactive.ColdTemperature) / span, 0, 1);
        var cold = ParseColor(reactive.ColdColor);
        var hot = ParseColor(reactive.HotColor);
        return new(
            (byte)(cold.R + (hot.R - cold.R) * f),
            (byte)(cold.G + (hot.G - cold.G) * f),
            (byte)(cold.B + (hot.B - cold.B) * f));
    }

    public static Rgb ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return new(0xFF, 0, 0);
        return new((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public static string ToHex(Rgb c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool IsStartupTaskInstalled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static void RemoveStartupTasks()
    {
        foreach (var name in LegacyTaskNames.Prepend(TaskName))
            RunSchtasks($"/Delete /TN \"{name}\" /F");
    }

    public static bool MigrateLegacyStartupTask()
    {
        var found = false;
        foreach (var name in LegacyTaskNames.Where(name => RunSchtasks($"/Query /TN \"{name}\"") == 0))
        {
            RunSchtasks($"/Delete /TN \"{name}\" /F");
            found = true;
        }
        return found && SetStartupTask(true);
    }

    public static bool SetStartupTask(bool enabled)
    {
        if (!enabled)
            return RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;

        var exe = Environment.ProcessPath!;
        return RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" --tray\" /SC ONLOGON /RL HIGHEST /F") == 0;
    }

    static int RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            process.WaitForExit(5000);
            return process.ExitCode;
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
