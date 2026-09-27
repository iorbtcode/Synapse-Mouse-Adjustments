using SynapseMouse.App.Services;
using SynapseMouse.Core.Config;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.ViewModels;

internal sealed class ScrollViewModel : ConfigPageViewModel
{
    public ScrollViewModel(AppController controller)
        : base(controller, "scroll", "Scroll", "Scroll speed, direction, acceleration and wheel remapping.")
    {
        WheelRows = new[] { InputSource.WheelUp, InputSource.WheelDown, InputSource.WheelLeft, InputSource.WheelRight }
            .Select(s => new MappingRowViewModel(controller, this, s)).ToList();
    }

    public IReadOnlyList<MappingRowViewModel> WheelRows { get; }

    // ------------------------------------------------------------------ Windows scroll amounts

    public bool OverrideWindowsScroll
    {
        get => Config.Scroll.OverrideWindowsScroll;
        set => Edit(c => c.Scroll.OverrideWindowsScroll = value);
    }

    public double LinesPerNotch
    {
        get => Config.Scroll.OverrideWindowsScroll ? Config.Scroll.LinesPerNotch : Math.Max(1, WindowsMouseSettings.ReadBaseline().ScrollLines);
        set => Edit(c => c.Scroll.LinesPerNotch = ConfigSanitizer.ScrollLines.Clamp((int)Math.Round(value)));
    }

    public bool PageScroll
    {
        get => Config.Scroll.PageScroll;
        set => Edit(c => c.Scroll.PageScroll = value);
    }

    public double HorizontalChars
    {
        get => Config.Scroll.OverrideWindowsScroll ? Config.Scroll.HorizontalChars : Math.Max(1, WindowsMouseSettings.ReadBaseline().ScrollChars);
        set => Edit(c => c.Scroll.HorizontalChars = ConfigSanitizer.ScrollChars.Clamp((int)Math.Round(value)));
    }

    public string WindowsScrollNote => Config.Scroll.OverrideWindowsScroll
        ? "Applied to Windows while this config is active; your own setting returns when Synapse is disabled."
        : $"Using your Windows setting ({DescribeBaseline()}).";

    // ------------------------------------------------------------------ software processing

    public double Sensitivity
    {
        get => Config.Scroll.Sensitivity;
        set => Edit(c => c.Scroll.Sensitivity = ConfigSanitizer.ScrollSensitivity.Clamp(Math.Round(value, 2)));
    }

    public bool FineGrained
    {
        get => Config.Scroll.FineGrained;
        set => Edit(c => c.Scroll.FineGrained = value);
    }

    public bool ReverseVertical
    {
        get => Config.Scroll.ReverseVertical;
        set => Edit(c => c.Scroll.ReverseVertical = value);
    }

    public bool ReverseHorizontal
    {
        get => Config.Scroll.ReverseHorizontal;
        set => Edit(c => c.Scroll.ReverseHorizontal = value);
    }

    public IReadOnlyList<Option<HorizontalScrollModifier>> ModifierOptions { get; } = new[]
    {
        new Option<HorizontalScrollModifier>("Off", HorizontalScrollModifier.None),
        new Option<HorizontalScrollModifier>("Hold Shift", HorizontalScrollModifier.Shift),
        new Option<HorizontalScrollModifier>("Hold Ctrl", HorizontalScrollModifier.Ctrl),
        new Option<HorizontalScrollModifier>("Hold Alt", HorizontalScrollModifier.Alt),
    };

    public HorizontalScrollModifier HorizontalModifier
    {
        get => Config.Scroll.HorizontalModifier;
        set => Edit(c => c.Scroll.HorizontalModifier = value);
    }

    public bool AccelerationEnabled
    {
        get => Config.Scroll.AccelerationEnabled;
        set => Edit(c => c.Scroll.AccelerationEnabled = value);
    }

    public double AccelerationStrength
    {
        get => Config.Scroll.AccelerationStrength;
        set => Edit(c => c.Scroll.AccelerationStrength = ConfigSanitizer.AccelStrength.Clamp((int)Math.Round(value)));
    }

    public double AccelerationMax
    {
        get => Config.Scroll.AccelerationMax;
        set => Edit(c => c.Scroll.AccelerationMax = ConfigSanitizer.AccelMax.Clamp(Math.Round(value, 1)));
    }

    protected override void OnConfigReplaced()
    {
        foreach (var row in WheelRows)
        {
            row.Refresh();
        }

        RefreshAll();
    }

    protected override void OnExternalEdit() => OnConfigReplaced();

    protected override void OnEdited()
    {
        OnPropertyChanged(nameof(LinesPerNotch));
        OnPropertyChanged(nameof(HorizontalChars));
        OnPropertyChanged(nameof(WindowsScrollNote));
    }

    private static string DescribeBaseline()
    {
        var b = WindowsMouseSettings.ReadBaseline();
        string lines = b.ScrollLines < 0 ? "one screen per notch" : $"{b.ScrollLines} lines per notch";
        return $"{lines}, {b.ScrollChars} characters horizontally";
    }
}
