using HidSharp;

namespace MonolithHub.Lighting;

public enum LightingMode : byte
{
    Static = 0x01,
    Off = 0x05,
    Breathing = 0x06,
    ColorCycle = 0x07,
    Blinking = 0x08,
}

public enum LightingSpeed : byte
{
    Slow = 0x01,
    Medium = 0x02,
    Fast = 0x03,
}

/// <summary>Zones exposées par le contrôleur d'un OMEN Obelisk 875 (même carte que l'OMEN 30L).</summary>
public enum LightingZone : byte
{
    Logo = 0x01,
    LightBar = 0x02,
}

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// Pilote du contrôleur USB HID « TracerLED » (VID 0x103C / PID 0x84FD).
/// Le format des trames provient de la rétro-ingénierie du projet OpenRGB (pilote HP Omen 30L).
/// </summary>
public sealed class OmenLightingDevice : IDisposable
{
    public const int VendorId = 0x103C;
    public const int ProductId = 0x84FD;

    const int ReportLength = 58;
    const byte VersionId = 0x12;
    const byte PowerOn = 0x01;
    const byte ThemeCustom = 0x00;
    const byte TypeStatic = 0x02;
    const byte TypeAnimated = 0x0A;
    const byte TypeDirect = 0x04;
    const LightingMode DirectMode = (LightingMode)0x04;

    readonly HidStream _stream;
    readonly object _lock = new();

    OmenLightingDevice(HidStream stream) => _stream = stream;

    /// <summary>Vrai si le contrôleur est branché, même s'il est occupé par une autre application.</summary>
    public static bool IsPresent() => FindDevice() is not null;

    static HidDevice? FindDevice() =>
        DeviceList.Local.GetHidDevices(VendorId, ProductId).FirstOrDefault(d => d.GetMaxOutputReportLength() == ReportLength);

    public static OmenLightingDevice? TryOpen()
    {
        var device = FindDevice();
        if (device is null || !device.TryOpen(out var stream))
            return null;
        stream.WriteTimeout = 1000;
        return new OmenLightingDevice(stream);
    }

    /// <param name="brightness">0 à 100.</param>
    /// <param name="colors">Une couleur pour statique / respiration / clignotement, plusieurs pour le cycle.</param>
    public void SetZone(LightingZone zone, LightingMode mode, IReadOnlyList<Rgb> colors, byte brightness, LightingSpeed speed)
    {
        lock (_lock)
        {
            if (mode == LightingMode.Off)
            {
                _stream.Write(CreateReport(zone, mode));
                return;
            }

            if (colors.Count == 0)
                throw new ArgumentException("Au moins une couleur est requise.", nameof(colors));

            brightness = Math.Min(brightness, (byte)100);

            if (mode == LightingMode.Static)
            {
                var report = CreateReport(zone, mode);
                report[0x04] = 1;
                report[0x05] = 1;
                WriteColor(report, zone, colors[0]);
                report[0x30] = brightness;
                report[0x31] = TypeStatic;
                _stream.Write(report);
                return;
            }

            // Modes animés : une trame par couleur, indexées à partir de 1.
            for (var i = 0; i < colors.Count; i++)
            {
                var report = CreateReport(zone, mode);
                report[0x04] = (byte)colors.Count;
                report[0x05] = (byte)(i + 1);
                WriteColor(report, zone, colors[i]);
                report[0x30] = brightness;
                report[0x31] = TypeAnimated;
                report[0x38] = ThemeCustom;
                report[0x39] = (byte)speed;
                _stream.Write(report);
            }
        }
    }

    /// <summary>
    /// Mode « direct » : couleur fixe pensée pour être rafraîchie souvent (effets pilotés par logiciel),
    /// contrairement au mode statique qui est mémorisé par le contrôleur.
    /// </summary>
    public void SetDirect(LightingZone zone, Rgb color, byte brightness)
    {
        lock (_lock)
        {
            var report = CreateReport(zone, DirectMode);
            report[0x04] = 1;
            report[0x05] = 1;
            var offset = 0x08 + ((byte)zone - 1) * 4;
            report[offset] = 100;
            report[offset + 1] = color.R;
            report[offset + 2] = color.G;
            report[offset + 3] = color.B;
            report[0x30] = Math.Min(brightness, (byte)100);
            report[0x31] = TypeDirect;
            _stream.Write(report);
        }
    }

    static byte[] CreateReport(LightingZone zone, LightingMode mode)
    {
        // L'octet 0 est l'identifiant de rapport HID (0).
        var report = new byte[ReportLength];
        report[0x02] = VersionId;
        report[0x03] = (byte)mode;
        report[0x36] = (byte)zone;
        report[0x37] = PowerOn;
        return report;
    }

    static void WriteColor(byte[] report, LightingZone zone, Rgb color)
    {
        var offset = 0x08 + ((byte)zone - 1) * 3;
        report[offset] = color.R;
        report[offset + 1] = color.G;
        report[offset + 2] = color.B;
    }

    public void Dispose() => _stream.Dispose();
}
