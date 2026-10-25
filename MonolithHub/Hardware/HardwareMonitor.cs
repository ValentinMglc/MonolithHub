using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace MonolithHub.Hardware;

/// <summary>
/// Collecte périodique des mesures. N'est appelé que depuis un seul thread (LibreHardwareMonitor n'est pas thread-safe).
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
    };

    readonly Stopwatch _clock = Stopwatch.StartNew();
    TimeSpan _lastNetworkSample;
    long _lastBytesReceived = -1, _lastBytesSent = -1;

    TimeSpan _lastProcessSample;
    readonly Dictionary<int, TimeSpan> _lastProcessCpu = [];

    TimeSpan? _lastDriveSample;
    IReadOnlyList<DriveStat> _drives = [];

    int _tick;

    public void Open() => _computer.Open();

    /// <param name="detailed">Faux quand la fenêtre est cachée : pas de processus, seulement l'essentiel.</param>
    public HardwareSnapshot Sample(bool detailed = true)
    {
        var sensors = new List<SensorReading>();
        foreach (var hardware in _computer.Hardware)
            Collect(hardware, sensors);

        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        var gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
                  ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);

        var cpuTemp = Find(cpu, SensorType.Temperature, "CPU Package") ?? Find(cpu, SensorType.Temperature, "Core Max");
        var cpuClocks = cpu?.Sensors
            .Where(s => s.SensorType == SensorType.Clock && s.Name.StartsWith("CPU Core") && s.Value > 0)
            .Select(s => s.Value!.Value)
            .ToList();

        var (ramUsed, ramTotal) = ReadMemory();
        var (down, up) = SampleNetwork();
        var now = _clock.Elapsed;

        if (_lastDriveSample is null || now - _lastDriveSample > TimeSpan.FromSeconds(10))
        {
            _drives = ReadDrives();
            _lastDriveSample = now;
        }

        // La liste des processus est plus coûteuse : toutes les 2 secondes seulement.
        var processes = detailed && _tick++ % 2 == 0 ? SampleProcesses() : null;

        return new HardwareSnapshot
        {
            CpuName = cpu?.Name ?? "Processeur",
            CpuLoad = Find(cpu, SensorType.Load, "CPU Total"),
            CpuTemp = cpuTemp,
            CpuClock = cpuClocks is { Count: > 0 } ? cpuClocks.Average() : null,
            CpuPower = Find(cpu, SensorType.Power, "CPU Package"),

            GpuName = gpu?.Name ?? "Carte graphique",
            GpuLoad = Find(gpu, SensorType.Load, "GPU Core"),
            GpuTemp = Find(gpu, SensorType.Temperature, "GPU Core"),
            GpuClock = Find(gpu, SensorType.Clock, "GPU Core"),
            GpuMemoryClock = Find(gpu, SensorType.Clock, "GPU Memory"),
            GpuMemoryUsedMb = Find(gpu, SensorType.SmallData, "GPU Memory Used"),
            GpuMemoryTotalMb = Find(gpu, SensorType.SmallData, "GPU Memory Total"),
            GpuPower = Find(gpu, SensorType.Power, "GPU Package") ?? Find(gpu, SensorType.Power, "GPU Power"),
            GpuFan = gpu?.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan)?.Value,

            RamUsedGb = ramUsed,
            RamTotalGb = ramTotal,
            DownloadMbps = down,
            UploadMbps = up,
            Drives = _drives,
            TopProcesses = processes,
            Sensors = sensors,
            LowLevelAccess = cpuTemp is > 0,
        };
    }

    static void Collect(IHardware hardware, List<SensorReading> into)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
            into.Add(new SensorReading(sensor.Identifier.ToString(), hardware.Name, hardware.HardwareType.ToString(),
                sensor.SensorType.ToString(), sensor.Name, sensor.Value, sensor.Min, sensor.Max));
        foreach (var sub in hardware.SubHardware)
            Collect(sub, into);
    }

    static float? Find(IHardware? hardware, SensorType type, string name) =>
        hardware?.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name)?.Value;

    (double Down, double Up) SampleNetwork()
    {
        long received = 0, sent = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            var stats = nic.GetIPStatistics();
            received += stats.BytesReceived;
            sent += stats.BytesSent;
        }

        var now = _clock.Elapsed;
        var seconds = (now - _lastNetworkSample).TotalSeconds;
        double down = 0, up = 0;
        if (_lastBytesReceived >= 0 && seconds > 0)
        {
            down = Math.Max(0, received - _lastBytesReceived) * 8 / 1e6 / seconds;
            up = Math.Max(0, sent - _lastBytesSent) * 8 / 1e6 / seconds;
        }
        _lastBytesReceived = received;
        _lastBytesSent = sent;
        _lastNetworkSample = now;
        return (down, up);
    }

    List<ProcessStat> SampleProcesses()
    {
        var now = _clock.Elapsed;
        var elapsed = (now - _lastProcessSample).TotalMilliseconds * Environment.ProcessorCount;
        _lastProcessSample = now;

        var seen = new HashSet<int>();
        var byName = new Dictionary<string, (double Cpu, double Ram)>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id is 0 or 4)
                    continue;
                try
                {
                    var total = process.TotalProcessorTime;
                    seen.Add(process.Id);
                    var cpu = _lastProcessCpu.TryGetValue(process.Id, out var previous) && elapsed > 0
                        ? (total - previous).TotalMilliseconds / elapsed * 100
                        : 0;
                    _lastProcessCpu[process.Id] = total;

                    var entry = byName.GetValueOrDefault(process.ProcessName);
                    byName[process.ProcessName] = (entry.Cpu + cpu, entry.Ram + process.WorkingSet64 / 1048576.0);
                }
                catch (Exception)
                {
                    // Processus protégé ou terminé entre-temps.
                }
            }
        }

        foreach (var pid in _lastProcessCpu.Keys.Where(pid => !seen.Contains(pid)).ToList())
            _lastProcessCpu.Remove(pid);

        return byName
            .Where(p => p.Key is not "Idle" and not "MonolithHub")
            .OrderByDescending(p => p.Value.Cpu)
            .ThenByDescending(p => p.Value.Ram)
            .Take(8)
            .Select(p => new ProcessStat(p.Key, Math.Min(100, p.Value.Cpu), p.Value.Ram))
            .ToList();
    }

    static List<DriveStat> ReadDrives() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new DriveStat(d.Name.TrimEnd('\\'), d.VolumeLabel, d.TotalSize / 1e9, d.AvailableFreeSpace / 1e9))
            .ToList();

    static (double UsedGb, double TotalGb) ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
            return (0, 0);
        const double gib = 1024.0 * 1024 * 1024;
        return ((status.TotalPhys - status.AvailPhys) / gib, status.TotalPhys / gib);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public void Dispose() => _computer.Close();
}
