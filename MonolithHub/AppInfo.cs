using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace MonolithHub;

public static class AppInfo
{
    public const string DisplayName = "Monolith Hub";

    public const string RepositoryUrl = "https://github.com/ValentinMglc/MonolithHub";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "?";

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
