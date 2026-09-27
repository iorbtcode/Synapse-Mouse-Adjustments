using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SynapseMouse.App.Infrastructure;
using SynapseMouse.App.Services;
using SynapseMouse.Core.Config;
using SynapseMouse.Core.Input;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.ViewModels;

/// <summary>One remappable input (button or wheel direction) of the active config.</summary>
internal sealed class MappingRowViewModel : ViewModelBase
{
    private readonly AppController _controller;
    private readonly object _owner;

    public MappingRowViewModel(AppController controller, object owner, InputSource source)
    {
        _controller = controller;
        _owner = owner;
        Source = source;
        var actions = source.IsWheel() ? ActionCatalog.WheelActions : ActionCatalog.ButtonActions;
        Actions = actions.Select(a => new Option<ActionType>(ActionCatalog.DisplayName(a), a)).ToList();
    }

    public InputSource Source { get; }

    public string SourceName => Source.DisplayName();

    public IReadOnlyList<Option<ActionType>> Actions { get; }

    private ButtonMapping Mapping => _controller.ActiveConfig.Buttons.GetMapping(Source);

    public ActionType Action
    {
        get => _pendingKeyboard ? ActionType.Keyboard : Mapping.Action;
        set
        {
            var previous = Mapping;
            if (value != ActionType.Keyboard)
            {
                _pendingKeyboard = false;
                PendingKey = null;
            }

            if (previous.Action == value)
            {
                RefreshAll();
                return;
            }

            if (value == ActionType.Keyboard)
            {
                // Wait for a key before the mapping takes effect.
                _pendingKeyboard = true;
                RefreshAll();
                return;
            }

            Apply(value, null);
            if (Source == InputSource.LeftButton && value != ActionType.Default && value != ActionType.LeftClick)
            {
                ConfirmPrimaryChange(previous);
            }
        }
    }

    private bool _pendingKeyboard;

    private KeyChord? PendingKey { get; set; }

    public bool IsKeyboard => _pendingKeyboard || Mapping.Action == ActionType.Keyboard;

    public KeyChord? Key
    {
        get => Mapping.Action == ActionType.Keyboard ? Mapping.Key : PendingKey;
        set
        {
            if (value is null || !value.IsValid)
            {
                if (Mapping.Action == ActionType.Keyboard)
                {
                    Apply(ActionType.Default, null);
                }

                _pendingKeyboard = false;
                RefreshAll();
                return;
            }

            _pendingKeyboard = false;
            PendingKey = value;
            var previous = Mapping;
            Apply(ActionType.Keyboard, value);
            if (Source == InputSource.LeftButton)
            {
                ConfirmPrimaryChange(previous);
            }
        }
    }

    public bool IsRemapped => Mapping.Action != ActionType.Default;

    public string Summary => _pendingKeyboard
        ? "Press the key to assign →"
        : Mapping.Action == ActionType.Default ? "Unchanged" : ActionCatalog.Describe(Mapping.Action, Mapping.Key);

    public void Refresh()
    {
        _pendingKeyboard = false;
        PendingKey = null;
        RefreshAll();
    }

    private void Apply(ActionType action, KeyChord? key)
    {
        _controller.ActiveConfig.Buttons.SetMapping(Source, action, key);
        _controller.OnConfigEdited(_owner);
        RefreshAll();
    }

    /// <summary>
    /// Remapping the primary button can make the mouse unable to click. The change is kept only if the
    /// user confirms (Enter works even when clicking does not); otherwise it reverts automatically.
    /// </summary>
    private void ConfirmPrimaryChange(ButtonMapping previous)
    {
        var dispatcher = Application.Current.Dispatcher;
        dispatcher.BeginInvoke(() =>
        {
            bool keep = DialogService.ConfirmWithTimeout(
                "Keep the new left-button mapping?",
                "The left button no longer produces a left click. If you can't click, press Enter to keep the change, " +
                $"or wait and it will be undone. {AppInfo.EmergencyHotkeyText} always turns Master Enable off.",
                15);
            if (!keep)
            {
                Apply(previous.Action, previous.Key);
            }
        });
    }
}

/// <summary>A keyboard shortcut acting as an extra mouse button.</summary>
internal sealed class VirtualButtonViewModel : ViewModelBase
{
    private readonly ButtonsViewModel _owner;

    public VirtualButtonViewModel(ButtonsViewModel owner, VirtualButton model)
    {
        _owner = owner;
        Model = model;
        RemoveCommand = new RelayCommand(() => owner.Remove(this));
    }

    public VirtualButton Model { get; }

    public ICommand RemoveCommand { get; }

    public IReadOnlyList<Option<ActionType>> Actions { get; } =
        ActionCatalog.VirtualButtonActions.Select(a => new Option<ActionType>(ActionCatalog.DisplayName(a), a)).ToList();

