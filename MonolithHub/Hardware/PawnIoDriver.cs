using System.Diagnostics;
using Microsoft.Win32;

namespace MonolithHub.Hardware;

public static class PawnIoDriver
{
    public const string Website = "https://pawnio.eu";
    const string WingetId = "namazso.PawnIO";

    public static bool IsInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return key is not null;
    }

    public static async Task<bool> InstallAsync()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("winget.exe",
                $"install --id {WingetId} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
        catch (Exception)
        {
        }
        return IsInstalled();
    }
}
