using System.Diagnostics;
using Microsoft.Win32;

namespace MonolithHub.Hardware;

/// <summary>Pilote noyau PawnIO, nécessaire pour lire température, fréquence et puissance du CPU.</summary>
public static class PawnIoDriver
{
    public const string Website = "https://pawnio.eu";
    const string WingetId = "namazso.PawnIO";

    public static bool IsInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return key is not null;
    }

    /// <summary>Installe PawnIO via winget (l'application tourne déjà en administrateur).</summary>
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
            // Lire les sorties évite un blocage si le tampon se remplit.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
        catch (Exception)
        {
            // winget absent ou bloqué : on vérifie quand même l'état ci-dessous.
        }
        return IsInstalled();
    }
}
