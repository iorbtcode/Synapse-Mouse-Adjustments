using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using SynapseMouse.App.Infrastructure;
using SynapseMouse.App.Native;
using SynapseMouse.App.Services;
using SynapseMouse.App.ViewModels;

namespace SynapseMouse.App.Views;

/// <summary>
/// The main window. Closing it (×) only hides Synapse to the tray — the input engine keeps running.
/// The window object is released while hidden to keep memory use low; it is recreated on demand.
/// </summary>
internal partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppController _controller;

    public MainWindow(MainViewModel viewModel, AppController controller)
    {
        _viewModel = viewModel;
        _controller = controller;
        InitializeComponent();
        DataContext = viewModel;
        IsVisibleChanged += (_, _) => _viewModel.SetWindowVisible(IsVisible && WindowState != WindowState.Minimized);
        StateChanged += OnStateChanged;
        Closing += OnClosing;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Maximized WindowChrome windows extend past the screen edge by the resize border.
        RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        _viewModel.SetWindowVisible(IsVisible && WindowState != WindowState.Minimized);
        if (WindowState == WindowState.Minimized && _controller.App.MinimizeToTray)
        {
            Dispatcher.BeginInvoke(Close);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _viewModel.SetWindowVisible(false);
        if (!_controller.App.HasShownTrayHint)
        {
            _controller.App.HasShownTrayHint = true;
            _controller.Configs.NotifyAppSettingsEdited();
            if (_controller.App.ShowTrayNotifications)
            {
                _controller.Tray.ShowNotification(AppInfo.Name + " is still running",
                    "Your settings stay active in the background. Use Exit in the tray menu to quit completely.");
            }
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
