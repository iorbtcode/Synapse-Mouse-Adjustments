using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using SynapseMouse.App.Infrastructure;
using static SynapseMouse.App.Native.NativeMethods;

namespace SynapseMouse.App.Services;

/// <summary>What Windows reports about one pointing device. Missing values are null ("Unavailable").</summary>
internal sealed class MouseDeviceInfo
{
    public required IntPtr Handle { get; init; }

    public required string Path { get; init; }

    public string? ProductName { get; init; }

    public string? Manufacturer { get; init; }

    public string? WindowsDescription { get; init; }

    public string? InstanceId { get; init; }

    public string? VendorId { get; init; }

    public string? ProductId { get; init; }

    public string ConnectionType { get; init; } = "Unknown";

    public int? ButtonCount { get; init; }

    public bool? HasHorizontalWheel { get; init; }

    public bool IsVirtual { get; init; }

    /// <summary>Best available display name.</summary>
    public string DisplayName => ProductName ?? WindowsDescription ?? (IsVirtual ? "Virtual pointing device" : "Pointing device");
}

/// <summary>
/// Enumerates mice through the Raw Input API and reads names from the HID descriptor and the Windows
/// device tree. Nothing is guessed: values the device/Windows does not expose stay null.
/// Listens for plug/unplug notifications so the Mouse page and Dashboard update live.
/// </summary>
internal sealed class DeviceService : IDisposable
{
    private static readonly Regex VidPid = new(@"VID_([0-9A-F]{4}).*?PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly MessageWindow _window;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private IntPtr _notification;
    private int _refreshing;

    public DeviceService(MessageWindow window)
    {
        _window = window;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) =>
        {
            _debounce!.Stop();
            Refresh();
        }, _dispatcher);
        _debounce.Stop();
        _window.MessageReceived += OnMessage;
        RegisterForNotifications();
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<MouseDeviceInfo> Devices { get; private set; } = Array.Empty<MouseDeviceInfo>();

    /// <summary>The device last seen moving (set by the raw input monitor), if any.</summary>
    public IntPtr LastActiveHandle { get; set; }

    public bool HasEnumerated { get; private set; }

    /// <summary>The device shown as "your mouse": last used, else the first physical external mouse.</summary>
    public MouseDeviceInfo? Primary =>
        Devices.FirstOrDefault(d => d.Handle == LastActiveHandle && LastActiveHandle != IntPtr.Zero)
        ?? Devices.FirstOrDefault(d => !d.IsVirtual && d.ConnectionType is "USB" or "Bluetooth")
        ?? Devices.FirstOrDefault(d => !d.IsVirtual);

    public bool AnyPhysicalMouse => Devices.Any(d => !d.IsVirtual);

