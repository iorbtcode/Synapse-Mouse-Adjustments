using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SynapseMouse.App.Services;
using SynapseMouse.Core.Config;
using SynapseMouse.Core.Input;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.ViewModels;

internal sealed class DpiStageViewModel : ViewModelBase
{
    private readonly DpiViewModel _owner;

    public DpiStageViewModel(DpiViewModel owner, int index)
    {
        _owner = owner;
        Index = index;
        SelectCommand = new RelayCommand(() => owner.SelectStage(Index), () => Enabled);
    }

    public int Index { get; }

    public string Label => $"Stage {Index + 1}";

    public ICommand SelectCommand { get; }

    private DpiStage Stage => _owner.StageModel(Index);

    public double Dpi
    {
        get => Stage.Dpi;
        set => _owner.EditStage(Index, s => s.Dpi = ConfigSanitizer.StageDpi.Clamp((int)Math.Round(value / 50.0) * 50));
    }

    public bool Enabled
    {
        get => Stage.Enabled;
        set => _owner.EditStage(Index, s => s.Enabled = value || _owner.ActiveStageIndex == Index);
    }

    public bool IsActive => _owner.ActiveStageIndex == Index;

    public void Refresh() => RefreshAll();
}

internal sealed class DpiViewModel : ConfigPageViewModel
{
    public DpiViewModel(AppController controller)
        : base(controller, "dpi", "DPI & Sensitivity", "Software DPI stages, per-axis sensitivity and Windows pointer settings — saved separately in every config.")
    {
        controller.AppSettingsChanged += (_, _) => RefreshAll();
        controller.HotkeysChanged += (_, _) => OnPropertyChanged(nameof(HotkeyStatus));
        BuildStages();
    }

    public ObservableCollection<DpiStageViewModel> Stages { get; } = new();

    public int ActiveStageIndex => Config.Sensitivity.ActiveStage;

    public DpiStage StageModel(int index) => Config.Sensitivity.Stages[Math.Clamp(index, 0, Config.Sensitivity.Stages.Count - 1)];

    public void EditStage(int index, Action<DpiStage> change)
    {
        change(StageModel(index));
        Controller.OnConfigEdited(this);
        OnEdited();
    }

    public void SelectStage(int index)
    {
        Controller.SetActiveStage(index, this);
        OnEdited();
    }

    // ------------------------------------------------------------------ software DPI

    public double NativeDpi
    {
        get => Config.Sensitivity.NativeDpi;
        set => Edit(c => c.Sensitivity.NativeDpi = ConfigSanitizer.NativeDpi.Clamp((int)Math.Round(value / 50.0) * 50));
    }

    public bool DpiStagesEnabled
    {
        get => Config.Sensitivity.DpiStagesEnabled;
        set => Edit(c => c.Sensitivity.DpiStagesEnabled = value);
    }

    public string EffectiveDpiText
    {
        get
        {
            var s = Config.Sensitivity;
            var (x, y) = PointerMath.MovementMultipliers(s);
            string axis = PointerMath.IsUnity(x - y + 1) ? string.Format(CultureInfo.CurrentCulture, "{0:0.00}×", x)
                : string.Format(CultureInfo.CurrentCulture, "X {0:0.00}× · Y {1:0.00}×", x, y);
            return string.Format(CultureInfo.CurrentCulture, "{0} DPI effective · {1} of hardware movement", PointerMath.EffectiveDpi(s), axis);
        }
    }

    public string CurrentStageText => Config.Sensitivity.DpiStagesEnabled
        ? $"Stage {Config.Sensitivity.ActiveStage + 1}: {StageModel(Config.Sensitivity.ActiveStage).Dpi} DPI"
        : "Stages off — hardware DPI unchanged";

    // ------------------------------------------------------------------ sensitivity

    public double XSensitivity
    {
        get => Config.Sensitivity.XSensitivity;
        set => Edit(c =>
        {
            c.Sensitivity.XSensitivity = ConfigSanitizer.Sensitivity.Clamp(Math.Round(value, 2));
            if (c.Sensitivity.LinkXY)
            {
                c.Sensitivity.YSensitivity = c.Sensitivity.XSensitivity;
            }
        });
    }

    public double YSensitivity
    {
        get => PointerMath.EffectiveY(Config.Sensitivity);
        set => Edit(c => c.Sensitivity.YSensitivity = ConfigSanitizer.Sensitivity.Clamp(Math.Round(value, 2)));
    }

    public bool LinkXY
    {
        get => Config.Sensitivity.LinkXY;
        set => Edit(c =>
        {
            c.Sensitivity.LinkXY = value;
            if (value)
            {
                c.Sensitivity.YSensitivity = c.Sensitivity.XSensitivity;
            }
        });
    }

    public bool YEditable => !Config.Sensitivity.LinkXY;

    public double ClutchDpi
    {
        get => Config.Sensitivity.ClutchDpi;
        set => Edit(c => c.Sensitivity.ClutchDpi = ConfigSanitizer.StageDpi.Clamp((int)Math.Round(value / 50.0) * 50));
    }

