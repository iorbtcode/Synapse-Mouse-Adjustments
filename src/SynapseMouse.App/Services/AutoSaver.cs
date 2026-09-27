using System.Windows.Threading;
using SynapseMouse.App.Infrastructure;
using SynapseMouse.Core.Config;
using SynapseMouse.Core.Models;
using SynapseMouse.Core.Persistence;

namespace SynapseMouse.App.Services;

/// <summary>
/// Saves settings automatically shortly after the last change (so dragging a slider writes once),
/// and synchronously on exit, sign-out and shutdown.
/// </summary>
internal sealed class AutoSaver
{
    private readonly Func<SettingsDocument> _document;
    private readonly DispatcherTimer _timer;
    private bool _dirty;
    private bool _reportedFailure;

    public AutoSaver(SettingsStore store, Func<SettingsDocument> document)
    {
        Store = store;
        _document = document;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Flush();
    }

    public event Action<string>? SaveFailed;

    public event EventHandler? Saved;

    public SettingsStore Store { get; set; }

    public DateTime? LastSaved { get; private set; }

    public void Request()
    {
        _dirty = true;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Writes pending changes now. Returns false if the write failed.</summary>
    public bool Flush(bool force = false)
    {
        _timer.Stop();
        if (!_dirty && !force)
        {
            return true;
        }

        try
        {
            Store.SaveText(ConfigSerializer.Serialize(_document()));
            _dirty = false;
            _reportedFailure = false;
            LastSaved = DateTime.Now;
            Saved?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("Saving settings failed.", ex);
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                SaveFailed?.Invoke(ex.Message);
            }

            return false;
        }
    }
}
