using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using SynapseMouse.App.Services;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.ViewModels;

internal sealed class PollingViewModel : ConfigPageViewModel
{
    private static readonly int[] StandardRates = { 125, 250, 500, 1000, 2000, 4000, 8000 };
    private readonly DispatcherTimer _timer;
    private ReportRate _rate;

    public PollingViewModel(AppController controller)
        : base(controller, "polling", "Polling Rate", "How often your mouse reports to Windows — measured live, never faked.")
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) =>
        {
            _rate = Controller.RawInput.GetRate();
            OnPropertyChanged(nameof(CurrentRateText));
            OnPropertyChanged(nameof(PeakRateText));
            OnPropertyChanged(nameof(NearestStandardText));
            OnPropertyChanged(nameof(MeterFraction));
            OnPropertyChanged(nameof(MeasuringHint));
            OnPropertyChanged(nameof(MeasuredDevice));
        };
        ResetCommand = new RelayCommand(() =>
        {
            Controller.RawInput.ResetPeaks();
            _rate = default;
            RefreshAll();
        });
    }

    public ICommand ResetCommand { get; }

    public IReadOnlyList<int> HardwareRates { get; } = new[] { 125, 250, 500, 1000 };

    public string HardwareSupportText =>
        "Not adjustable for this mouse. A USB mouse's report rate is fixed by its firmware; Windows offers no standard way to change it. " +
        "Gaming mice that support 125–1000 Hz switch rates with vendor-specific commands only their own software can send. " +
        "Synapse will not show a rate it cannot actually set.";

    public string CurrentRateText => _rate.Moving ? string.Format(CultureInfo.CurrentCulture, "{0:0} Hz", _rate.CurrentHz) : "—";

    public string PeakRateText => _rate.PeakHz > 0 ? string.Format(CultureInfo.CurrentCulture, "{0:0} Hz", _rate.PeakHz) : "—";

    public string NearestStandardText
    {
        get
        {
            if (_rate.PeakHz <= 0)
            {
                return "Move your mouse in fast circles for a few seconds.";
            }

            int nearest = StandardRates.OrderBy(r => Math.Abs(Math.Log(r / _rate.PeakHz))).First();
            return string.Format(CultureInfo.CurrentCulture, "Your mouse reports at about {0} Hz ({1:0.0} ms between reports).", nearest, 1000.0 / nearest);
        }
    }

    public double MeterFraction => Math.Clamp(_rate.CurrentHz / 1000.0, 0, 1);

    public string MeasuringHint => _rate.Moving ? "Measuring…" : "Waiting for movement";

    public string MeasuredDevice
    {
        get
        {
            var device = Controller.Devices.Devices.FirstOrDefault(d => d.Handle == _rate.Device);
            return device?.DisplayName ?? (_rate.Device == IntPtr.Zero ? "No movement yet" : "Unidentified device");
        }
    }

    // ------------------------------------------------------------------ software input processing (real settings)

    public IReadOnlyList<Option<InputThreadPriority>> PriorityOptions { get; } = new[]
    {
        new Option<InputThreadPriority>("Normal", InputThreadPriority.Normal),
        new Option<InputThreadPriority>("Above normal", InputThreadPriority.AboveNormal),
        new Option<InputThreadPriority>("Highest (recommended)", InputThreadPriority.Highest),
    };

    public InputThreadPriority ThreadPriority
    {
        get => Config.Input.ThreadPriority;
        set => Edit(c => c.Input.ThreadPriority = value);
    }

    public bool PreciseTiming
    {
        get => Config.Input.PreciseTiming;
        set => Edit(c => c.Input.PreciseTiming = value);
    }

    protected override void OnActiveChanged(bool active)
    {
        if (active)
        {
            Controller.RawInput.Start();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            Controller.RawInput.Stop();
        }
    }
}
