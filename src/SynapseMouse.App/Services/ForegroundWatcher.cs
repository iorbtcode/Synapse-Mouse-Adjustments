using System.Diagnostics;
using System.Text;
using SynapseMouse.App.Infrastructure;
using static SynapseMouse.App.Native.NativeMethods;

namespace SynapseMouse.App.Services;

internal sealed record ForegroundInfo(IntPtr Window, uint ProcessId, string? ProcessName, string? Title, bool IsElevated, bool IsOwnProcess);

/// <summary>
/// Tracks the foreground application (event driven, no polling) for application-specific configs and
/// for pausing input replacement over elevated windows. Title changes are only watched for the
/// foreground process, keeping the event volume tiny.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly WinEventDelegate _callback;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly bool _selfElevated;
    private IntPtr _foregroundHook;
    private IntPtr _titleHook;
    private uint _titleHookProcess;
    private bool _watchTitles;

    public ForegroundWatcher()
    {
        _callback = OnWinEvent;
        _selfElevated = IsProcessElevated(GetCurrentProcess());
    }

    public event EventHandler<ForegroundInfo>? ForegroundChanged;

    public ForegroundInfo? Current { get; private set; }

    /// <summary>True if Synapse itself runs as administrator (then elevated windows are not a problem).</summary>
    public bool SelfElevated => _selfElevated;

    /// <summary>Watch window-title changes of the foreground app (needed for title-based associations).</summary>
    public bool WatchTitles
    {
        get => _watchTitles;
        set
        {
            _watchTitles = value;
            UpdateTitleHook(Current?.ProcessId ?? 0);
        }
    }

    public void Start()
    {
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_foregroundHook == IntPtr.Zero)
        {
            Log.Warn("Foreground tracking unavailable.");
        }

        Evaluate(GetForegroundWindow());
    }

    public void Dispose()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        UpdateTitleHook(0, remove: true);
    }

    /// <summary>Visible top-level windows of other processes (for the "pick an app" dialog).</summary>
    public static List<(string ProcessName, string Title)> ListWindowedApps()
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint own = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
            {
                return true;
            }

            string title = GetTitle(hwnd) ?? string.Empty;
            if (title.Length == 0)
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == own)
            {
                return true;
            }

            string? name = GetProcessName(pid);
            if (name is not null && seen.Add(name + "|" + title))
            {
                result.Add((name, title));
            }

            return true;
        }, IntPtr.Zero);
        return result.OrderBy(r => r.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                Evaluate(hwnd);
            }
            else if (eventType == EVENT_OBJECT_NAMECHANGE && idObject == OBJID_WINDOW && idChild == 0 && hwnd == GetForegroundWindow())
            {
                Evaluate(hwnd);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Foreground tracking failed.", ex);
        }
    }

    private void Evaluate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);
        string? title = GetTitle(hwnd);
        if (Current is { } previous && previous.Window == hwnd && previous.ProcessId == pid && previous.Title == title)
        {
            return;
        }

        bool own = pid == _ownProcessId;
        string? name = own ? Path.GetFileName(AppInfo.ExecutablePath) : GetProcessName(pid);
        bool elevated = !own && !_selfElevated && IsElevated(pid);
        Current = new ForegroundInfo(hwnd, pid, name, title, elevated, own);
        UpdateTitleHook(pid);
        ForegroundChanged?.Invoke(this, Current);
    }

    private void UpdateTitleHook(uint pid, bool remove = false)
    {
        bool want = !remove && _watchTitles && pid != 0;
        if (want && _titleHook != IntPtr.Zero && _titleHookProcess == pid)
        {
            return;
        }

        if (_titleHook != IntPtr.Zero)
        {
            UnhookWinEvent(_titleHook);
            _titleHook = IntPtr.Zero;
            _titleHookProcess = 0;
        }

        if (want)
        {
            _titleHook = SetWinEventHook(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _callback, pid, 0, WINEVENT_OUTOFCONTEXT);
            _titleHookProcess = pid;
        }
    }

    private static string? GetTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : null;
    }

    private static string? GetProcessName(uint pid)
    {
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
        {
            try
            {
                return Process.GetProcessById((int)pid).ProcessName + ".exe";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }

        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(process, 0, sb, ref size) ? Path.GetFileName(sb.ToString()) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool IsElevated(uint pid)
    {
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return IsProcessElevated(process);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool IsProcessElevated(IntPtr process)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out IntPtr token))
        {
            // A normal user cannot open an administrator process's token: treat that as elevated.
            return true;
        }

        try
        {
            return GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) && elevated != 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
