using System.IO;

namespace MonolithHub.Diagnostics;

public static class Log
{
    const long MaxSize = 1024 * 1024;

    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonolithHub", "logs");

    static readonly string FilePath = Path.Combine(Folder, "monolith-hub.log");
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
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxSize)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}", System.Text.Encoding.UTF8);
            }
        }
        catch (Exception)
        {
        }
    }
}
