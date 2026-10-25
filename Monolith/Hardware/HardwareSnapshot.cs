namespace Monolith.Hardware;

public sealed record DriveStat(string Name, string Label, double TotalGb, double FreeGb)
{
    public double UsedPercent => TotalGb <= 0 ? 0 : (TotalGb - FreeGb) / TotalGb * 100;
    public string Summary => $"{FreeGb:0} Go libres sur {TotalGb:0} Go";
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? "Disque local" : Label;
}

public sealed record ProcessStat(string Name, double CpuPercent, double RamMb);

public sealed record SensorReading(
    string Id, string Hardware, string HardwareKind, string Type, string Name, float? Value, float? Min, float? Max);

public sealed class HardwareSnapshot
{
    public string CpuName { get; init; } = "";
    public float? CpuLoad { get; init; }
    public float? CpuTemp { get; init; }
    public float? CpuClock { get; init; }
    public float? CpuPower { get; init; }

    public string GpuName { get; init; } = "";
    public float? GpuLoad { get; init; }
    public float? GpuTemp { get; init; }
    public float? GpuClock { get; init; }
    public float? GpuMemoryClock { get; init; }
    public float? GpuMemoryUsedMb { get; init; }
    public float? GpuMemoryTotalMb { get; init; }
    public float? GpuPower { get; init; }
    public float? GpuFan { get; init; }

    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public double RamLoad => RamTotalGb <= 0 ? 0 : RamUsedGb / RamTotalGb * 100;

    public double DownloadMbps { get; init; }
    public double UploadMbps { get; init; }

    public IReadOnlyList<DriveStat> Drives { get; init; } = [];
    /// <summary>Null quand la liste n'a pas été recalculée à ce tick.</summary>
    public IReadOnlyList<ProcessStat>? TopProcesses { get; init; }
    public IReadOnlyList<SensorReading> Sensors { get; init; } = [];

    /// <summary>Faux si le pilote bas niveau (PawnIO) est absent : pas de température CPU.</summary>
    public bool LowLevelAccess { get; init; }
}
