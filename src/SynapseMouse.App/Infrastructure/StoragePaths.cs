using SynapseMouse.Core.Persistence;

namespace SynapseMouse.App.Infrastructure;

/// <summary>
/// Resolves where settings are stored.
///  * Portable mode: a file named "portable.txt" next to the executable → "&lt;exe folder&gt;\Data".
///  * Custom location chosen in Settings → recorded in %APPDATA%\SynapseMouseAdjustments\storage-location.txt.
///  * Default: %APPDATA%\SynapseMouseAdjustments.
/// </summary>
internal static class StoragePaths
{
    /// <summary>Forces a data directory (used by --self-test so it never touches real settings).</summary>
    public static string? OverrideDirectory { get; set; }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.ShortName);

    private static string PointerFile => Path.Combine(DefaultDirectory, "storage-location.txt");

    public static bool IsPortable => File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    public static string LogDirectory => Path.Combine(OverrideDirectory ?? DefaultDirectory, "logs");

    public static string ResolveDataDirectory()
    {
        if (OverrideDirectory is not null)
        {
            Directory.CreateDirectory(OverrideDirectory);
            return OverrideDirectory;
        }

        if (IsPortable)
        {
            return Path.Combine(AppContext.BaseDirectory, "Data");
        }

        try
        {
            if (File.Exists(PointerFile))
            {
                string custom = File.ReadAllText(PointerFile).Trim();
                if (custom.Length > 0 && Path.IsPathFullyQualified(custom))
                {
                    Directory.CreateDirectory(custom);
                    return custom;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Warn("Custom storage location unavailable; using the default.", ex);
        }

        return DefaultDirectory;
    }

    /// <summary>Moves settings to a new folder and remembers it. Returns the new directory.</summary>
    public static string ChangeDataDirectory(string currentDirectory, string newDirectory)
    {
        if (IsPortable)
        {
            throw new InvalidOperationException("Portable mode is active (portable.txt next to the app); the storage folder is fixed.");
        }

        newDirectory = Path.GetFullPath(newDirectory);
        Directory.CreateDirectory(newDirectory);
        foreach (string name in new[] { SettingsStore.FileName, SettingsStore.FileName + ".bak" })
        {
            string source = Path.Combine(currentDirectory, name);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(newDirectory, name), overwrite: true);
            }
        }

        Directory.CreateDirectory(DefaultDirectory);
        if (string.Equals(newDirectory.TrimEnd('\\'), DefaultDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(PointerFile);
        }
        else
        {
            File.WriteAllText(PointerFile, newDirectory);
        }

        return newDirectory;
    }
}