    /// <summary>Re-enumerates devices on a background thread (HID queries can be slow for wireless devices).</summary>
    public void Refresh()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        Task.Run(() =>
        {
            List<MouseDeviceInfo> list;
            try
            {
                list = Enumerate();
            }
            catch (Exception ex)
            {
                Log.Error("Mouse enumeration failed.", ex);
                list = new List<MouseDeviceInfo>();
            }

            _dispatcher.BeginInvoke(() =>
            {
                Devices = list;
                HasEnumerated = true;
                Interlocked.Exchange(ref _refreshing, 0);
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    public void Dispose()
    {
        _window.MessageReceived -= OnMessage;
        _debounce.Stop();
        if (_notification != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_notification);
            _notification = IntPtr.Zero;
        }
    }

    private void RegisterForNotifications()
    {
        var filter = new DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = GUID_DEVINTERFACE_MOUSE,
        };
        _notification = RegisterDeviceNotification(_window.Handle, ref filter, 0);
        if (_notification == IntPtr.Zero)
        {
            Log.Warn($"Device notifications unavailable (error {Marshal.GetLastWin32Error()}).");
        }
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE && (wParam.ToInt32() == DBT_DEVICEARRIVAL || wParam.ToInt32() == DBT_DEVICEREMOVECOMPLETE))
        {
            _debounce.Stop();
            _debounce.Start();
        }
    }

    private static List<MouseDeviceInfo> Enumerate()
    {
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        if (GetRawInputDeviceList(null, ref count, size) != 0 || count == 0)
        {
            return new List<MouseDeviceInfo>();
        }

        var list = new RAWINPUTDEVICELIST[count];
        uint got = GetRawInputDeviceList(list, ref count, size);
        if (got == uint.MaxValue)
        {
            return new List<MouseDeviceInfo>();
        }

        var result = new List<MouseDeviceInfo>();
        for (int i = 0; i < got; i++)
        {
            if (list[i].dwType != RIM_TYPEMOUSE)
            {
                continue;
            }

            try
            {
                var info = Describe(list[i].hDevice);
                if (info is not null)
                {
                    result.Add(info);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read a mouse device.", ex);
            }
        }

        return result
            .OrderBy(d => d.IsVirtual)
            .ThenBy(d => d.ConnectionType == "USB" || d.ConnectionType == "Bluetooth" ? 0 : 1)
            .ToList();
    }

    private static MouseDeviceInfo? Describe(IntPtr handle)
    {
        string? path = GetDeviceName(handle);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            path = @"\\?\" + path[4..];
        }

        var raw = new RID_DEVICE_INFO { cbSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO>() };
        uint rawSize = raw.cbSize;
        int? buttons = null;
        bool? hwheel = null;
        IntPtr buffer = Marshal.AllocHGlobal((int)rawSize);
        try
        {
            Marshal.StructureToPtr(raw, buffer, false);
            if (GetRawInputDeviceInfo(handle, RIDI_DEVICEINFO, buffer, ref rawSize) != uint.MaxValue)
            {
                raw = Marshal.PtrToStructure<RID_DEVICE_INFO>(buffer);
                if (raw.dwNumberOfButtons > 0)
                {
                    buttons = (int)raw.dwNumberOfButtons;
                }

                hwheel = raw.fHasHorizontalWheel != 0;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        bool isVirtual = path.Contains("RDP_MOU", StringComparison.OrdinalIgnoreCase)
                         || path.Contains("VMBUS", StringComparison.OrdinalIgnoreCase)
                         || path.Contains("VirtualMouse", StringComparison.OrdinalIgnoreCase);

        string? product = null;
        string? manufacturer = null;
        string? vid = null;
        string? pid = null;
        var match = VidPid.Match(path);
        if (match.Success)
        {
            vid = match.Groups[1].Value.ToUpperInvariant();
            pid = match.Groups[2].Value.ToUpperInvariant();
        }

        if (path.Contains("HID#", StringComparison.OrdinalIgnoreCase))
        {
            (product, manufacturer, vid, pid) = ReadHidStrings(path, vid, pid);
        }

        string? instanceId = GetInterfaceString(path, DEVPKEY_Device_InstanceId);
        string? description = null;
        string? busName = null;
        string connection = isVirtual ? "Virtual" : "Unknown";
        if (instanceId is not null && CM_Locate_DevNode(out uint devInst, instanceId, 0) == CR_SUCCESS)
        {
            description = GetNodeString(devInst, DEVPKEY_Device_FriendlyName) ?? GetNodeString(devInst, DEVPKEY_Device_DeviceDesc);
            connection = isVirtual ? "Virtual" : ClassifyConnection(instanceId, devInst, out busName);
        }

        return new MouseDeviceInfo
        {
            Handle = handle,
            Path = path,
            ProductName = Clean(product) ?? Clean(busName),
            Manufacturer = Clean(manufacturer),
            WindowsDescription = Clean(description),
            InstanceId = instanceId,
            VendorId = vid,
            ProductId = pid,
            ConnectionType = connection,
            ButtonCount = buttons,
            HasHorizontalWheel = hwheel,
            IsVirtual = isVirtual,
        };
    }

    private static string? GetDeviceName(IntPtr handle)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, IntPtr.Zero, ref chars);
        if (chars == 0)
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)(chars + 1) * 2);
        try
        {
            if (GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, buffer, ref chars) == uint.MaxValue)
            {
                return null;
            }

            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Opens the HID collection without read/write access (allowed even for system-owned mice).</summary>
    private static (string? Product, string? Manufacturer, string? Vid, string? Pid) ReadHidStrings(string path, string? vid, string? pid)
    {
        using var handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return (null, null, vid, pid);
        }

