using System.Diagnostics;
using System.Windows.Input;
using SynapseMouse.App.Infrastructure;
using SynapseMouse.App.Services;
using SynapseMouse.Core.Config;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.ViewModels;

internal sealed class SettingsViewModel : PageViewModel
{
    public SettingsViewModel(AppController controller)
        : base(controller, "settings", "Settings", "Application behavior, storage, backup and safety.")
    {
        OpenStorageCommand = new RelayCommand(() => OpenFolder(Controller.DataDirectory));
        ChangeStorageCommand = new RelayCommand(ChangeStorage, () => !StoragePaths.IsPortable);
        OpenLogsCommand = new RelayCommand(() => OpenFolder(StoragePaths.LogDirectory));
        ResetAllCommand = new RelayCommand(ResetAll);
        ExportCommand = new RelayCommand(ExportBackup);
        ImportCommand = new RelayCommand(ImportBackup);
        controller.AppSettingsChanged += (sender, _) =>
        {
            if (!ReferenceEquals(sender, this))
            {
                RefreshAll();
            }
        };
        controller.HotkeysChanged += (_, _) => OnPropertyChanged(nameof(MasterHotkeyStatus));
    }

    private AppSettings App => Controller.App;

    public ICommand OpenStorageCommand { get; }

    public ICommand ChangeStorageCommand { get; }

    public ICommand OpenLogsCommand { get; }

    public ICommand ResetAllCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand ImportCommand { get; }

    // ------------------------------------------------------------------ startup