    public bool ScalingActive => Controller.CurrentSettings.MovementInterception;

    public string ScalingNote => ScalingActive
        ? "Software scaling is active: Synapse re-sends your movement multiplied by the values above. It works in Windows and in games " +
          "that read Raw Input. Pointer acceleration is kept off meanwhile, because Windows would otherwise apply it twice."
        : "All multipliers are 1.00×, so movement is not touched at all (zero processing).";

    // ------------------------------------------------------------------ Windows pointer

    public bool OverridePointer
    {
        get => Config.Sensitivity.OverrideWindowsPointer;
        set => Edit(c => c.Sensitivity.OverrideWindowsPointer = value);
    }

    public double PointerSpeed
    {
        get => Config.Sensitivity.OverrideWindowsPointer ? Config.Sensitivity.PointerSpeed : WindowsMouseSettings.ReadBaseline().PointerSpeed;
        set => Edit(c => c.Sensitivity.PointerSpeed = ConfigSanitizer.PointerSpeed.Clamp((int)Math.Round(value)));
    }

    public string PointerSpeedText => string.Format(CultureInfo.CurrentCulture, "{0}/20 · {1:0.###}× cursor gain",
        (int)PointerSpeed, PointerMath.SpeedMultiplier((int)PointerSpeed));

    public bool EnhancePointerPrecision
    {
        get => Controller.AccelerationForcedOff ? false
            : Config.Sensitivity.OverrideWindowsPointer ? Config.Sensitivity.EnhancePointerPrecision
            : WindowsMouseSettings.ReadBaseline().EnhancePointerPrecision;
        set => Edit(c => c.Sensitivity.EnhancePointerPrecision = value);
    }

    public bool AccelerationEditable => Config.Sensitivity.OverrideWindowsPointer && !Controller.AccelerationForcedOff;

    public string AccelerationNote => Controller.AccelerationForcedOff
        ? "Unavailable while software DPI/sensitivity scaling is active (acceleration would be applied twice)."
        : Config.Sensitivity.OverrideWindowsPointer
            ? "Windows \"Enhance pointer precision\". Most players keep this off for consistent aim."
            : "Turn on \"Override Windows pointer settings\" to change this for this config.";

    // ------------------------------------------------------------------ hotkeys (app-wide)

    public KeyChord? DpiUpHotkey
    {
        get => Controller.App.DpiUpHotkey;
        set
        {
            Controller.App.DpiUpHotkey = ConfigSanitizer.CleanTrigger(value);
            Controller.OnAppSettingsEdited(this);
        }
    }

    public KeyChord? DpiDownHotkey
    {
        get => Controller.App.DpiDownHotkey;
        set
        {
            Controller.App.DpiDownHotkey = ConfigSanitizer.CleanTrigger(value);
            Controller.OnAppSettingsEdited(this);
        }
    }

    public KeyChord? DpiCycleHotkey
    {
        get => Controller.App.DpiCycleHotkey;
        set
        {
            Controller.App.DpiCycleHotkey = ConfigSanitizer.CleanTrigger(value);
            Controller.OnAppSettingsEdited(this);
        }
    }

    public string HotkeyStatus
    {
        get
        {
            var failed = Controller.Hotkeys.Failed.Where(id => id.StartsWith("dpi-", StringComparison.Ordinal)).ToList();
            return failed.Count == 0
                ? "Hotkeys work in every app while Master Enable is on. A DPI button can also be assigned on the Buttons page."
                : "Some DPI hotkeys could not be registered — another app (or another Synapse hotkey) already uses that combination.";
        }
    }

    protected override void OnConfigReplaced()
    {
        BuildStages();
        RefreshAll();
    }

    protected override void OnExternalEdit()
    {
        foreach (var stage in Stages)
        {
            stage.Refresh();
        }

        RefreshAll();
    }

    protected override void OnEdited()
    {
        foreach (var stage in Stages)
        {
            stage.Refresh();
        }

        OnPropertyChanged(nameof(EffectiveDpiText));
        OnPropertyChanged(nameof(CurrentStageText));
        OnPropertyChanged(nameof(ActiveStageIndex));
        OnPropertyChanged(nameof(YSensitivity));
        OnPropertyChanged(nameof(YEditable));
        OnPropertyChanged(nameof(ScalingActive));
        OnPropertyChanged(nameof(ScalingNote));
        OnPropertyChanged(nameof(PointerSpeed));
        OnPropertyChanged(nameof(PointerSpeedText));
        OnPropertyChanged(nameof(EnhancePointerPrecision));
        OnPropertyChanged(nameof(AccelerationEditable));
        OnPropertyChanged(nameof(AccelerationNote));
    }

    private void BuildStages()
    {
        Stages.Clear();
        for (int i = 0; i < Config.Sensitivity.Stages.Count; i++)
        {
            Stages.Add(new DpiStageViewModel(this, i));
        }
    }
}
