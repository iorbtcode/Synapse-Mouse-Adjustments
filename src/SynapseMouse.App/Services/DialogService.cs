using System.Windows;
using Microsoft.Win32;
using SynapseMouse.App.Views.Dialogs;

namespace SynapseMouse.App.Services;

/// <summary>Shows dialogs owned by the main window (or centered on screen when it is hidden).</summary>
internal static class DialogService
{
    private static Window? Owner =>
        Application.Current?.Windows.OfType<SynapseMouse.App.Views.MainWindow>().FirstOrDefault(w => w.IsVisible);

    /// <summary>Set by --self-test: dialogs are logged instead of shown (nothing may block).</summary>
    public static bool Headless { get; set; }

    public static List<string> HeadlessLog { get; } = new();

    public static void Info(string title, string message)
    {
        if (Headless)
        {
            HeadlessLog.Add($"Dialog: {title} — {message}");
            return;
        }

        SynapseDialog.Create(Owner, title, message, "OK", null, false).ShowDialog();
    }

    public static bool Confirm(string title, string message, string confirmText = "OK", bool danger = false)
    {
        if (Headless)
        {
            HeadlessLog.Add($"Confirm (declined): {title}");
            return false;
        }

        return SynapseDialog.Create(Owner, title, message, confirmText, "Cancel", danger).ShowDialog() == true;
    }

    public static string? Prompt(string title, string message, string initial, string confirmText = "Save")
    {
        if (Headless)
        {
            return null;
        }

        var dialog = SynapseDialog.Create(Owner, title, message, confirmText, "Cancel", false);
        dialog.ShowInput(initial);
        return dialog.ShowDialog() == true ? dialog.InputText : null;
    }

    /// <summary>Asks to keep a risky change; returns false (revert) if not confirmed in time.</summary>
    public static bool ConfirmWithTimeout(string title, string message, int seconds)
    {
        if (Headless)
        {
            HeadlessLog.Add($"Timed confirm (reverted): {title}");
            return false;
        }

        var dialog = SynapseDialog.Create(Owner, title, message, "Keep change", "Revert", false);
        dialog.StartCountdown(seconds, "Reverting automatically in {0} s — press Enter to keep.");
        return dialog.ShowDialog() == true;
    }

    public static (string ProcessName, string Title)? PickApplication(IEnumerable<(string ProcessName, string Title)> apps)
    {
        var dialog = SynapseDialog.Create(Owner, "Choose an application",
            "Pick a running app. Its config will activate automatically whenever the app is in the foreground.",
            "Select", "Cancel", false);
        dialog.ShowChoices(apps.Select(a => (object)Tuple.Create(a.ProcessName, a.Title)));
        return dialog.ShowDialog() == true && dialog.SelectedChoice is Tuple<string, string> t ? (t.Item1, t.Item2) : null;
    }

    public static string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return Show(dialog) ? dialog.FileName : null;
    }

    public static string? SaveFile(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, AddExtension = true };
        return Show(dialog) ? dialog.FileName : null;
    }

    private static bool Show(CommonDialog dialog) =>
        (Owner is { IsVisible: true } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    public static string? PickFolder(string title, string initial)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = initial };
        return Show(dialog) ? dialog.FolderName : null;
    }
}
