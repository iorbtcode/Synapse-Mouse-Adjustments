using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using MouseButton = SynapseMouse.Core.Models.MouseButton;
using System.Windows.Threading;
using SynapseMouse.App.Controls;
using SynapseMouse.App.Native;
using SynapseMouse.App.Services;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.ViewModels;

/// <summary>Live state of one mouse button: what the mouse sent vs. what Windows received.</summary>
internal sealed class ButtonStateItem : ViewModelBase
{
    private bool _physicalDown;
    private bool _outputDown;
    private int _physicalPresses;
    private int _outputPresses;

    public ButtonStateItem(MouseButton button)
    {
        Button = button;
    }

    public MouseButton Button { get; }

    public string Name => Button.ShortName();

    public bool PhysicalDown
    {
        get => _physicalDown;
        set => SetField(ref _physicalDown, value);
    }

    public bool OutputDown
    {
        get => _outputDown;
        set => SetField(ref _outputDown, value);
    }

    public int PhysicalPresses
    {
        get => _physicalPresses;
        set => SetField(ref _physicalPresses, value);
    }

    public int OutputPresses
    {
        get => _outputPresses;
        set => SetField(ref _outputPresses, value);
    }
}

/// <summary>Click timing statistics for one button and one side (physical or output).</summary>
internal sealed class ClickTimer
{
    private readonly Queue<double> _presses = new();
    private double _lastDown = double.NaN;

    public double? LastHoldMs { get; private set; }

    public double? LastIntervalMs { get; private set; }

    public void Down(double t)
    {
        if (!double.IsNaN(_lastDown))
        {
            LastIntervalMs = t - _lastDown;
        }

        _lastDown = t;
        _presses.Enqueue(t);
    }

    public void Up(double t)
    {
        if (!double.IsNaN(_lastDown))
        {
            LastHoldMs = t - _lastDown;
        }
    }

    public int ClicksPerSecond(double now)
    {
        while (_presses.Count > 0 && now - _presses.Peek() > 1000)
        {
            _presses.Dequeue();
        }

        return _presses.Count;
    }

    public void Reset()
    {
        _presses.Clear();
        _lastDown = double.NaN;
        LastHoldMs = null;
        LastIntervalMs = null;
    }
}

internal sealed class TesterViewModel : PageViewModel
{
    private const double HistoryMs = 4000;

    private readonly DispatcherTimer _timer;
    private readonly List<TimelineSpan> _spans = new();
    private readonly List<TimelineMark> _marks = new();
    private readonly Dictionary<(int Lane, bool Output), TimelineSpan> _open = new();
    private readonly ClickTimer[,] _timers = new ClickTimer[2, 2]; // [left/right, physical/output]
    private EngineStats _baseStats;
    private long _lastPhysicalMoves;
    private long _lastSynapseMoves;
    private DateTime _lastMoveSample = DateTime.UtcNow;
    private string _movementText = "—";
    private string _cursorText = "—";
    private string _doubleClickText = "Double-click the test pad to check Windows double-click detection.";
    private int _wheelUpPhysical;
    private int _wheelDownPhysical;
    private int _wheelUpOutput;
    private int _wheelDownOutput;
    private int _wheelHorizontal;

