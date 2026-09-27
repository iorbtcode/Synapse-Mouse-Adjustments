using System.Windows;
using System.Windows.Threading;
using SynapseMouse.App.Infrastructure;
using SynapseMouse.App.Services;
using SynapseMouse.App.ViewModels;
using SynapseMouse.App.Views;

namespace SynapseMouse.App;

internal partial class App : Application
{
    private AppController? _controller;
    private MainViewModel? _mainViewModel;
    private MainWindow? _window;

    /// <summary>Launched by Windows at sign-in (--startup): start hidden in the tray.</summary>
    public bool StartedFromStartup { get; init; }

    public void ShowMainWindow(string? page)
    {
        if (_controller is null || _mainViewModel is null)
        {
            return;
        }

        if (_window is null)
        {
            _window = new MainWindow(_mainViewModel, _controller);
            _window.Closed += (_, _) => _window = null;
        }

        if (page is not null)
        {
            _mainViewModel.Navigate(page);
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        try
        {
            _controller = new AppController();
            _controller.ShowWindowRequested += ShowMainWindow;
            _controller.Start();
            _mainViewModel = new MainViewModel(_controller);
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed.", ex);
            _controller?.EmergencyCleanup();
            MessageBox.Show("Synapse Mouse Adjustments could not start:\n\n" + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        bool startHidden = StartedFromStartup || _controller.App.StartMinimized;
        if (!startHidden)
        {
            ShowMainWindow(null);
        }

        if (_controller.LoadWarning is { } warning)
        {
            if (startHidden)
            {
                _controller.Tray.ShowNotification("Settings recovered", warning);
            }
            else
            {
                DialogService.Info("Settings recovered", warning);
            }
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Sign-out / shutdown: save and restore Windows settings before the session ends.
        base.OnSessionEnding(e);
        _controller?.Exit();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
        try
        {
            DialogService.Info("Something went wrong", "Synapse hit an unexpected error but is still running.\n\n" + e.Exception.Message);
        }
        catch (Exception)
        {
            // Never let error reporting take the app down.
        }
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Fatal exception.", e.ExceptionObject as Exception);
        // The process is going down: release input and restore Windows settings first.
        _controller?.EmergencyCleanup();
    }
}
