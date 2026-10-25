using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Monolith;

/// <summary>Informations sur l'application (version, liens) partagées par l'interface et le journal.</summary>
public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/ValentinMglc/monolith-hub";

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
