using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SynapseMouse.App.Infrastructure;
using SynapseMouse.App.Services;
using SynapseMouse.Core.Config;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.ViewModels;

internal sealed class ConfigItemViewModel : ViewModelBase
{
    private readonly ConfigsViewModel _owner;

    public ConfigItemViewModel(ConfigsViewModel owner, MouseConfig model)
    {
        _owner = owner;
        Model = model;
    }

    public MouseConfig Model { get; }

    public Guid Id => Model.Id;

    public string Name => Model.Name;

    public string Preset => "Preset: " + Model.Preset;

    public bool IsActive => _owner.ActiveId == Model.Id;

    public bool IsDefault => _owner.DefaultId == Model.Id;

    public string Badges => string.Join("  ·  ", new[] { IsActive ? "ACTIVE" : null, IsDefault ? "DEFAULT" : null }.Where(b => b is not null));

    public string Summary
    {
        get
        {
            var features = FeatureSummary.GetActiveFeatures(Model);
            return features.Count == 0 ? "No modifications" : string.Join(" · ", features);
        }
    }

    public KeyChord? Hotkey
    {
        get => Model.Hotkey;
        set => _owner.SetHotkey(this, value);
    }

    public bool HotkeyFailed => _owner.HotkeyFailed(Model.Id);

    public void Refresh() => RefreshAll();
}

internal sealed class AssociationViewModel : ViewModelBase
{
    private readonly ConfigsViewModel _owner;

    public AssociationViewModel(ConfigsViewModel owner, AppAssociation model)
    {
        _owner = owner;
        Model = model;
        RemoveCommand = new RelayCommand(() => owner.RemoveAssociation(this));
    }

    public AppAssociation Model { get; }

    public ICommand RemoveCommand { get; }

    public ObservableCollection<Option<Guid>> ConfigOptions => _owner.ConfigOptions;

    public string ProcessName
    {
        get => Model.ProcessName;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Model.ProcessName = AutoSwitchLogic.NormalizeProcessName(value);
                _owner.CommitAssociations();
            }

            OnPropertyChanged();
        }
    }

    public string WindowTitleContains
    {
        get => Model.WindowTitleContains ?? string.Empty;
        set
        {
            Model.WindowTitleContains = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _owner.CommitAssociations();
            OnPropertyChanged();
        }
    }

    public Guid ConfigId
    {
        get => Model.ConfigId;
        set
        {
            if (value == Guid.Empty)
            {
                return;
            }

            Model.ConfigId = value;
            _owner.CommitAssociations();
            OnPropertyChanged();
        }
    }

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            Model.Enabled = value;
            _owner.CommitAssociations();
            OnPropertyChanged();
        }
    }
}

internal sealed class ConfigsViewModel : PageViewModel
{
    private ConfigItemViewModel? _selected;
    private string _foregroundText = "—";

    public ConfigsViewModel(AppController controller)
        : base(controller, "configs", "Configs", "Complete saved mouse setups. Switch instantly, export to share, and link configs to applications.")
    {
        ActivateCommand = new RelayCommand(p => Activate(p as ConfigItemViewModel ?? Selected));
        NewCommand = new RelayCommand(CreateConfig);
        DuplicateCommand = new RelayCommand(() => Run(() => Controller.Configs.Duplicate(Selected!.Id)), () => Selected is not null);
        RenameCommand = new RelayCommand(Rename, () => Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null && Controller.Configs.Configs.Count > 1);
        ResetCommand = new RelayCommand(Reset, () => Selected is not null);
        SetDefaultCommand = new RelayCommand(() => Controller.Configs.SetDefault(Selected!.Id), () => Selected is not null);
        MoveUpCommand = new RelayCommand(() => Controller.Configs.MoveConfig(Selected!.Id, -1), () => Selected is not null);
        MoveDownCommand = new RelayCommand(() => Controller.Configs.MoveConfig(Selected!.Id, +1), () => Selected is not null);
        SaveCommand = new RelayCommand(Save);
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(Export, () => Selected is not null);
        AddAssociationCommand = new RelayCommand(AddAssociation);
        AddMinecraftCommand = new RelayCommand(AddMinecraft);

        controller.ConfigListChanged += (_, _) => Rebuild();
        controller.ActiveConfigChanged += (_, _) => RefreshItems();
        controller.ConfigEdited += (_, _) => RefreshItems();
        controller.HotkeysChanged += (_, _) => RefreshItems();
        controller.AppSettingsChanged += (sender, _) =>
        {
            if (!ReferenceEquals(sender, this))
            {
                Rebuild();
            }

            OnPropertyChanged(nameof(AutoSwitchEnabled));
            OnPropertyChanged(nameof(Fallback));
        };
        controller.Saver.Saved += (_, _) => OnPropertyChanged(nameof(SaveStatus));
        controller.ForegroundChanged += (_, info) =>
        {
            _foregroundText = info.IsOwnProcess ? "Synapse (ignored)" : $"{info.ProcessName ?? "?"} — {info.Title ?? "(no title)"}";
            OnPropertyChanged(nameof(ForegroundText));
        };
        Rebuild();
    }

