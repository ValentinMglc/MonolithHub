using System.IO;
using MonolithHub.Diagnostics;
using MonolithHub.Lighting;

namespace MonolithHub.Shell;

/// <summary>Retire de Windows ce que Monolith Hub y a installé (tâche de démarrage, réglages).</summary>
public static class Cleanup
{
    public static void Run(bool removeSettings)
    {
        LightingService.RemoveStartupTasks();
        Log.Info($"Nettoyage effectué (réglages supprimés : {removeSettings})");
        if (!removeSettings)
            return;
        foreach (var name in new[] { "MonolithHub", "Monolith", "OmenLite" })
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name);
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception)
            {
                // Fichier encore ouvert (journal) : sans gravité, il ne reste qu'un petit dossier.
            }
        }
    }
}
