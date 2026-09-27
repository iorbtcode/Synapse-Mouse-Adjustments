using SynapseMouse.Core.Models;

namespace SynapseMouse.Core.Config;

/// <summary>Pure decision logic for application-specific config switching.</summary>
public static class AutoSwitchLogic
{
    /// <summary>Finds the first enabled association matching a foreground application.</summary>
    public static AppAssociation? Match(IEnumerable<AppAssociation> associations, string? processName, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        string exe = NormalizeProcessName(processName);
        foreach (var association in associations)
        {
            if (!association.Enabled || NormalizeProcessName(association.ProcessName) != exe)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(association.WindowTitleContains)
                && (windowTitle is null || windowTitle.IndexOf(association.WindowTitleContains, StringComparison.OrdinalIgnoreCase) < 0))
            {
                continue;
            }

            return association;
        }

        return null;
    }

    /// <summary>"C:\Games\javaw.EXE" and "javaw" both become "javaw.exe".</summary>
    public static string NormalizeProcessName(string name)
    {
        string trimmed = name.Trim().Trim('"');
        int slash = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        if (slash >= 0)
        {
            trimmed = trimmed[(slash + 1)..];
        }

        trimmed = trimmed.ToLowerInvariant();
        return trimmed.EndsWith(".exe", StringComparison.Ordinal) ? trimmed : trimmed + ".exe";
    }

    /// <summary>
    /// Decides which config should be active for a foreground change.
    /// Returns null when the active config should stay as it is.
    /// </summary>
    public static Guid? Decide(
        AppSettings app,
        Guid activeConfigId,
        Guid defaultConfigId,
        bool currentlyAutoSelected,
        AppAssociation? match,
        Func<Guid, bool> configExists)
    {
        if (!app.AutoSwitchEnabled)
        {
            return null;
        }

        if (match is not null)
        {
            return configExists(match.ConfigId) && match.ConfigId != activeConfigId ? match.ConfigId : null;
        }

        if (!currentlyAutoSelected)
        {
            return null;
        }

        Guid? target = app.AutoSwitchFallback switch
        {
            AutoSwitchFallback.ReturnToPrevious => app.LastManualConfigId,
            AutoSwitchFallback.ReturnToDefault => defaultConfigId,
            _ => null,
        };

        return target is { } id && configExists(id) && id != activeConfigId ? id : null;
    }
}
