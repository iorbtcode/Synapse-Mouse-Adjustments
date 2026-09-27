using SynapseMouse.App.Infrastructure;
using SynapseMouse.App.Services;

namespace SynapseMouse.App;

/// <summary>
/// Entry point. Handles the helper modes before any UI starts:
///   --watchdog &lt;pid&gt; &lt;token&gt;   crash guard (see <see cref="Watchdog"/>)
///   --restore-windows-settings  re-applies the user's own Windows mouse settings and exits
///   --uninstall                 restores settings and removes the startup entry (for uninstallers)
/// Otherwise enforces a single instance and runs the WPF application.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == Watchdog.Argument)
        {
            return Watchdog.Run(args);
        }

        if (args.Contains("--restore-windows-settings") || args.Contains("--uninstall"))
        {
            WindowsMouseSettings.RestoreBaselineUnconditionally();
            if (args.Contains("--uninstall"))
            {
                try
                {
                    StartupService.SetEnabled(false);
                }
                catch (Exception)
                {
                    return 1;
                }
            }

            return 0;
        }

        Log.Initialize(StoragePaths.LogDirectory);
        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            // Already running: bring the existing window forward instead of starting twice.
            SingleInstance.SignalExisting();
            return 0;
        }

        var app = new App { StartedFromStartup = args.Contains(StartupService.StartupArgument) };
        app.InitializeComponent();
        instance.ListenForShowRequests(() => app.Dispatcher.BeginInvoke(() => app.ShowMainWindow(null)));
        return app.Run();
    }
}
