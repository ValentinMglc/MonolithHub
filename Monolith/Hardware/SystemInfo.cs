using System.Management;

namespace Monolith.Hardware;

public sealed record InfoRow(string Label, string Value);

public sealed record InfoSection(string Title, string Glyph, IReadOnlyList<InfoRow> Rows);

/// <summary>Informations statiques lues une seule fois via WMI.</summary>
public static class SystemInfo
{
    public static IReadOnlyList<InfoSection> Read()
    {
        var sections = new List<InfoSection>();

        var system = Query("Win32_ComputerSystem").FirstOrDefault();
        var bios = Query("Win32_BIOS").FirstOrDefault();
        sections.Add(new("Ordinateur", "", [
            new("Modèle", Get(system, "Model")),
            new("Fabricant", Get(system, "Manufacturer")),
            new("Numéro de série", Get(bios, "SerialNumber")),
            new("BIOS", $"{Get(bios, "SMBIOSBIOSVersion")} ({Get(bios, "Manufacturer")})"),
        ]));

        var os = Query("Win32_OperatingSystem").FirstOrDefault();
        sections.Add(new("Windows", "", [
            new("Édition", Get(os, "Caption").Replace("Microsoft ", "")),
            new("Version", Get(os, "Version")),
            new("Nom de l'appareil", Get(system, "Name")),
        ]));

        sections.Add(new("Processeur", "", Query("Win32_Processor").SelectMany(p => new InfoRow[]
        {
            new("Modèle", Get(p, "Name").Trim()),
            new("Cœurs / threads", $"{Get(p, "NumberOfCores")} / {Get(p, "NumberOfLogicalProcessors")}"),
            new("Fréquence de base", $"{Get(p, "MaxClockSpeed")} MHz"),
        }).ToList()));

        sections.Add(new("Carte graphique", "", Query("Win32_VideoController").SelectMany(g => new InfoRow[]
        {
            new("Modèle", Get(g, "Name")),
            new("Pilote", Get(g, "DriverVersion")),
            new("Affichage", $"{Get(g, "CurrentHorizontalResolution")} × {Get(g, "CurrentVerticalResolution")} à {Get(g, "CurrentRefreshRate")} Hz"),
        }).ToList()));

        sections.Add(new("Mémoire", "", Query("Win32_PhysicalMemory")
            .Select(m => new InfoRow(
                Get(m, "DeviceLocator"),
                $"{ToGb(m["Capacity"], 1 << 30)} Go · {Get(m, "ConfiguredClockSpeed")} MT/s · {Get(m, "PartNumber").Trim()}"))
            .ToList()));

        sections.Add(new("Stockage", "", Query("Win32_DiskDrive")
            .Select(d => new InfoRow(Get(d, "Model"), $"{ToGb(d["Size"])} Go"))
            .ToList()));

        sections.Add(new("Audio", "", Query("Win32_SoundDevice")
            .Select(a => new InfoRow("", Get(a, "Name")))
            .ToList()));

        return sections;
    }

    static List<ManagementBaseObject> Query(string wmiClass)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {wmiClass}");
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch (ManagementException)
        {
            return [];
        }
    }

    static string Get(ManagementBaseObject? obj, string property)
    {
        try { return obj?[property]?.ToString() ?? "—"; }
        catch (ManagementException) { return "—"; }
    }

    // Disques en Go décimaux (comme les fabricants), barrettes mémoire en Gio.
    static string ToGb(object? bytes, double unit = 1e9) =>
        bytes is null ? "?" : Math.Round(Convert.ToDouble(bytes) / unit).ToString("0");
}
