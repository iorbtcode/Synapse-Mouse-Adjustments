using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SynapseMouse.App.Services;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.ViewModels;

internal abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Notifies that every property may have changed.</summary>
    protected void RefreshAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}

/// <summary>Label/value pair for combo boxes.</summary>
internal sealed record Option<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

/// <summary>A navigation page.</summary>
internal abstract class PageViewModel : ViewModelBase
{
    protected PageViewModel(AppController controller, string key, string title, string subtitle)
    {
        Controller = controller;
        Key = key;
        Title = title;
        Subtitle = subtitle;
    }

    public string Key { get; }

    public string Title { get; }

    public string Subtitle { get; }

    protected AppController Controller { get; }

    public bool IsActive { get; private set; }

    /// <summary>Called when the page becomes visible/hidden (window shown and page selected).</summary>
    public void SetActive(bool active)
    {
        if (IsActive == active)
        {
            return;
        }

        IsActive = active;
        OnActiveChanged(active);
    }

    /// <summary>Start/stop live work (timers, raw input, tracing) when the page is shown or hidden.</summary>
    protected virtual void OnActiveChanged(bool active)
    {
    }
}

/// <summary>
/// Base for pages that edit the active config. Every setter goes through <see cref="Edit"/>, which applies
/// the change immediately (engine + Windows) and schedules an automatic save.
/// </summary>
internal abstract class ConfigPageViewModel : PageViewModel
{
    protected ConfigPageViewModel(AppController controller, string key, string title, string subtitle)
        : base(controller, key, title, subtitle)
    {
        controller.ActiveConfigChanged += (_, _) => OnConfigReplaced();
        controller.ConfigEdited += (sender, _) =>
        {
            if (!ReferenceEquals(sender, this))
            {
                OnExternalEdit();
            }
        };
    }

    public string ConfigName => Config.Name;

    protected MouseConfig Config => Controller.ActiveConfig;

    protected void Edit(Action<MouseConfig> change, [CallerMemberName] string? name = null)
    {
        change(Config);
        Controller.OnConfigEdited(this);
        OnPropertyChanged(name);
        OnEdited();
    }

    /// <summary>Hook for dependent properties after an edit made by this page.</summary>
    protected virtual void OnEdited()
    {
    }

    /// <summary>The active config was switched/reset: rebuild everything.</summary>
    protected virtual void OnConfigReplaced() => RefreshAll();

    /// <summary>Another page edited the active config.</summary>
    protected virtual void OnExternalEdit() => RefreshAll();
}
