using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SynapseMouse.App.Infrastructure;
using SynapseMouse.App.Services;
using SynapseMouse.Core.Config;
using SynapseMouse.Core.Input;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.ViewModels;

internal sealed class StageChip : ViewModelBase
{
    public StageChip(int index, int dpi, bool enabled, bool active, ICommand select)
    {
        Index = index;
        Label = dpi.ToString(CultureInfo.InvariantCulture);
        IsEnabled = enabled;
        IsActive = active;
        SelectCommand = select;
    }

    public int Index { get; }

    public string Label { get; }

    public bool IsEnabled { get; }

    public bool IsActive { get; }

    public ICommand SelectCommand { get; }
}

internal sealed class DashboardViewModel : ConfigPageViewModel
{
    private readonly MainViewModel _main;

    public DashboardViewModel(AppController controller, MainViewModel main)
        : base(controller, "dashboard", "Dashboard", "Everything Synapse is doing right now.")
    {
        _main = main;
        SelectStageCommand = new RelayCommand(p =>
        {
            if (p is int index)
            {
                Controller.SetActiveStage(index, this);
                OnEdited();
            }
        });
        OpenPageCommand = new RelayCommand(p => _main.Navigate(p as string ?? "dashboard"));
        controller.StateChanged += (_, _) => RefreshAll();
        controller.ConfigListChanged += (_, _) => RefreshConfigs();
        controller.DevicesChanged += (_, _) => RefreshAll();
        RefreshConfigs();
    }

    public ICommand SelectStageCommand { get; }

    public ICommand OpenPageCommand { get; }

    public ObservableCollection<Option<Guid>> ConfigOptions { get; } = new();

    // ------------------------------------------------------------------ master / status tiles

    public bool MasterEnabled
    {
        get => Controller.MasterEnabled;
        set
        {
            Controller.SetMaster(value);
            RefreshAll();
        }
    }

    public string MasterText => Controller.MasterEnabled ? "● ENABLED" : "○ DISABLED";

    public string MasterDescription => Controller.MasterEnabled
        ? "All enabled Synapse settings are active across Windows."
        : "Synapse is not modifying the mouse. Windows behaves normally; the app keeps running in the background.";

    public string BackgroundStatus => Controller.Engine.IsRunning ? "● RUNNING" : "○ STOPPED";

    public string BackgroundDetail => Controller.EngineStatusText;

    public string CurrentConfigName => Config.Name;

    public string AutoSwitchNote => Controller.AutoSelected ? "Selected automatically for the active app" : string.Empty;

    public IReadOnlyList<string> ActiveFeatures => FeatureSummary.GetActiveFeatures(Config);

    public string ActiveFeatureText
    {
        get
        {
            if (!Controller.MasterEnabled)
            {
                return "0 Active";
            }

            int count = ActiveFeatures.Count;
            return count == 1 ? "1 Enabled" : $"{count} Enabled";
        }
    }

    public string FeaturesHint => ActiveFeatures.Count == 0
        ? "This config changes nothing — mouse input is exactly as Windows provides it."
        : Controller.MasterEnabled ? string.Empty : "Paused by Master Enable.";

    public string MouseStatus
    {
        get
        {
            var devices = Controller.Devices;
            if (!devices.HasEnumerated)
            {
                return "Detecting…";
            }

            return devices.AnyPhysicalMouse ? "Connected" : "Not detected";
        }
    }

    public string MouseName => Controller.Devices.Primary?.DisplayName ?? "No mouse reported by Windows";

    public string EmergencyHotkey => AppInfo.EmergencyHotkeyText;

    // ------------------------------------------------------------------ quick controls

    public Guid SelectedConfigId
    {
        get => Config.Id;
        set
        {
            if (value != Guid.Empty && value != Config.Id)
            {
                Controller.SwitchConfig(value, SwitchReason.Manual);
            }
        }
    }

    public bool DpiStagesEnabled
    {
        get => Config.Sensitivity.DpiStagesEnabled;
        set => Edit(c => c.Sensitivity.DpiStagesEnabled = value);
    }

    public IReadOnlyList<StageChip> Stages
    {
        get
        {
            var s = Config.Sensitivity;
            return s.Stages.Select((stage, i) => new StageChip(i, stage.Dpi, stage.Enabled, i == s.ActiveStage, SelectStageCommand)).ToList();
        }
    }

    public string CurrentDpiText => Config.Sensitivity.DpiStagesEnabled
        ? $"{PointerMath.EffectiveDpi(Config.Sensitivity)} DPI (software)"
        : $"{Config.Sensitivity.NativeDpi} DPI (hardware, unchanged)";

    public bool DebounceEnabled
    {
        get => Config.Debounce.Enabled;
        set => Edit(c => c.Debounce.Enabled = value);
    }

    public double DebounceMs
    {
        get => Config.Debounce.TimeMs;
        set => Edit(c => c.Debounce.TimeMs = (int)Math.Round(value));
    }

    public bool DoubleClickEnabled
    {
        get => Config.DoubleClick.Enabled;
        set => Edit(c => c.DoubleClick.Enabled = value);
    }

    public double DoubleClickInterval
    {
        get => Config.DoubleClick.IntervalMs;
        set => Edit(c =>
        {
            c.DoubleClick.IntervalMs = (int)Math.Round(value);
            ConfigSanitizer.Sanitize(c);
        });
    }

    protected override void OnEdited()
    {
        OnPropertyChanged(nameof(ActiveFeatures));
        OnPropertyChanged(nameof(ActiveFeatureText));
        OnPropertyChanged(nameof(FeaturesHint));
        OnPropertyChanged(nameof(Stages));
        OnPropertyChanged(nameof(CurrentDpiText));
        OnPropertyChanged(nameof(BackgroundDetail));
    }

    private void RefreshConfigs()
    {
        ConfigOptions.Clear();
        foreach (var c in Controller.Configs.Configs)
        {
            ConfigOptions.Add(new Option<Guid>(c.Name, c.Id));
        }

        OnPropertyChanged(nameof(SelectedConfigId));
    }
}
