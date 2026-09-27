using System.Reflection;

namespace SynapseMouse.App.Infrastructure;

internal static class AppInfo
{
    public const string Name = "Synapse Mouse Adjustments";
    public const string ShortName = "SynapseMouseAdjustments";

    /// <summary>Emergency shortcut that always turns Master Enable off (Ctrl + Alt + Shift + F12).</summary>
    public const string EmergencyHotkeyText = "Ctrl + Alt + Shift + F12";

    public static string Version
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, ShortName + ".exe");
}
