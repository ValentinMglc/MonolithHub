using System.Management;

namespace MonolithHub.Hardware;

public sealed record InfoRow(string Label, string Value);

public sealed record InfoSection(string Title, string Glyph, IReadOnlyList<InfoRow> Rows);

/// <summary>Informations statiques lues via WMI (relues à chaque changement de langue).</summary>
public static class SystemInfo
{
    public static IReadOnlyList<InfoSection> Read()
    {
        var sections = new List<InfoSection>();
        var gb = Loc.T("Unit_GB");

        var system = Query("Win32_ComputerSystem").FirstOrDefault();
        var bios = Query("Win32_BIOS").FirstOrDefault();
        sections.Add(new(Loc.T("Info_Computer"), "", [
            new(Loc.T("Info_Model"), Get(system, "Model")),
            new(Loc.T("Info_Manufacturer"), Get(system, "Manufacturer")),
            new(Loc.T("Info_Serial"), Get(bios, "SerialNumber")),
            new("BIOS", $"{Get(bios, "SMBIOSBIOSVersion")} ({Get(bios, "Manufacturer")})"),
        ]));

        var os = Query("Win32_OperatingSystem").FirstOrDefault();
        sections.Add(new("Windows", "", [
            new(Loc.T("Info_Edition"), Get(os, "Caption").Replace("Microsoft ", "")),
            new(Loc.T("Info_Version"), Get(os, "Version")),
            new(Loc.T("Info_DeviceName"), Get(system, "Name")),
        ]));

        sections.Add(new(Loc.T("Common_Processor"), "", Query("Win32_Processor").SelectMany(p => new InfoRow[]
        {
            new(Loc.T("Info_Model"), Get(p, "Name").Trim()),
            new(Loc.T("Info_CoresThreads"), $"{Get(p, "NumberOfCores")} / {Get(p, "NumberOfLogicalProcessors")}"),
            new(Loc.T("Info_BaseClock"), $"{Get(p, "MaxClockSpeed")} MHz"),
        }).ToList()));

        sections.Add(new(Loc.T("Common_GraphicsCard"), "", Query("Win32_VideoController").SelectMany(g => new InfoRow[]
        {
            new(Loc.T("Info_Model"), Get(g, "Name")),
            new(Loc.T("Info_Driver"), Get(g, "DriverVersion")),
            new(Loc.T("Info_Display"), Loc.F("Info_DisplayValue",
                Get(g, "CurrentHorizontalResolution"), Get(g, "CurrentVerticalResolution"), Get(g, "CurrentRefreshRate"))),
        }).ToList()));

        sections.Add(new(Loc.T("Common_Memory"), "", Query("Win32_PhysicalMemory")
            .Select(m => new InfoRow(
                Get(m, "DeviceLocator"),
                $"{ToGb(m["Capacity"], 1 << 30)} {gb} · {Get(m, "ConfiguredClockSpeed")} MT/s · {Get(m, "PartNumber").Trim()}"))
            .ToList()));

        sections.Add(new(Loc.T("Dash_Storage"), "", Query("Win32_DiskDrive")
            .Select(d => new InfoRow(Get(d, "Model"), $"{ToGb(d["Size"])} {gb}"))
            .ToList()));

        sections.Add(new(Loc.T("Info_Audio"), "", Query("Win32_SoundDevice")
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
        try { return obj?[property]?.ToString() ?? "-"; }
        catch (ManagementException) { return "-"; }
    }

    // Disques en Go décimaux (comme les fabricants), barrettes mémoire en Gio.
    static string ToGb(object? bytes, double unit = 1e9) =>
        bytes is null ? "?" : Math.Round(Convert.ToDouble(bytes) / unit).ToString("0");
}
