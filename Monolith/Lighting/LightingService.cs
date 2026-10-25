using System.Diagnostics;
using System.Globalization;

namespace Monolith.Lighting;

public static class LightingService
{
    /// <summary>Couleurs utilisées par le mode « Cycle de couleurs ».</summary>
    public static readonly IReadOnlyList<Rgb> CycleColors =
    [
        new(0xFF, 0x00, 0x00),
        new(0x00, 0xFF, 0x00),
        new(0x00, 0x00, 0xFF),
        new(0xB0, 0x00, 0xFF),
    ];

    const string TaskName = "Monolith";
    /// <summary>Nom de la tâche créée par les versions « OMEN Lite ».</summary>
    const string LegacyTaskName = "OmenLite - Eclairage";

    static readonly ZoneSettings OffSettings = new() { Mode = LightingMode.Off };

    /// <summary>Réglage enregistré d'une zone (tient compte de l'interrupteur et de la synchro).</summary>
    public static ZoneSettings Effective(LightingSettings settings, LightingZone zone) =>
        !settings.Enabled ? OffSettings
        : zone == LightingZone.Logo || settings.SyncZones ? settings.Logo
        : settings.LightBar;

    /// <summary>Vrai si la zone est actuellement pilotée par la température.</summary>
    public static bool IsReactive(LightingSettings settings, LightingZone zone) =>
        settings.Enabled && settings.Reactive.Enabled && settings.Reactive.Covers(zone);

    public static void Apply(OmenLightingDevice device, LightingZone zone, ZoneSettings settings)
    {
        var colors = settings.Mode == LightingMode.ColorCycle ? CycleColors : [ParseColor(settings.Color)];
        device.SetZone(zone, settings.Mode, colors, settings.Brightness, settings.Speed);
    }

    /// <param name="reactiveColor">Couleur calculée depuis la température ; null = ignorer l'option.</param>
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

    /// <summary>Température utilisée par l'éclairage réactif, selon la source choisie.</summary>
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

    /// <summary>Dégradé linéaire entre la couleur « froid » et la couleur « chaud ».</summary>
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

    // L'application demande les droits administrateur : une entrée « Run » du registre serait bloquée par l'UAC,
    // d'où une tâche planifiée à l'ouverture de session avec privilèges élevés.
    public static bool IsStartupTaskInstalled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static void RemoveStartupTasks()
    {
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        RunSchtasks($"/Delete /TN \"{LegacyTaskName}\" /F");
    }

    /// <summary>Remplace l'ancienne tâche « OMEN Lite » par celle de Monolith ; renvoie vrai si une migration a eu lieu.</summary>
    public static bool MigrateLegacyStartupTask()
    {
        if (RunSchtasks($"/Query /TN \"{LegacyTaskName}\"") != 0)
            return false;
        RunSchtasks($"/Delete /TN \"{LegacyTaskName}\" /F");
        return SetStartupTask(true);
    }

    /// <summary>Au démarrage de Windows, Monolith se lance réduit dans la zone de notification.</summary>
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
