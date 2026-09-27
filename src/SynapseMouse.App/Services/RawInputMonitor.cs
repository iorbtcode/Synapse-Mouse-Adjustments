using System.Diagnostics;
using System.Runtime.InteropServices;
using SynapseMouse.App.Infrastructure;
using static SynapseMouse.App.Native.NativeMethods;

namespace SynapseMouse.App.Services;

/// <summary>Result of measuring how often a mouse sends movement reports.</summary>
internal readonly record struct ReportRate(IntPtr Device, double CurrentHz, double PeakHz, bool Moving);

/// <summary>
/// Measures the real report (polling) rate of the mouse by counting Raw Input movement reports.
/// Only active while the Polling Rate or Mouse page is open, so it costs nothing otherwise.
/// This is a measurement only — it cannot change the rate, which is fixed by the mouse firmware.
/// </summary>
internal sealed class RawInputMonitor : IDisposable
{
    private const double WindowSeconds = 0.25;

    private readonly MessageWindow _window;
    private readonly int _headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
    private readonly Dictionary<IntPtr, DeviceCounter> _counters = new();
    private IntPtr _buffer;
    private uint _bufferSize;
    private int _users;

    public RawInputMonitor(MessageWindow window)
    {
        _window = window;
    }

    /// <summary>The device that most recently reported movement.</summary>
    public IntPtr LastDevice { get; private set; }

    public event EventHandler? ActiveDeviceChanged;

    public void Start()
    {
        if (_users++ > 0)
        {
            return;
        }

        var device = new[]
        {
            new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = _window.Handle },
        };
        if (!RegisterRawInputDevices(device, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            Log.Warn($"Raw input registration failed (error {Marshal.GetLastWin32Error()}).");
        }

        _window.MessageReceived += OnMessage;
    }

    public void Stop()
    {
        if (_users == 0 || --_users > 0)
        {
            return;
        }

        _window.MessageReceived -= OnMessage;
        var device = new[] { new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero } };
        RegisterRawInputDevices(device, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        _counters.Clear();
    }

    public void ResetPeaks()
    {
        foreach (var c in _counters.Values)
        {
            c.PeakHz = 0;
        }
    }

    /// <summary>Rate for the most recently active device.</summary>
    public ReportRate GetRate()
    {
        if (LastDevice == IntPtr.Zero || !_counters.TryGetValue(LastDevice, out var counter))
        {
            return default;
        }

        long now = Stopwatch.GetTimestamp();
        double sinceLast = (now - counter.LastEventTicks) / (double)Stopwatch.Frequency;
        bool moving = sinceLast < 0.15;
        return new ReportRate(LastDevice, moving ? counter.CurrentHz : 0, counter.PeakHz, moving);
    }

    public void Dispose()
    {
        _users = Math.Min(_users, 1);
        Stop();
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT)
        {
            return;
        }

        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)_headerSize);
        if (size == 0)
        {
            return;
        }

        if (size > _bufferSize)
        {
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
            }

            _buffer = Marshal.AllocHGlobal((int)size);
            _bufferSize = size;
        }

        if (GetRawInputData(lParam, RID_INPUT, _buffer, ref size, (uint)_headerSize) == uint.MaxValue)
        {
            return;
        }

        var header = Marshal.PtrToStructure<RAWINPUTHEADER>(_buffer);
        if (header.dwType != RIM_TYPEMOUSE)
        {
            return;
        }

        // RAWMOUSE: usFlags(2) pad(2) buttons(4) rawButtons(4) lLastX(4) lLastY(4) extra(4)
        int lastX = Marshal.ReadInt32(_buffer, _headerSize + 12);
        int lastY = Marshal.ReadInt32(_buffer, _headerSize + 16);
        if (lastX == 0 && lastY == 0)
        {
            return;
        }

        if (!_counters.TryGetValue(header.hDevice, out var counter))
        {
            counter = new DeviceCounter();
            _counters[header.hDevice] = counter;
        }

        counter.Add(Stopwatch.GetTimestamp());
        if (header.hDevice != LastDevice && header.hDevice != IntPtr.Zero)
        {
            LastDevice = header.hDevice;
            ActiveDeviceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class DeviceCounter
    {
        private long _windowStart;
        private int _count;

        public long LastEventTicks { get; private set; }

        public double CurrentHz { get; private set; }

        public double PeakHz { get; set; }

        public void Add(long ticks)
        {
            double gap = (ticks - LastEventTicks) / (double)Stopwatch.Frequency;
            LastEventTicks = ticks;
            if (_windowStart == 0 || gap > 0.1)
            {
                // Movement (re)started: begin a fresh window so pauses do not lower the measurement.
                _windowStart = ticks;
                _count = 0;
                return;
            }

            _count++;
            double elapsed = (ticks - _windowStart) / (double)Stopwatch.Frequency;
            if (elapsed >= WindowSeconds)
            {
                CurrentHz = _count / elapsed;
                PeakHz = Math.Max(PeakHz, CurrentHz);
                _windowStart = ticks;
                _count = 0;
            }
        }
    }
}
