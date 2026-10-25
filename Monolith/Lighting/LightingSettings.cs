using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monolith.Lighting;

public sealed class ZoneSettings
{
    public LightingMode Mode { get; set; } = LightingMode.Static;
    public string Color { get; set; } = "#FF0000";
    public byte Brightness { get; set; } = 100;
    public LightingSpeed Speed { get; set; } = LightingSpeed.Medium;

    public void CopyFrom(ZoneSettings other)
    {
        Mode = other.Mode;
        Color = other.Color;
        Brightness = other.Brightness;
        Speed = other.Speed;
    }

    public ZoneSettings Clone()
    {
        var clone = new ZoneSettings();
        clone.CopyFrom(this);
        return clone;
    }
}

public enum TemperatureSource { Hottest, Cpu, Gpu }

public enum ReactiveZones { Logo, LightBar, Both }

/// <summary>Couleur pilotée par la température (dégradé froid → chaud).</summary>
public sealed class ReactiveSettings
{
    public bool Enabled { get; set; }
    public TemperatureSource Source { get; set; } = TemperatureSource.Hottest;
    public ReactiveZones Zones { get; set; } = ReactiveZones.Logo;
    public string ColdColor { get; set; } = "#0080FF";
    public string HotColor { get; set; } = "#FF0000";
    public int ColdTemperature { get; set; } = 40;
    public int HotTemperature { get; set; } = 85;

    public bool Covers(LightingZone zone) =>
        Zones == ReactiveZones.Both || (zone == LightingZone.Logo ? Zones == ReactiveZones.Logo : Zones == ReactiveZones.LightBar);
}

/// <summary>Réglage d'éclairage enregistré sous un nom.</summary>
public sealed class LightingProfile
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool SyncZones { get; set; } = true;
    public ZoneSettings Logo { get; set; } = new();
    public ZoneSettings LightBar { get; set; } = new();

    public static LightingProfile Capture(string name, LightingSettings settings) => new()
    {
        Name = name,
        Enabled = settings.Enabled,
        SyncZones = settings.SyncZones,
        Logo = settings.Logo.Clone(),
        LightBar = settings.LightBar.Clone(),
    };

    public void ApplyTo(LightingSettings settings)
    {
        settings.Enabled = Enabled;
        settings.SyncZones = SyncZones;
        settings.Logo.CopyFrom(Logo);
        settings.LightBar.CopyFrom(LightBar);
        settings.ActiveProfile = Name;
    }
}

public sealed class LightingSettings
{
    /// <summary>Interrupteur général : faux = tout éteint, sans perdre les réglages des zones.</summary>
    public bool Enabled { get; set; } = true;
    public ZoneSettings Logo { get; set; } = new();
    public ZoneSettings LightBar { get; set; } = new();
    public bool SyncZones { get; set; } = true;
    public ReactiveSettings Reactive { get; set; } = new();
    public List<LightingProfile> Profiles { get; set; } = DefaultProfiles();
    /// <summary>Profil appliqué en dernier, tant qu'aucun réglage n'a été modifié à la main.</summary>
    public string? ActiveProfile { get; set; }

    static List<LightingProfile> DefaultProfiles() =>
    [
        new() { Name = "Jeu", Logo = new() { Color = "#FF0000" }, LightBar = new() { Color = "#FF0000" } },
        new()
        {
            Name = "Nuit",
            Logo = new() { Mode = LightingMode.Breathing, Color = "#7300FF", Brightness = 25, Speed = LightingSpeed.Slow },
            LightBar = new() { Mode = LightingMode.Breathing, Color = "#7300FF", Brightness = 25, Speed = LightingSpeed.Slow },
        },
        new()
        {
            Name = "Arc-en-ciel",
            Logo = new() { Mode = LightingMode.ColorCycle },
            LightBar = new() { Mode = LightingMode.ColorCycle },
        },
        new() { Name = "Éteint", Enabled = false },
    ];

    public static LightingSettings Load() => JsonStore.Load<LightingSettings>("lighting.json");
    public void Save() => JsonStore.Save("lighting.json", this);
}

/// <summary>Réglages généraux de l'application.</summary>
public sealed class AppSettings
{
    public bool CloseToTray { get; set; } = true;
    public bool TrayHintShown { get; set; }
    public bool HeatAlertsEnabled { get; set; } = true;
    public int CpuAlertThreshold { get; set; } = 85;
    public int GpuAlertThreshold { get; set; } = 85;

    public static AppSettings Load() => JsonStore.Load<AppSettings>("settings.json");
    public void Save() => JsonStore.Save("settings.json", this);
}

static class JsonStore
{
    static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Monolith");

    /// <summary>Dossier des réglages des versions « OMEN Lite », repris une seule fois.</summary>
    static readonly string LegacyFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmenLite");

    static JsonStore()
    {
        try
        {
            if (Directory.Exists(Folder) || !Directory.Exists(LegacyFolder))
                return;
            Directory.CreateDirectory(Folder);
            foreach (var file in Directory.GetFiles(LegacyFolder, "*.json"))
                File.Copy(file, Path.Combine(Folder, Path.GetFileName(file)));
        }
        catch (Exception)
        {
            // Migration impossible : on démarre simplement avec les réglages par défaut.
        }
    }

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string file) where T : new()
    {
        try
        {
            var path = Path.Combine(Folder, file);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new();
        }
        catch (Exception)
        {
            // Fichier corrompu ou ancien format : on repart des valeurs par défaut.
        }
        return new();
    }

    public static void Save<T>(string file, T value)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, file), JsonSerializer.Serialize(value, Options));
    }
}