    public ObservableCollection<ConfigItemViewModel> Configs { get; } = new();

    public ObservableCollection<AssociationViewModel> Associations { get; } = new();

    public ObservableCollection<Option<Guid>> ConfigOptions { get; } = new();

    public IReadOnlyList<string> PresetOptions { get; } = ConfigPresets.All;

    public string NewPreset { get; set; } = ConfigPresets.Custom;

    public Guid ActiveId => Controller.ActiveConfig.Id;

    public Guid DefaultId => Controller.Configs.Document.DefaultConfigId;

    public ConfigItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public ICommand ActivateCommand { get; }

    public ICommand NewCommand { get; }

    public ICommand DuplicateCommand { get; }

    public ICommand RenameCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand SetDefaultCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand AddAssociationCommand { get; }

    public ICommand AddMinecraftCommand { get; }

    public string SaveStatus => Controller.Saver.LastSaved is { } t
        ? "All changes are saved automatically · last saved " + t.ToString("T", CultureInfo.CurrentCulture)
        : "All changes are saved automatically.";

    public bool AutoSwitchEnabled
    {
        get => Controller.App.AutoSwitchEnabled;
        set
        {
            Controller.App.AutoSwitchEnabled = value;
            Controller.OnAppSettingsEdited(this);
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<Option<AutoSwitchFallback>> FallbackOptions { get; } = new[]
    {
        new Option<AutoSwitchFallback>("Return to the previous config", AutoSwitchFallback.ReturnToPrevious),
        new Option<AutoSwitchFallback>("Switch to the default config", AutoSwitchFallback.ReturnToDefault),
        new Option<AutoSwitchFallback>("Stay on the app's config", AutoSwitchFallback.KeepCurrent),
    };

    public AutoSwitchFallback Fallback
    {
        get => Controller.App.AutoSwitchFallback;
        set
        {
            Controller.App.AutoSwitchFallback = value;
            Controller.OnAppSettingsEdited(this);
        }
    }

    public string ForegroundText => _foregroundText;

    public bool HotkeyFailed(Guid id) => Controller.Hotkeys.Failed.Contains("config:" + id.ToString("N"));

    public void SetHotkey(ConfigItemViewModel item, KeyChord? chord)
    {
        var clean = ConfigSanitizer.CleanTrigger(chord);
        if (Core.Input.InputSafety.IsReserved(clean))
        {
            clean = null;
        }

        if (clean is not null)
        {
            // A hotkey can belong to one config only.
            foreach (var other in Controller.Configs.Configs.Where(c => c.Id != item.Id && clean.Equals(c.Hotkey)))
            {
                other.Hotkey = null;
            }
        }

        item.Model.Hotkey = clean;
        Controller.Configs.NotifyEdited(item.Model);
        Controller.RegisterHotkeys();
        RefreshItems();
    }

    public void CommitAssociations() => Controller.OnAppSettingsEdited(this);

    public void RemoveAssociation(AssociationViewModel vm)
    {
        Controller.App.AppAssociations.Remove(vm.Model);
        Associations.Remove(vm);
        CommitAssociations();
    }

    private void Rebuild()
    {
        Guid? selectedId = Selected?.Id;
        Configs.Clear();
        foreach (var config in Controller.Configs.Configs)
        {
            Configs.Add(new ConfigItemViewModel(this, config));
        }

        Selected = Configs.FirstOrDefault(c => c.Id == selectedId) ?? Configs.FirstOrDefault(c => c.IsActive);

        ConfigOptions.Clear();
        foreach (var config in Controller.Configs.Configs)
        {
            ConfigOptions.Add(new Option<Guid>(config.Name, config.Id));
        }

        Associations.Clear();
        foreach (var association in Controller.App.AppAssociations)
        {
            Associations.Add(new AssociationViewModel(this, association));
        }

        OnPropertyChanged(nameof(SaveStatus));
    }

    private void RefreshItems()
    {
        // Configs may have been replaced (reset): rebuild if the instances differ.
        if (Configs.Count != Controller.Configs.Configs.Count
            || Configs.Zip(Controller.Configs.Configs).Any(p => !ReferenceEquals(p.First.Model, p.Second)))
        {
            Rebuild();
            return;
        }

        foreach (var item in Configs)
        {
            item.Refresh();
        }
    }

    private void Activate(ConfigItemViewModel? item)
    {
        if (item is not null)
        {
            Controller.SwitchConfig(item.Id, SwitchReason.Manual);
        }
    }

    private void CreateConfig()
    {
        string? name = DialogService.Prompt("New config", $"Name for the new config (starts from the \"{NewPreset}\" preset).", NewPreset == ConfigPresets.Custom ? "My config" : NewPreset, "Create");
        if (name is null)
        {
            return;
        }

        Run(() =>
        {
            var config = Controller.Configs.Create(name, NewPreset);
            Selected = Configs.FirstOrDefault(c => c.Id == config.Id);
        });
    }

    private void Rename()
    {
        var item = Selected;
        if (item is null)
        {
            return;
        }

        string? name = DialogService.Prompt("Rename config", string.Empty, item.Name, "Rename");
        if (name is not null)
        {
            Run(() => Controller.Configs.Rename(item.Id, name));
        }
    }

    private void Delete()
    {
        var item = Selected;
        if (item is null)
        {
            return;
        }

        if (DialogService.Confirm("Delete config?", $"\"{item.Name}\" will be removed permanently, including its app associations.", "Delete", danger: true))
        {
            Run(() => Controller.Configs.Delete(item.Id));
        }
    }

    private void Reset()
    {
        var item = Selected;
        if (item is null)
        {
            return;
        }

        if (DialogService.Confirm("Reset config?", $"\"{item.Name}\" returns to the \"{item.Model.Preset}\" preset values. Its name and hotkey are kept.", "Reset", danger: true))
        {
            Run(() => Controller.Configs.Reset(item.Id));
        }
    }

    private void Save()
    {
        if (Controller.Saver.Flush(force: true))
        {
            OnPropertyChanged(nameof(SaveStatus));
        }
        else
        {
            DialogService.Info("Could not save", "Your settings could not be written to disk. Check the storage location on the Settings page.");
        }
    }

    private void Import()
    {
        string? path = DialogService.OpenFile("Import config", "Synapse config (*.synapseconfig)|*.synapseconfig|All files (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        try
        {
            if (new FileInfo(path).Length > ConfigSerializer.MaxFileBytes)
            {
                throw new ConfigFormatException("The file is too large to be a Synapse config.");
            }

            var config = Controller.Configs.Import(File.ReadAllText(path));
            Selected = Configs.FirstOrDefault(c => c.Id == config.Id);
            DialogService.Info("Config imported", $"\"{config.Name}\" was added. Select it and press Activate to use it.");
        }
        catch (Exception ex) when (ex is ConfigFormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn("Config import failed.", ex);
            DialogService.Info("Import failed", ex.Message);
        }
    }

    private void Export()
    {
        var item = Selected;
        if (item is null)
        {
            return;
        }

        string fileName = new string(item.Name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch) && ch != ' ').ToArray());
        string? path = DialogService.SaveFile("Export config", "Synapse config (*.synapseconfig)|*.synapseconfig",
            (fileName.Length == 0 ? "Config" : fileName) + ConfigSerializer.ConfigExtension);
        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, Controller.Configs.Export(item.Id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DialogService.Info("Export failed", ex.Message);
        }
    }

    private void AddAssociation()
    {
        var pick = DialogService.PickApplication(ForegroundWatcher.ListWindowedApps());
        if (pick is null)
        {
            return;
        }

        AddAssociation(pick.Value.ProcessName, null);
    }

    private void AddMinecraft()
    {
        // Minecraft Java Edition runs in javaw.exe; the title filter avoids matching other Java apps.
        AddAssociation("javaw.exe", "Minecraft");
    }

    private void AddAssociation(string processName, string? title)
    {
        if (Controller.App.AppAssociations.Count >= ConfigSanitizer.MaxAssociations)
        {
            return;
        }

        var target = Controller.Configs.FindByName(ConfigPresets.MinecraftPvp) is { } pvp && title == "Minecraft"
            ? pvp
            : Controller.ActiveConfig;
        var association = new AppAssociation
        {
            ProcessName = AutoSwitchLogic.NormalizeProcessName(processName),
            WindowTitleContains = title,
            ConfigId = target.Id,
        };
        Controller.App.AppAssociations.Add(association);
        Associations.Add(new AssociationViewModel(this, association));
        if (!Controller.App.AutoSwitchEnabled)
        {
            Controller.App.AutoSwitchEnabled = true;
            OnPropertyChanged(nameof(AutoSwitchEnabled));
        }

        CommitAssociations();
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            DialogService.Info("Not possible", ex.Message);
        }
    }
}