        var buffer = new byte[512];
        string? product = HidD_GetProductString(handle, buffer, buffer.Length) ? DecodeUtf16(buffer) : null;
        Array.Clear(buffer);
        string? manufacturer = HidD_GetManufacturerString(handle, buffer, buffer.Length) ? DecodeUtf16(buffer) : null;

        var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (HidD_GetAttributes(handle, ref attributes))
        {
            vid ??= attributes.VendorID.ToString("X4", CultureInfo.InvariantCulture);
            pid ??= attributes.ProductID.ToString("X4", CultureInfo.InvariantCulture);
        }

        return (product, manufacturer, vid, pid);
    }

    private static string ClassifyConnection(string instanceId, uint devInst, out string? busReportedName)
    {
        busReportedName = null;
        string connection = instanceId.StartsWith("ACPI", StringComparison.OrdinalIgnoreCase) ? "PS/2 / internal" : "Unknown";
        uint current = devInst;
        for (int depth = 0; depth < 4; depth++)
        {
            busReportedName ??= GetNodeString(current, DEVPKEY_Device_BusReportedDeviceDesc);
            string? id = GetNodeId(current);
            if (id is not null)
            {
                if (id.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                {
                    connection = "USB";
                }
                else if (id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) || id.Contains("00001124-0000-1000-8000-00805F9B34FB", StringComparison.OrdinalIgnoreCase))
                {
                    connection = "Bluetooth";
                }
                else if (id.StartsWith("ACPI", StringComparison.OrdinalIgnoreCase) && connection == "Unknown")
                {
                    connection = "PS/2 / internal";
                }
                else if (id.StartsWith("HID\\VID_06CB", StringComparison.OrdinalIgnoreCase) || id.Contains("I2C", StringComparison.OrdinalIgnoreCase))
                {
                    connection = "Internal (I²C)";
                }
            }

            if (connection is "USB" or "Bluetooth" || CM_Get_Parent(out uint parent, current, 0) != CR_SUCCESS)
            {
                break;
            }

            current = parent;
        }

        return connection;
    }

    private static string? GetInterfaceString(string path, DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CM_Get_Device_Interface_Property(path, ref key, out _, null, ref size, 0);
        if (cr != CR_BUFFER_SMALL || size == 0)
        {
            return null;
        }

        var buffer = new byte[size];
        cr = CM_Get_Device_Interface_Property(path, ref key, out uint type, buffer, ref size, 0);
        return cr == CR_SUCCESS && type == DEVPROP_TYPE_STRING ? DecodeUtf16(buffer) : null;
    }

    private static string? GetNodeString(uint devInst, DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CM_Get_DevNode_Property(devInst, ref key, out _, null, ref size, 0);
        if (cr != CR_BUFFER_SMALL || size == 0)
        {
            return null;
        }

        var buffer = new byte[size];
        cr = CM_Get_DevNode_Property(devInst, ref key, out uint type, buffer, ref size, 0);
        return cr == CR_SUCCESS && type == DEVPROP_TYPE_STRING ? DecodeUtf16(buffer) : null;
    }

    private static string? GetNodeId(uint devInst)
    {
        var sb = new StringBuilder(512);
        return CM_Get_Device_ID(devInst, sb, sb.Capacity, 0) == CR_SUCCESS ? sb.ToString() : null;
    }

    private static string DecodeUtf16(byte[] buffer)
    {
        string s = Encoding.Unicode.GetString(buffer);
        int nul = s.IndexOf('\0');
        return nul >= 0 ? s[..nul] : s;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        // Some drivers return placeholder strings; treat them as unavailable rather than inventing names.
        return value.Length < 2 ? null : value;
    }
}
