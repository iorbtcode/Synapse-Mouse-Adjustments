using System.Globalization;
using System.Text;

namespace SynapseMouse.App.Infrastructure;

/// <summary>Tiny thread-safe file logger (logs\synapse.log, rotated at 1 MB). Never throws.</summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static string? FilePath => _path;

    public static void Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "synapse.log");
            if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
            {
                File.Move(_path, _path + ".old", overwrite: true);
            }
        }
        catch (Exception)
        {
            _path = null;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        string? path = _path;
        if (path is null)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
          .Append(" [").Append(level).Append("] ").Append(message);
        if (ex is not null)
        {
            sb.AppendLine().Append(ex);
        }

        sb.AppendLine();
        lock (Gate)
        {
            try
            {
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Logging must never break the app.
            }
        }
    }
}