    public string Name
    {
        get => Model.Name;
        set
        {
            Model.Name = ConfigSanitizer.CleanName(value, "Virtual button");
            _owner.CommitVirtual();
        }
    }

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            Model.Enabled = value;
            _owner.CommitVirtual();
            OnPropertyChanged();
        }
    }

    public KeyChord? Trigger
    {
        get => Model.Trigger;
        set
        {
            var chord = ConfigSanitizer.CleanTrigger(value);
            Model.Trigger = InputSafety.IsReserved(chord) ? null : chord;
            _owner.CommitVirtual();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Warning));
        }
    }

    public ActionType Action
    {
        get => Model.Action;
        set
        {
            Model.Action = value;
            if (value != ActionType.Keyboard)
            {
                Model.Key = null;
            }

            _owner.CommitVirtual();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsKeyboard));
        }
    }

    public bool IsKeyboard => Model.Action == ActionType.Keyboard;

    public KeyChord? Key
    {
        get => Model.Key;
        set
        {
            Model.Key = ConfigSanitizer.CleanTarget(value);
            _owner.CommitVirtual();
            OnPropertyChanged();
        }
    }

    public string Warning
    {
        get
        {
            if (Model.Trigger is null)
            {
                return "Choose a shortcut to activate this button.";
            }

            if (Model.Trigger.Modifiers == KeyModifiers.None && Model.Trigger.Key is (>= 0x30 and <= 0x5A) or 0x20 or 0x0D)
            {
                return "This key will stop typing normally while Synapse is enabled. Consider F13–F24 or a combination.";
            }

            return string.Empty;
        }
    }
}

internal sealed class ButtonsViewModel : ConfigPageViewModel
{
    public ButtonsViewModel(AppController controller)
        : base(controller, "buttons", "Buttons", "Remap mouse buttons and the scroll wheel to other mouse buttons, keys or Synapse functions. Changes apply instantly.")
    {
        ButtonRows = new[] { InputSource.LeftButton, InputSource.RightButton, InputSource.MiddleButton, InputSource.XButton1, InputSource.XButton2 }
            .Select(s => new MappingRowViewModel(controller, this, s)).ToList();
        WheelRows = new[] { InputSource.WheelUp, InputSource.WheelDown, InputSource.WheelLeft, InputSource.WheelRight }
            .Select(s => new MappingRowViewModel(controller, this, s)).ToList();
        AddVirtualButtonCommand = new RelayCommand(AddVirtualButton, () => VirtualButtons.Count < ConfigSanitizer.MaxVirtualButtons);
        ResetMappingsCommand = new RelayCommand(ResetMappings);
        controller.DevicesChanged += (_, _) => OnPropertyChanged(nameof(HardwareNote));
        LoadVirtualButtons();
    }

    public IReadOnlyList<MappingRowViewModel> ButtonRows { get; }

    public IReadOnlyList<MappingRowViewModel> WheelRows { get; }

    public ObservableCollection<VirtualButtonViewModel> VirtualButtons { get; } = new();

    public ICommand AddVirtualButtonCommand { get; }

    public ICommand ResetMappingsCommand { get; }

    public string HardwareNote => Controller.Devices.Primary?.ButtonCount is int n
        ? $"Windows reports {n} buttons on your mouse. Mappings for buttons it doesn't have simply never trigger — use Virtual buttons below instead."
        : "Mappings for buttons your mouse doesn't have simply never trigger — use Virtual buttons below instead.";

    public string EmergencyHotkey => AppInfo.EmergencyHotkeyText;

    public void Remove(VirtualButtonViewModel vb)
    {
        Config.Buttons.VirtualButtons.Remove(vb.Model);
        VirtualButtons.Remove(vb);
        CommitVirtual();
    }

    public void CommitVirtual() => Controller.OnConfigEdited(this);

    protected override void OnConfigReplaced()
    {
        foreach (var row in ButtonRows.Concat(WheelRows))
        {
            row.Refresh();
        }

        LoadVirtualButtons();
        RefreshAll();
    }

    protected override void OnExternalEdit() => OnConfigReplaced();

    private void LoadVirtualButtons()
    {
        VirtualButtons.Clear();
        foreach (var vb in Config.Buttons.VirtualButtons)
        {
            VirtualButtons.Add(new VirtualButtonViewModel(this, vb));
        }
    }

    private void AddVirtualButton()
    {
        int n = Config.Buttons.VirtualButtons.Count;
        var model = new VirtualButton
        {
            Name = n == 0 ? "Back" : n == 1 ? "Forward" : $"Virtual button {n + 1}",
            Action = n == 1 ? ActionType.ForwardButton : ActionType.BackButton,
        };
        Config.Buttons.VirtualButtons.Add(model);
        VirtualButtons.Add(new VirtualButtonViewModel(this, model));
        CommitVirtual();
    }

    private void ResetMappings()
    {
        if (!DialogService.Confirm("Reset button mappings?", $"All buttons and wheel directions in \"{Config.Name}\" go back to their default behavior. Virtual buttons are kept.", "Reset"))
        {
            return;
        }

        Config.Buttons.Mappings.Clear();
        Controller.OnConfigEdited(this);
        OnConfigReplaced();
    }
}