    public bool StartWithWindows
    {
        get => App.StartWithWindows;
        set
        {
            try
            {
                StartupService.SetEnabled(value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log.Warn("Changing the startup entry failed.", ex);
                DialogService.Info("Could not change startup", ex.Message);
            }

            App.StartWithWindows = StartupService.IsEnabled();
            Save();
            OnPropertyChanged(nameof(StartupNote));
        }
    }

    public string StartupNote => App.StartWithWindows && StartupService.IsDisabledInTaskManager()
        ? "Windows reports this startup entry as disabled in Task Manager → Startup apps."
        : App.StartWithWindows
            ? "Starts minimized to the tray at sign-in and restores your last config and Master Enable state."
            : "Synapse does not start automatically.";

    public bool StartMinimized
    {
        get => App.StartMinimized;
        set
        {
            App.StartMinimized = value;
            Save();
        }
    }

    public bool MinimizeToTray
    {
        get => App.MinimizeToTray;
        set
        {
            App.MinimizeToTray = value;
            Save();
        }
    }

    public bool ShowOsd
    {
        get => App.ShowOsd;
        set
        {
            App.ShowOsd = value;
            Save();
        }
    }

    // ------------------------------------------------------------------ master enable behavior

    public IReadOnlyList<Option<MasterStartupBehavior>> StartupOptions { get; } = new[]
    {
        new Option<MasterStartupBehavior>("Restore the last state", MasterStartupBehavior.RestoreLast),
        new Option<MasterStartupBehavior>("Always ON", MasterStartupBehavior.AlwaysOn),
        new Option<MasterStartupBehavior>("Always OFF", MasterStartupBehavior.AlwaysOff),
    };

    public MasterStartupBehavior MasterOnStartup
    {
        get => App.MasterOnStartup;
        set
        {
            App.MasterOnStartup = value;
            Save();
        }
    }

    public IReadOnlyList<Option<MasterScope>> ScopeOptions { get; } = new[]
    {
        new Option<MasterScope>("One switch for all configs", MasterScope.Global),
        new Option<MasterScope>("Each config remembers its own state", MasterScope.PerConfig),
    };

    public MasterScope MasterScope
    {
        get => App.MasterScope;
        set
        {
            if (App.MasterScope == value)
            {
                return;
            }

            // Carry the current state over so switching the mode never flips Master Enable by surprise.
            bool current = Controller.MasterEnabled;
            App.MasterScope = value;
            Controller.Configs.Document.MasterEnabled = current;
            foreach (var config in Controller.Configs.Configs)
            {
                config.MasterEnabled = current;
            }

            Save();
        }
    }

    public KeyChord? MasterToggleHotkey
    {
        get => App.MasterToggleHotkey;
        set
        {
            var chord = ConfigSanitizer.CleanTrigger(value);
            App.MasterToggleHotkey = Core.Input.InputSafety.IsReserved(chord) ? null : chord;
            Save();
        }
    }

    public string MasterHotkeyStatus => Controller.Hotkeys.Failed.Contains("master")
        ? "This shortcut is already used by another app or another Synapse hotkey."
        : "Optional shortcut that toggles Master Enable from anywhere.";

    public string EmergencyHotkey => AppInfo.EmergencyHotkeyText;

    public bool WatchdogEnabled
    {
        get => App.WatchdogEnabled;
        set
        {
            App.WatchdogEnabled = value;
            if (value)
            {
                Watchdog.Launch();
            }
            else
            {
                Watchdog.Stop();
            }

            Save();
        }
    }

    public bool ShowTrayNotifications
    {
        get => App.ShowTrayNotifications;
        set
        {
            App.ShowTrayNotifications = value;
            Save();
        }
    }

    // ------------------------------------------------------------------ storage

    public string StorageLocation => Controller.DataDirectory;

    public string StorageNote => StoragePaths.IsPortable
        ? "Portable mode (portable.txt next to the app): settings are stored next to the executable."
        : "settings.json is saved automatically after every change, with a backup copy (settings.json.bak).";

    // ------------------------------------------------------------------ about

    public string Version => AppInfo.Version;

    public string AboutText =>
        "Synapse Mouse Adjustments customizes any mouse at the Windows software level: button remapping, software debounce, " +
        "click timing, software DPI stages, scrolling and per-app configs. It does not modify mouse firmware or hardware, " +
        "uses no kernel driver and needs no administrator rights. Features the hardware or Windows cannot provide are " +
        "clearly labeled instead of simulated. Not affiliated with Razer or any mouse manufacturer.";

    public string RuntimeText => $".NET {Environment.Version} · {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} · {Environment.OSVersion.VersionString}";

    private void Save() => Controller.OnAppSettingsEdited(this);

    private void ChangeStorage()
    {
        string? folder = DialogService.PickFolder("Choose where Synapse stores its settings", Controller.DataDirectory);
        if (folder is null)
        {
            return;
        }

        try
        {
            Controller.ChangeStorageLocation(folder);
            OnPropertyChanged(nameof(StorageLocation));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            DialogService.Info("Could not change the storage location", ex.Message);
        }
    }

    private void ResetAll()
    {
        if (!DialogService.Confirm(
                "Reset all settings?",
                "This removes every custom config and association, restores default settings, turns off Start with Windows and returns " +
                "the mouse to normal Windows behavior. This cannot be undone — export a backup first if you might want your configs back.",
                "Reset everything",
                danger: true))
        {
            return;
        }

        Controller.ResetAll();
        RefreshAll();
        DialogService.Info("Settings reset", "Synapse is back to its default settings. Your Windows mouse settings are unchanged.");
    }

    private void ExportBackup()
    {
        string? path = DialogService.SaveFile("Export all settings", "Synapse backup (*.synapsebackup)|*.synapsebackup",
            "SynapseBackup-" + DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ConfigSerializer.BackupExtension);
        if (path is null)
        {
            return;
        }

        try
        {
            Controller.ExportBackup(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DialogService.Info("Export failed", ex.Message);
        }
    }

    private void ImportBackup()
    {
        string? path = DialogService.OpenFile("Import settings backup", "Synapse backup (*.synapsebackup)|*.synapsebackup|All files (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        if (!DialogService.Confirm("Replace all settings?", "Every config and setting will be replaced with the contents of the backup.", "Import", danger: true))
        {
            return;
        }

        try
        {
            Controller.ImportBackup(path);
            RefreshAll();
            DialogService.Info("Backup imported", "Your settings were restored from the backup.");
        }
        catch (Exception ex) when (ex is ConfigFormatException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("Backup import failed.", ex);
            DialogService.Info("Import failed", ex.Message);
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            DialogService.Info("Could not open the folder", ex.Message);
        }
    }
}
