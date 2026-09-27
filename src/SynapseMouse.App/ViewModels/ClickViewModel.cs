using System.Globalization;
using System.Windows.Threading;
using SynapseMouse.App.Services;
using SynapseMouse.Core.Config;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.ViewModels;

internal sealed class ClickViewModel : ConfigPageViewModel
{
    private readonly DispatcherTimer _statsTimer;

    public ClickViewModel(AppController controller)
        : base(controller, "click", "Click Settings", "Debounce, Single → Double click and click timing, processed in software for every app.")
    {
        _statsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) => OnPropertyChanged(nameof(StatsText));
    }

    // ------------------------------------------------------------------ debounce

    public bool DebounceEnabled
    {
        get => Config.Debounce.Enabled;
        set => Edit(c => c.Debounce.Enabled = value);
    }

    public double DebounceMs
    {
        get => Config.Debounce.TimeMs;
        set => Edit(c => c.Debounce.TimeMs = ConfigSanitizer.DebounceMs.Clamp((int)Math.Round(value)));
    }

    public DebounceMode DebounceMode
    {
        get => Config.Debounce.Mode;
        set => Edit(c => c.Debounce.Mode = value);
    }

    public MouseButtonFlags DebounceButtons
    {
        get => Config.Debounce.Buttons;
        set => Edit(c => c.Debounce.Buttons = value);
    }

    public string DebounceSummary => Config.Debounce.Enabled
        ? string.Format(CultureInfo.CurrentCulture, "Current value: {0} ms ({1})", Config.Debounce.TimeMs,
            Config.Debounce.Mode == DebounceMode.Eager ? "instant press, bounce filtered" : "instant press, confirmed release")
        : "Off — every press and release from the mouse is passed on unchanged.";

    // ------------------------------------------------------------------ single → double

    public bool DoubleEnabled
    {
        get => Config.DoubleClick.Enabled;
        set => Edit(c => c.DoubleClick.Enabled = value);
    }

    public MouseButtonFlags DoubleButtons
    {
        get => Config.DoubleClick.Buttons;
        set => Edit(c => c.DoubleClick.Buttons = value);
    }

    public double DoubleInterval
    {
        get => Config.DoubleClick.IntervalMs;
        set => Edit(c =>
        {
            c.DoubleClick.IntervalMs = (int)Math.Round(value);
            ConfigSanitizer.Sanitize(c);
        });
    }

    public double DoublePress
    {
        get => Config.DoubleClick.PressDurationMs;
        set => Edit(c =>
        {
            c.DoubleClick.PressDurationMs = (int)Math.Round(value);
            ConfigSanitizer.Sanitize(c);
        });
    }

    public DoubleClickTiming DoubleTiming
    {
        get => Config.DoubleClick.Timing;
        set => Edit(c => c.DoubleClick.Timing = value);
    }

    public string DoubleSummary
    {
        get
        {
            var d = Config.DoubleClick;
            if (!d.Enabled)
            {
                return "Off — one physical click produces one click.";
            }

            string timing = d.Timing == DoubleClickTiming.OnPress
                ? $"click 1 at press, click 2 starts {d.IntervalMs} ms later"
                : $"click 2 starts {d.IntervalMs} ms after click 1, once you release";
            string warning = d.IntervalMs >= WindowsSettingsDoubleClickTime
                ? " Note: the interval is longer than the Windows double-click time, so Windows apps see two single clicks."
                : string.Empty;
            return $"One physical click → two clicks ({timing}, each held {d.PressDurationMs} ms).{warning}";
        }
    }

    // ------------------------------------------------------------------ click response

    public bool ResponseEnabled
    {
        get => Config.ClickResponse.Enabled;
        set => Edit(c => c.ClickResponse.Enabled = value);
    }

    public MouseButtonFlags ResponseButtons
    {
        get => Config.ClickResponse.Buttons;
        set => Edit(c => c.ClickResponse.Buttons = value);
    }

    public double PressDelay
    {
        get => Config.ClickResponse.PressDelayMs;
        set => Edit(c => c.ClickResponse.PressDelayMs = ConfigSanitizer.DelayMs.Clamp((int)Math.Round(value)));
    }

    public double ReleaseDelay
    {
        get => Config.ClickResponse.ReleaseDelayMs;
        set => Edit(c => c.ClickResponse.ReleaseDelayMs = ConfigSanitizer.DelayMs.Clamp((int)Math.Round(value)));
    }

    public double MinimumHold
    {
        get => Config.ClickResponse.MinimumHoldMs;
        set => Edit(c => c.ClickResponse.MinimumHoldMs = ConfigSanitizer.MinHoldMs.Clamp((int)Math.Round(value)));
    }

    public bool SuppressDuplicates
    {
        get => Config.ClickResponse.SuppressDuplicateEvents;
        set => Edit(c => c.ClickResponse.SuppressDuplicateEvents = value);
    }

    public bool OverrideDoubleClickTime
    {
        get => Config.ClickResponse.OverrideDoubleClickTime;
        set => Edit(c => c.ClickResponse.OverrideDoubleClickTime = value);
    }

    public double DoubleClickTime
    {
        get => Config.ClickResponse.OverrideDoubleClickTime ? Config.ClickResponse.DoubleClickTimeMs : WindowsSettingsDoubleClickTime;
        set => Edit(c => c.ClickResponse.DoubleClickTimeMs = ConfigSanitizer.DoubleClickTimeMs.Clamp((int)Math.Round(value)));
    }

    public string DoubleClickTimeNote => Config.ClickResponse.OverrideDoubleClickTime
        ? "Applied to Windows while this config is active; your own setting returns when Synapse is disabled."
        : $"Using your Windows setting ({WindowsMouseSettings.ReadBaseline().DoubleClickTime} ms).";

    public string StatsText
    {
        get
        {
            var stats = Controller.Engine.GetStats();
            return string.Format(CultureInfo.CurrentCulture,
                "This session: {0} bounce events filtered · {1} duplicate presses dropped · {2} Single → Double clicks generated",
                stats.BouncesFiltered, stats.DuplicatesFiltered, stats.DoubleClicks);
        }
    }

    private int WindowsSettingsDoubleClickTime => Config.ClickResponse.OverrideDoubleClickTime
        ? Config.ClickResponse.DoubleClickTimeMs
        : WindowsMouseSettings.GetSessionDoubleClickTime();

    protected override void OnActiveChanged(bool active)
    {
        if (active)
        {
            _statsTimer.Start();
            OnPropertyChanged(nameof(StatsText));
        }
        else
        {
            _statsTimer.Stop();
        }
    }

    protected override void OnEdited()
    {
        OnPropertyChanged(nameof(DebounceSummary));
        OnPropertyChanged(nameof(DoubleSummary));
        OnPropertyChanged(nameof(DoubleInterval));
        OnPropertyChanged(nameof(DoublePress));
        OnPropertyChanged(nameof(DoubleClickTime));
        OnPropertyChanged(nameof(DoubleClickTimeNote));
    }
}
