using Microsoft.Win32.SafeHandles;
using SynapseMouse.App.Native;

namespace SynapseMouse.App.Services;

/// <summary>
/// Sleeps until a deadline (or an early wake signal) with sub-millisecond accuracy, so generated clicks
/// (single→double, delays, hold times) land on time. Uses a high-resolution waitable timer
/// (Windows 10 1803+); falls back to timeBeginPeriod(1) only while a timed wait is actually pending.
/// Idle waits use no timer at all, so the engine costs no CPU or power when nothing is scheduled.
/// </summary>
internal sealed class PreciseWaiter : IDisposable
{
    private readonly AutoResetEvent _signal;
    private readonly TimerHandle? _timer;
    private readonly WaitHandle[]? _handles;
    private bool _periodRaised;

    public PreciseWaiter(AutoResetEvent signal)
    {
        _signal = signal;
        SafeWaitHandle handle = NativeMethods.CreateWaitableTimerEx(
            IntPtr.Zero, null, NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, NativeMethods.TIMER_ALL_ACCESS);
        if (!handle.IsInvalid)
        {
            _timer = new TimerHandle(handle);
            _handles = new WaitHandle[] { signal, _timer };
        }
        else
        {
            handle.Dispose();
        }
    }

    /// <summary>When false, timed waits use the default (~15 ms) system timer resolution.</summary>
    public bool Precise { get; set; } = true;

    /// <param name="microseconds">Negative = wait for the signal only.</param>
    public void Wait(long microseconds)
    {
        if (microseconds < 0)
        {
            SetPeriod(false);
            _signal.WaitOne();
            return;
        }

        if (microseconds == 0)
        {
            return;
        }

        if (Precise && _timer is not null)
        {
            long due = -microseconds * 10; // relative, in 100 ns units
            if (NativeMethods.SetWaitableTimer(_timer.Handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                SetPeriod(false);
                WaitHandle.WaitAny(_handles!);
                return;
            }
        }

        SetPeriod(Precise);
        int ms = (int)Math.Clamp((microseconds + 999) / 1000, 1, int.MaxValue);
        _signal.WaitOne(ms);
    }

    public void Dispose()
    {
        SetPeriod(false);
        _timer?.Dispose();
    }

    private void SetPeriod(bool raised)
    {
        if (raised == _periodRaised)
        {
            return;
        }

        if (raised)
        {
            NativeMethods.timeBeginPeriod(1);
        }
        else
        {
            NativeMethods.timeEndPeriod(1);
        }

        _periodRaised = raised;
    }

    private sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(SafeWaitHandle handle)
        {
            SafeWaitHandle = handle;
            Handle = handle;
        }

        public new SafeWaitHandle Handle { get; }
    }
}
