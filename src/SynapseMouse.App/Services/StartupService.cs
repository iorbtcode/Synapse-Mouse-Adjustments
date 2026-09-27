using Microsoft.Win32;
using SynapseMouse.App.Infrastructure;

namespace SynapseMouse.App.Services;

/// <summary>
/// "Start with Windows" through the per-user Run key (no administrator rights needed).
/// The app is launched with --startup, which starts it minimized to the tray.
/// </summary>
internal static class StartupService
{
    public const string StartupArgument = "--startup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = AppInfo.ShortName;

    private static string Command => $"\"{AppInfo.ExecutablePath}\" {StartupArgument}";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when the user disabled the entry in Task Manager's Startup tab.</summary>
    public static bool IsDisabledInTaskManager()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            // First byte: even = enabled, odd = disabled by the user.
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } data && (data[0] & 1) == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, Command, RegistryValueKind.String);
            ClearTaskManagerDisable();
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Keeps the Run entry pointing at the current executable if the app was moved.</summary>
    public static void RepairPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string existing && !string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, Command, RegistryValueKind.String);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not update the startup entry.", ex);
        }
    }

    private static void ClearTaskManagerDisable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            if (key?.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not clear the Task Manager startup flag.", ex);
        }
    }
}
