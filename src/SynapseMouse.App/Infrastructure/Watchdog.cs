using System.Diagnostics;
using SynapseMouse.App.Services;
using static SynapseMouse.App.Native.NativeMethods;

namespace SynapseMouse.App.Infrastructure;

/// <summary>
/// Optional crash guard. A tiny second process (the same executable started with --watchdog) waits on
/// the main process. If the main process ends without a clean exit (crash, killed in Task Manager), it
/// restores the user's Windows mouse settings and releases any mouse buttons left logically pressed.
/// Input hooks need no cleanup: Windows removes them automatically when a process ends.
/// </summary>
internal static class Watchdog
{
    public const string Argument = "--watchdog";

    private static string CleanExitEventName(int pid, string token) => $@"Local\SynapseMouseAdjustments.CleanExit.{pid}.{token}";

    private static EventWaitHandle? _cleanExitEvent;
    private static Process? _process;

    /// <summary>Set by --self-test: no helper process is started.</summary>
    public static bool Suppressed { get; set; }

    /// <summary>Called by the main app at startup.</summary>
    public static void Launch()
    {
        if (Suppressed)
        {
            return;
        }

        try
        {
            Stop();
            int pid = Environment.ProcessId;
            string token = Guid.NewGuid().ToString("N");
            _cleanExitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, CleanExitEventName(pid, token));
            var psi = new ProcessStartInfo(AppInfo.ExecutablePath, $"{Argument} {pid} {token}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            _process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not start the safety watchdog.", ex);
        }
    }

    /// <summary>Called by the main app on a clean exit so the watchdog stands down.</summary>
    public static void SignalCleanExit()
    {
        try
        {
            _cleanExitEvent?.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Called by the main app when the user turns the watchdog off.</summary>
    public static void Stop()
    {
        SignalCleanExit();
        _cleanExitEvent?.Dispose();
        _cleanExitEvent = null;
        _process?.Dispose();
        _process = null;
    }

    public static bool IsRunning => _process is { HasExited: false };

    /// <summary>Entry point of the watchdog process.</summary>
    public static int Run(string[] args)
    {
        if (args.Length < 3 || !int.TryParse(args[1], out int pid))
        {
            return 2;
        }

        string token = args[2];

        Process parent;
        try
        {
            parent = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return 0;
        }

        if (!EventWaitHandle.TryOpenExisting(CleanExitEventName(pid, token), out var cleanExit))
        {
            return 0;
        }

        using (cleanExit)
        using (parent)
        {
            var parentHandle = new ProcessWaitHandle(parent);
            int signaled = WaitHandle.WaitAny(new WaitHandle[] { cleanExit, parentHandle });
            if (signaled == 0 || cleanExit.WaitOne(0))
            {
                return 0; // Clean exit: the app restored everything itself.
            }

            // The app ended unexpectedly. If a new instance is already running it manages settings itself.
            if (Mutex.TryOpenExisting(SingleInstance.MutexName, out var mutex))
            {
                mutex.Dispose();
                return 0;
            }

            Log.Initialize(StoragePaths.LogDirectory);
            Log.Warn($"Synapse (pid {pid}) ended unexpectedly; restoring Windows mouse settings.");
            try
            {
                WindowsMouseSettings.RestoreBaselineUnconditionally();
                ReleaseStuckButtons();
            }
            catch (Exception ex)
            {
                Log.Error("Watchdog restore failed.", ex);
            }
        }

        return 0;
    }

    private static void ReleaseStuckButtons()
    {
        var events = new List<SynapseMouse.Core.Input.OutputEvent>();
        void ReleaseIfDown(int vk, SynapseMouse.Core.Models.MouseButton button)
        {
            if (IsKeyDown(vk))
            {
                events.Add(SynapseMouse.Core.Input.OutputEvent.ButtonUp(0, button));
            }
        }

        ReleaseIfDown(0x01, SynapseMouse.Core.Models.MouseButton.Left);
        ReleaseIfDown(0x02, SynapseMouse.Core.Models.MouseButton.Right);
        ReleaseIfDown(0x04, SynapseMouse.Core.Models.MouseButton.Middle);
        ReleaseIfDown(0x05, SynapseMouse.Core.Models.MouseButton.XButton1);
        ReleaseIfDown(0x06, SynapseMouse.Core.Models.MouseButton.XButton2);
        InputInjector.Send(events);
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(Process process)
        {
            SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(process.Handle, ownsHandle: false);
        }
    }
}
