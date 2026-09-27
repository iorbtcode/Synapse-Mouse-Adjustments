using System.Text;
using SynapseMouse.Core.Config;
using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Persistence;

/// <summary>Result of loading settings, including what (if anything) had to be recovered.</summary>
public sealed record LoadResult(SettingsDocument Document, bool IsFirstRun, string? Warning);

/// <summary>
/// Crash-safe JSON persistence for <see cref="SettingsDocument"/>.
/// Writes go to a temp file that is flushed to disk and then atomically swapped in, keeping the previous
/// version as settings.json.bak. Loading falls back to the backup if the main file is damaged, and a
/// damaged file is preserved (settings.json.corrupt) rather than silently overwritten.
/// </summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    private readonly object _writeLock = new();

    public SettingsStore(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    public string FilePath => Path.Combine(Directory, FileName);

    public string BackupPath => FilePath + ".bak";

    public LoadResult Load()
    {
        if (!File.Exists(FilePath) && !File.Exists(BackupPath))
        {
            return new LoadResult(ConfigPresets.CreateDefaultDocument(), true, null);
        }

        string? mainError = null;
        if (File.Exists(FilePath))
        {
            try
            {
                return new LoadResult(ConfigSerializer.DeserializeDocument(ReadText(FilePath)), false, null);
            }
            catch (Exception ex) when (ex is ConfigFormatException or IOException or UnauthorizedAccessException)
            {
                mainError = ex.Message;
                TryPreserveCorrupt(FilePath);
            }
        }

        if (File.Exists(BackupPath))
        {
            try
            {
                var doc = ConfigSerializer.DeserializeDocument(ReadText(BackupPath));
                return new LoadResult(doc, false, "Your settings file could not be read, so the last backup was restored." +
                    (mainError is null ? string.Empty : " (" + mainError + ")"));
            }
            catch (Exception ex) when (ex is ConfigFormatException or IOException or UnauthorizedAccessException)
            {
                // Fall through to defaults.
            }
        }

        return new LoadResult(ConfigPresets.CreateDefaultDocument(), false,
            "Your settings could not be read and were reset to defaults." + (mainError is null ? string.Empty : " (" + mainError + ")"));
    }

    /// <summary>Serializes and atomically writes the document. Thread-safe.</summary>
    public void Save(SettingsDocument document) => SaveText(ConfigSerializer.Serialize(document));

    /// <summary>Atomically writes already-serialized settings. Thread-safe.</summary>
    public void SaveText(string json)
    {
        lock (_writeLock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            string temp = FilePath + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(FilePath))
            {
                try
                {
                    File.Replace(temp, FilePath, BackupPath, ignoreMetadataErrors: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    // File.Replace is not available on every file system; fall back to copy + move.
                    File.Copy(FilePath, BackupPath, overwrite: true);
                }
            }

            File.Move(temp, FilePath, overwrite: true);
        }
    }

    private static string ReadText(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > ConfigSerializer.MaxFileBytes)
        {
            throw new ConfigFormatException("The settings file is too large.");
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static void TryPreserveCorrupt(string path)
    {
        try
        {
            File.Copy(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort only.
        }
    }
}
