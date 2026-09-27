using System.Windows.Interop;

namespace SynapseMouse.App.Infrastructure;

/// <summary>
/// A hidden message-only window on the UI thread. Receives hotkeys, device-change notifications and
/// raw input without needing the main window to exist (it may be closed to the tray).
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);
    private readonly HwndSource _source;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("SynapseMouseAdjustments.MessageWindow")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public delegate void MessageHandler(int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

    public event MessageHandler? MessageReceived;

    public IntPtr Handle => _source.Handle;

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            MessageReceived?.Invoke(msg, wParam, lParam, ref handled);
        }
        catch (Exception ex)
        {
            Log.Error("Message window handler failed.", ex);
        }

        return IntPtr.Zero;
    }
}
