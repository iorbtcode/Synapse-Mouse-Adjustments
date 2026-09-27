using System.Globalization;
using Microsoft.Win32;
using SynapseMouse.App.Infrastructure;
using static SynapseMouse.App.Native.NativeMethods;

namespace SynapseMouse.App.Services;

/// <summary>Windows mouse settings Synapse may override. Null means "leave the Windows setting alone".</summary>
internal sealed record DesiredWindowsSettings(
    int? PointerSpeed,
    bool? EnhancePointerPrecision,
    int? ScrollLines,
    int? ScrollChars,
    int? DoubleClickTime)
{
    public static DesiredWindowsSettings None { get; } = new(null, null, null, null, null);
}

/// <summary>The user's own Windows mouse settings, as stored in their profile.</summary>
internal sealed record WindowsMouseBaseline(int PointerSpeed, int[] MouseParams, int ScrollLines, int ScrollChars, int DoubleClickTime)
{
    public bool EnhancePointerPrecision => MouseParams[2] != 0;
}

/// <summary>
/// Applies Windows pointer speed, acceleration, wheel scroll amounts and double-click time.
///
/// Safety model: changes are made for the current session only (never written to the user profile),
/// so the user's real settings stay in the registry untouched. "Restoring normal Windows behavior"
/// simply re-applies the values from the registry — which also works after a crash (via the watchdog
/// or the next launch), and Windows itself reverts session values at sign-out.
/// </summary>
internal sealed class WindowsMouseSettings
{
    private readonly object _gate = new();
    private readonly object _applyGate = new();
    private readonly SemaphoreSlim _workerSignal = new(0);
    private DesiredWindowsSettings _pending = DesiredWindowsSettings.None;
    private bool _hasPending;
    private volatile bool _speedModified;
    private bool _accelModified;
    private bool _linesModified;
    private bool _charsModified;
    private bool _doubleClickModified;
    private Thread? _worker;

    /// <summary>Reads the user's persisted settings (what Windows uses at sign-in).</summary>
    public static WindowsMouseBaseline ReadBaseline()
    {
        int speed = ReadRegistryInt(@"Control Panel\Mouse", "MouseSensitivity", 10);
        int accel = ReadRegistryInt(@"Control Panel\Mouse", "MouseSpeed", 1);
        int t1 = ReadRegistryInt(@"Control Panel\Mouse", "MouseThreshold1", 6);
        int t2 = ReadRegistryInt(@"Control Panel\Mouse", "MouseThreshold2", 10);
        int doubleClick = ReadRegistryInt(@"Control Panel\Mouse", "DoubleClickSpeed", 500);
        int lines = ReadRegistryInt(@"Control Panel\Desktop", "WheelScrollLines", 3);
        int chars = ReadRegistryInt(@"Control Panel\Desktop", "WheelScrollChars", 3);
        return new WindowsMouseBaseline(
            Math.Clamp(speed, 1, 20),
            new[] { t1, t2, accel },
            lines,
            Math.Max(0, chars),
            Math.Clamp(doubleClick, 100, 5000));
    }

    public static int GetSessionSpeed()
    {
        uint speed = 10;
        return SystemParametersInfo(SPI_GETMOUSESPEED, 0, ref speed, 0) ? (int)Math.Clamp(speed, 1u, 20u) : 10;
    }

    public static bool GetSessionEnhancePointerPrecision()
    {
        var values = new int[3];
        return SystemParametersInfo(SPI_GETMOUSE, 0, values, 0) && values[2] != 0;
    }

    public static int GetSessionScrollLines()
    {
        uint lines = 3;
        SystemParametersInfo(SPI_GETWHEELSCROLLLINES, 0, ref lines, 0);
        return lines == WHEEL_PAGESCROLL ? -1 : (int)lines;
    }

    public static int GetSessionScrollChars()
    {
        uint chars = 3;
        SystemParametersInfo(SPI_GETWHEELSCROLLCHARS, 0, ref chars, 0);
        return (int)chars;
    }

    public static int GetSessionDoubleClickTime() => (int)GetDoubleClickTime();

    /// <summary>Queues the desired state; applied on a background thread (setting broadcasts can block).</summary>
    public void ApplyAsync(DesiredWindowsSettings desired)
    {
        lock (_gate)
        {
            _pending = desired;
            _hasPending = true;
            if (_worker is null)
            {
                _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Synapse Windows settings" };
                _worker.Start();
            }
        }

        _workerSignal.Release();
    }

    /// <summary>Synchronously restores everything Synapse changed (used on exit).</summary>
    public void RestoreNow()
    {
        lock (_gate)
        {
            _pending = DesiredWindowsSettings.None;
            _hasPending = false;
        }

        lock (_applyGate)
        {
            ApplyCore(DesiredWindowsSettings.None);
        }
    }