    public TesterViewModel(AppController controller)
        : base(controller, "tester", "Input Tester", "Live view of what your mouse sends and what Windows receives after Synapse. Use it to verify debounce, remapping and Single → Double click.")
    {
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                _timers[i, j] = new ClickTimer();
            }
        }

        Buttons = new ObservableCollection<ButtonStateItem>(
            new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.XButton1, MouseButton.XButton2 }.Select(b => new ButtonStateItem(b)));
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Poll();
        ClearCommand = new RelayCommand(Clear);
    }

    /// <summary>Raised ~30×/s with fresh timeline data for the view to draw.</summary>
    public event Action<IReadOnlyList<TimelineSpan>, IReadOnlyList<TimelineMark>, double>? TimelineUpdated;

    public ObservableCollection<ButtonStateItem> Buttons { get; }

    public ObservableCollection<string> EventLog { get; } = new();

    public ICommand ClearCommand { get; }

    public string MovementText => _movementText;

    public string CursorText => _cursorText;

    public string DoubleClickText => _doubleClickText;

    public string WheelText => string.Format(CultureInfo.CurrentCulture,
        "Mouse sent: ▲ {0}  ▼ {1}    ·    Windows received: ▲ {2}  ▼ {3}    ·    Horizontal: {4}",
        _wheelUpPhysical, _wheelDownPhysical, _wheelUpOutput, _wheelDownOutput, _wheelHorizontal);

    public string LeftTiming => Timing(0);

    public string RightTiming => Timing(1);

    public string FilterText
    {
        get
        {
            var s = Controller.Engine.GetStats();
            return string.Format(CultureInfo.CurrentCulture,
                "Since opening: {0} bounce events filtered by debounce · {1} duplicate presses dropped · {2} Single → Double clicks generated · {3} remapped events",
                s.BouncesFiltered - _baseStats.BouncesFiltered, s.DuplicatesFiltered - _baseStats.DuplicatesFiltered,
                s.DoubleClicks - _baseStats.DoubleClicks, s.Remapped - _baseStats.Remapped);
        }
    }

    public string StatusText => Controller.MasterEnabled
        ? $"Master Enable is ON — config \"{Controller.ActiveConfig.Name}\" is being applied."
        : "Master Enable is OFF — you are seeing unmodified input.";

    protected override void OnActiveChanged(bool active)
    {
        Controller.Engine.TraceEnabled = active;
        if (active)
        {
            _baseStats = Controller.Engine.GetStats();
            _lastPhysicalMoves = Controller.Engine.PhysicalMoveEvents;
            _lastSynapseMoves = Controller.Engine.SynapseMoveEvents;
            _lastMoveSample = DateTime.UtcNow;
            _timer.Start();
            RefreshAll();
        }
        else
        {
            _timer.Stop();
            foreach (var b in Buttons)
            {
                b.PhysicalDown = false;
                b.OutputDown = false;
            }

            _open.Clear();
        }
    }

    public void OnPadDoubleClick(int windowsDoubleClickTime)
    {
        _doubleClickText = string.Format(CultureInfo.CurrentCulture, "Double-click detected by Windows at {0:HH:mm:ss.fff} (Windows double-click time: {1} ms).",
            DateTime.Now, windowsDoubleClickTime);
        OnPropertyChanged(nameof(DoubleClickText));
    }

    private void Poll()
    {
        double now = Controller.Engine.NowUs / 1000.0;
        bool changed = false;
        while (Controller.Engine.Trace.TryDequeue(out var e))
        {
            Handle(e);
            changed = true;
        }

        // Movement rates.
        var elapsed = (DateTime.UtcNow - _lastMoveSample).TotalSeconds;
        if (elapsed >= 0.25)
        {
            long physical = Controller.Engine.PhysicalMoveEvents;
            long synapse = Controller.Engine.SynapseMoveEvents;
            double physicalRate = (physical - _lastPhysicalMoves) / elapsed;
            double synapseRate = (synapse - _lastSynapseMoves) / elapsed;
            _lastPhysicalMoves = physical;
            _lastSynapseMoves = synapse;
            _lastMoveSample = DateTime.UtcNow;
            _movementText = physicalRate < 1
                ? "Not moving"
                : synapseRate > 0
                    ? string.Format(CultureInfo.CurrentCulture, "{0:0} move events/s from the mouse → {1:0}/s re-sent by Synapse (software sensitivity)", physicalRate, synapseRate)
                    : string.Format(CultureInfo.CurrentCulture, "{0:0} move events/s (passed through unchanged)", physicalRate);
            OnPropertyChanged(nameof(MovementText));
            OnPropertyChanged(nameof(FilterText));
            OnPropertyChanged(nameof(StatusText));
        }

        NativeMethods.GetCursorPos(out var cursor);
        string cursorText = string.Format(CultureInfo.CurrentCulture, "X {0}   Y {1}", cursor.X, cursor.Y);
        if (cursorText != _cursorText)
        {
            _cursorText = cursorText;
            OnPropertyChanged(nameof(CursorText));
        }

        if (changed)
        {
            OnPropertyChanged(nameof(WheelText));
            OnPropertyChanged(nameof(LeftTiming));
            OnPropertyChanged(nameof(RightTiming));
        }
        else
        {
            // CPS decays over time even without new events.
            OnPropertyChanged(nameof(LeftTiming));
            OnPropertyChanged(nameof(RightTiming));
        }

        Trim(now);
        TimelineUpdated?.Invoke(_spans, _marks, now);
    }

    private void Handle(TraceEvent e)
    {
        double t = e.TimeUs / 1000.0;
        bool physical = e.Origin is TraceOrigin.Physical or TraceOrigin.PhysicalBlocked;
        bool output = e.Origin is TraceOrigin.Physical or TraceOrigin.Synapse or TraceOrigin.OtherInjected;

        if (e.Kind is TraceKind.Wheel or TraceKind.HWheel)
        {
            bool up = e.Delta > 0;
            if (e.Kind == TraceKind.HWheel)
            {
                if (physical)
                {
                    _wheelHorizontal++;
                }
            }
            else
            {
                if (physical)
                {
                    if (up)
                    {
                        _wheelUpPhysical++;
                    }
                    else
                    {
                        _wheelDownPhysical++;
                    }
                }

                if (output)
                {
                    if (up)
                    {
                        _wheelUpOutput++;
                    }
                    else
                    {
                        _wheelDownOutput++;
                    }
                }
            }

            if (physical)
            {
                _marks.Add(new TimelineMark(5, false, t, up));
            }

            if (output)
            {
                _marks.Add(new TimelineMark(5, true, t, up));
            }

            Log(e, t);
            return;
        }

        int lane = (int)e.Button;
        bool down = e.Kind == TraceKind.ButtonDown;
        var item = Buttons[lane];
        if (physical)
        {
            item.PhysicalDown = down;
            if (down)
            {
                item.PhysicalPresses++;
            }

            UpdateSpan(lane, false, down, t);
            UpdateTimer(e.Button, 0, down, t);
        }

        if (output)
        {
            item.OutputDown = down;
            if (down)
            {
                item.OutputPresses++;
            }

            UpdateSpan(lane, true, down, t);
            UpdateTimer(e.Button, 1, down, t);
        }

        Log(e, t);
    }

    private void UpdateSpan(int lane, bool output, bool down, double t)
    {
        var key = (lane, output);
        if (down)
        {
            if (_open.TryGetValue(key, out var existing))
            {
                existing.EndMs ??= t;
            }

            var span = new TimelineSpan { Lane = lane, IsOutput = output, StartMs = t };
            _spans.Add(span);
            _open[key] = span;
        }
        else if (_open.Remove(key, out var span))
        {
            span.EndMs = t;
        }
    }

    private void UpdateTimer(MouseButton button, int side, bool down, double t)
    {
        int index = button switch
        {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            _ => -1,
        };
        if (index < 0)
        {
            return;
        }

        if (down)
        {
            _timers[index, side].Down(t);
        }
        else
        {
            _timers[index, side].Up(t);
        }
    }

    private string Timing(int index)
    {
        double now = Controller.Engine.NowUs / 1000.0;
        string Side(ClickTimer c) => string.Format(CultureInfo.CurrentCulture, "hold {0} · interval {1} · {2} CPS",
            c.LastHoldMs is { } h ? h.ToString("0.0", CultureInfo.CurrentCulture) + " ms" : "—",
            c.LastIntervalMs is { } i ? i.ToString("0.0", CultureInfo.CurrentCulture) + " ms" : "—",
            c.ClicksPerSecond(now));
        return $"Mouse: {Side(_timers[index, 0])}\nWindows: {Side(_timers[index, 1])}";
    }

    private void Log(TraceEvent e, double t)
    {
        string what = e.Kind switch
        {
            TraceKind.ButtonDown => e.Button.ShortName() + " press",
            TraceKind.ButtonUp => e.Button.ShortName() + " release",
            TraceKind.Wheel => e.Delta > 0 ? $"Wheel up ({e.Delta})" : $"Wheel down ({e.Delta})",
            _ => e.Delta > 0 ? $"Wheel right ({e.Delta})" : $"Wheel left ({e.Delta})",
        };
        string origin = e.Origin switch
        {
            TraceOrigin.Physical => "mouse → Windows",
            TraceOrigin.PhysicalBlocked => "mouse (handled by Synapse)",
            TraceOrigin.Synapse => "generated by Synapse",
            _ => "injected by another app",
        };
        EventLog.Insert(0, string.Format(CultureInfo.CurrentCulture, "{0,10:0.0} ms   {1,-22} {2}", t % 100000, what, origin));
        while (EventLog.Count > 80)
        {
            EventLog.RemoveAt(EventLog.Count - 1);
        }
    }

    private void Trim(double now)
    {
        double cutoff = now - HistoryMs;
        _spans.RemoveAll(s => s.EndMs is { } end && end < cutoff);
        _marks.RemoveAll(m => m.TimeMs < cutoff);
    }

    private void Clear()
    {
        _spans.Clear();
        _marks.Clear();
        _open.Clear();
        EventLog.Clear();
        foreach (var b in Buttons)
        {
            b.PhysicalPresses = 0;
            b.OutputPresses = 0;
        }

        foreach (var timer in _timers)
        {
            timer.Reset();
        }

        _wheelUpPhysical = _wheelDownPhysical = _wheelUpOutput = _wheelDownOutput = _wheelHorizontal = 0;
        _baseStats = Controller.Engine.GetStats();
        _doubleClickText = "Double-click the test pad to check Windows double-click detection.";
        RefreshAll();
    }
}
