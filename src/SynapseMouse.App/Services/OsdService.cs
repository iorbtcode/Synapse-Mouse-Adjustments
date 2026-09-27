using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SynapseMouse.App.Native;

namespace SynapseMouse.App.Services;

/// <summary>
/// A small click-through on-screen indicator for changes made with hotkeys (DPI stage, config, Master
/// Enable) while the main window is hidden. It never takes focus or input.
/// </summary>
internal sealed class OsdService
{
    private OsdWindow? _window;

    public bool Enabled { get; set; } = true;

    public void Show(string caption, string value)
    {
        if (!Enabled)
        {
            return;
        }

        _window ??= new OsdWindow();
        _window.Display(caption, value);
    }

    public void Close()
    {
        _window?.Close();
        _window = null;
    }

    private sealed class OsdWindow : Window
    {
        private readonly TextBlock _caption;
        private readonly TextBlock _value;
        private readonly DispatcherTimer _hideTimer;

        public OsdWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "Synapse OSD";

            _caption = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x9B)),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _value = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
            };
            var panel = new StackPanel();
            panel.Children.Add(_caption);
            panel.Children.Add(_value);
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x0B, 0x0D, 0x12)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(26, 12, 26, 14),
                MinWidth = 180,
                Child = panel,
            };
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
                fade.Completed += (_, _) =>
                {
                    if (Opacity == 0)
                    {
                        Hide();
                    }
                };
                BeginAnimation(OpacityProperty, fade);
            };
        }

        public void Display(string caption, string value)
        {
            _caption.Text = caption.ToUpperInvariant();
            _value.Text = value;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (!IsVisible)
            {
                Show();
            }

            UpdateLayout();
            var area = SystemParameters.WorkArea;
            Left = area.Left + ((area.Width - ActualWidth) / 2);
            Top = area.Bottom - ActualHeight - 110;
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            style |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        }
    }
}