    /// <summary>The pointer speed that will be in effect once <paramref name="desired"/> is applied.</summary>
    public int PredictPointerSpeed(DesiredWindowsSettings desired) =>
        desired.PointerSpeed ?? (_speedModified ? ReadBaseline().PointerSpeed : GetSessionSpeed());

    /// <summary>Unconditionally re-applies the user's persisted settings (watchdog / cleanup).</summary>
    public static void RestoreBaselineUnconditionally()
    {
        var b = ReadBaseline();
        SetSpeed(b.PointerSpeed);
        SetMouseParams(b.MouseParams);
        SetScrollLines(b.ScrollLines);
        SetScrollChars(b.ScrollChars);
        SetDoubleClickTime(b.DoubleClickTime);
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _workerSignal.Wait();
            DesiredWindowsSettings next;
            lock (_gate)
            {
                if (!_hasPending)
                {
                    continue;
                }

                _hasPending = false;
                next = _pending;
            }

            lock (_applyGate)
            {
                try
                {
                    ApplyCore(next);
                }
                catch (Exception ex)
                {
                    Log.Error("Applying Windows mouse settings failed.", ex);
                }
            }
        }
    }

    /// <summary>Must be called with <see cref="_applyGate"/> held.</summary>
    private void ApplyCore(DesiredWindowsSettings d)
    {
        WindowsMouseBaseline? baseline = null;
        WindowsMouseBaseline Baseline() => baseline ??= ReadBaseline();

        if (d.PointerSpeed is { } speed)
        {
            if (GetSessionSpeed() != speed)
            {
                SetSpeed(speed);
            }

            _speedModified = true;
        }
        else if (_speedModified)
        {
            SetSpeed(Baseline().PointerSpeed);
            _speedModified = false;
        }

        if (d.EnhancePointerPrecision is { } epp)
        {
            if (GetSessionEnhancePointerPrecision() != epp)
            {
                int[] values = epp
                    ? (Baseline().EnhancePointerPrecision ? Baseline().MouseParams : new[] { 6, 10, 1 })
                    : new[] { 0, 0, 0 };
                SetMouseParams(values);
            }

            _accelModified = true;
        }
        else if (_accelModified)
        {
            SetMouseParams(Baseline().MouseParams);
            _accelModified = false;
        }

        if (d.ScrollLines is { } lines)
        {
            if (GetSessionScrollLines() != lines)
            {
                SetScrollLines(lines);
            }

            _linesModified = true;
        }
        else if (_linesModified)
        {
            SetScrollLines(Baseline().ScrollLines);
            _linesModified = false;
        }

        if (d.ScrollChars is { } chars)
        {
            if (GetSessionScrollChars() != chars)
            {
                SetScrollChars(chars);
            }

            _charsModified = true;
        }
        else if (_charsModified)
        {
            SetScrollChars(Baseline().ScrollChars);
            _charsModified = false;
        }

        if (d.DoubleClickTime is { } time)
        {
            if (GetSessionDoubleClickTime() != time)
            {
                SetDoubleClickTime(time);
            }

            _doubleClickModified = true;
        }
        else if (_doubleClickModified)
        {
            SetDoubleClickTime(Baseline().DoubleClickTime);
            _doubleClickModified = false;
        }
    }

    // fWinIni = 0 → session only, nothing is written to the user's profile.
    private static void SetSpeed(int speed) =>
        Check(SystemParametersInfo(SPI_SETMOUSESPEED, 0, (IntPtr)Math.Clamp(speed, 1, 20), 0), "pointer speed");

    private static void SetMouseParams(int[] values) =>
        Check(SystemParametersInfo(SPI_SETMOUSE, 0, (int[])values.Clone(), 0), "pointer acceleration");

    private static void SetScrollLines(int lines) =>
        Check(SystemParametersInfo(SPI_SETWHEELSCROLLLINES, lines < 0 ? WHEEL_PAGESCROLL : (uint)lines, IntPtr.Zero, SPIF_SENDCHANGE), "scroll lines");

    private static void SetScrollChars(int chars) =>
        Check(SystemParametersInfo(SPI_SETWHEELSCROLLCHARS, (uint)Math.Max(0, chars), IntPtr.Zero, SPIF_SENDCHANGE), "scroll characters");

    private static void SetDoubleClickTime(int ms) =>
        Check(SystemParametersInfo(SPI_SETDOUBLECLICKTIME, (uint)ms, IntPtr.Zero, SPIF_SENDCHANGE), "double-click time");

    private static void Check(bool ok, string what)
    {
        if (!ok)
        {
            Log.Warn($"Windows rejected the {what} change (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}).");
        }
    }

    private static int ReadRegistryInt(string key, string name, int fallback)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(key);
            object? value = k?.GetValue(name);
            return value switch
            {
                int i => i,
                string s when int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
                _ => fallback,
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }
}
