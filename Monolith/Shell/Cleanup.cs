using System.IO;
using Monolith.Diagnostics;
using Monolith.Lighting;

namespace Monolith.Shell;

/// <summary>Retire de Windows ce que Monolith y a installé (tâche de démarrage, réglages).</summary>
public static class Cleanup
{
    public static void Run(bool removeSettings)
    {
        LightingService.RemoveStartupTasks();
        Log.Info($"Nettoyage effectué (réglages supprimés : {removeSettings})");
        if (!removeSettings)
            return;
        foreach (var name in new[] { "Monolith", "OmenLite" })
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
