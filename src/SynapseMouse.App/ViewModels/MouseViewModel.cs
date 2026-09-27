using System.Windows.Input;
using SynapseMouse.App.Services;

namespace SynapseMouse.App.ViewModels;

internal sealed class DeviceItem
{
    private const string Unavailable = "Unavailable";

    public DeviceItem(MouseDeviceInfo info, bool isPrimary, bool isActive)
    {
        Name = info.DisplayName;
        Manufacturer = info.Manufacturer ?? Unavailable;
        Connection = info.ConnectionType == "Unknown" ? Unavailable : info.ConnectionType;
        WindowsDescription = info.WindowsDescription ?? Unavailable;
        DeviceId = info.InstanceId ?? Unavailable;
        VendorProduct = info.VendorId is null ? Unavailable : $"VID {info.VendorId} · PID {info.ProductId ?? "?"}";
        Buttons = info.ButtonCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Unavailable;
        HorizontalWheel = info.HasHorizontalWheel switch { true => "Yes", false => "No", _ => Unavailable };
        IsVirtual = info.IsVirtual;
        IsPrimary = isPrimary;
        IsActive = isActive;
        Badge = isActive ? "IN USE" : isPrimary ? "PRIMARY" : info.IsVirtual ? "VIRTUAL" : string.Empty;
    }

    public string Name { get; }

    public string Manufacturer { get; }

    public string Connection { get; }

    public string WindowsDescription { get; }

    public string DeviceId { get; }

    public string VendorProduct { get; }

    public string Buttons { get; }

    public string HorizontalWheel { get; }

    public bool IsVirtual { get; }

    public bool IsPrimary { get; }

    public bool IsActive { get; }

    public string Badge { get; }
}

internal sealed class MouseViewModel : PageViewModel
{
    private const string Unavailable = "Unavailable";

    public MouseViewModel(AppController controller)
        : base(controller, "mouse", "Mouse", "What Windows reports about your pointing devices. Anything the hardware does not expose is shown as Unavailable.")
    {
        RefreshCommand = new RelayCommand(() => Controller.Devices.Refresh());
        controller.DevicesChanged += (_, _) => RefreshAll();
        controller.StateChanged += (_, _) => RefreshAll();
        controller.ActiveConfigChanged += (_, _) => RefreshAll();
    }

    public ICommand RefreshCommand { get; }

    public IReadOnlyList<DeviceItem> Devices
    {
        get
        {
            var primary = Controller.Devices.Primary;
            var active = Controller.Devices.LastActiveHandle;
            return Controller.Devices.Devices
                .Select(d => new DeviceItem(d, ReferenceEquals(d, primary), d.Handle == active && active != IntPtr.Zero))
                .ToList();
        }
    }

    private MouseDeviceInfo? Primary => Controller.Devices.Primary;

    public string DeviceName => Primary?.DisplayName ?? (Controller.Devices.HasEnumerated ? "No mouse detected" : "Detecting…");

    public string Manufacturer => Primary?.Manufacturer ?? Unavailable;

    public string ConnectionStatus => Primary is null ? "Not connected" : "Connected";

    public string ConnectionType => Primary is null || Primary.ConnectionType == "Unknown" ? Unavailable : Primary.ConnectionType;

    public string DeviceId => Primary?.InstanceId ?? Unavailable;

    public string VendorProduct => Primary?.VendorId is null ? Unavailable : $"VID {Primary.VendorId} · PID {Primary.ProductId ?? "?"}";

    public string ButtonCount => Primary?.ButtonCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Unavailable;

    public string HorizontalWheel => Primary?.HasHorizontalWheel switch { true => "Yes", false => "No", _ => Unavailable };

    public string ActiveConfig => Controller.ActiveConfig.Name;

    public string SoftwareStatus => Controller.MasterEnabled
        ? "Master Enable ON — " + Controller.EngineStatusText
        : "Master Enable OFF — Windows handles the mouse normally";

    public string SideButtonNote => Primary?.ButtonCount is int n && n < 4
        ? $"Windows reports {n} buttons, so this mouse has no physical side buttons. Use Virtual buttons on the Buttons page to get Back/Forward and other actions from keyboard shortcuts."
        : "Side buttons 4 and 5 can be remapped on the Buttons page.";

    protected override void OnActiveChanged(bool active)
    {
        // While the page is visible, raw input identifies which device is actually being moved.
        if (active)
        {
            Controller.RawInput.Start();
            Controller.Devices.Refresh();
        }
        else
        {
            Controller.RawInput.Stop();
        }
    }
}
