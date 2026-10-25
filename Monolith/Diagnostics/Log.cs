using System.IO;

namespace Monolith.Diagnostics;

/// <summary>
/// Journal texte dans %APPDATA%\Monolith\logs. Permet aux utilisateurs de joindre un fichier à un signalement de bug.
/// </summary>
public static class Log
{
    const long MaxSize = 1024 * 1024;

    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Monolith", "logs");

    static readonly string FilePath = Path.Combine(Folder, "monolith.log");
    static readonly object Lock = new();

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message, Exception? ex = null) => Write("WARN ", ex is null ? message : $"{message}\n{ex}");

    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Folder);
                // Rotation simple : un seul fichier de sauvegarde, le journal ne dépasse jamais ~2 Mo.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxSize)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                // UTF-8 avec BOM : les accents s'affichent correctement dans tous les éditeurs.
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}", System.Text.Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Le journal ne doit jamais faire planter l'application.
        }
    }
}
