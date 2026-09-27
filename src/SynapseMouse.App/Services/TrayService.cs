using System.Drawing;
using System.Windows.Forms;
using SynapseMouse.App.Infrastructure;

namespace SynapseMouse.App.Services;

/// <summary>
/// System-tray icon and its context menu (Open, Master Enable, Current Config, Settings, Exit).
/// Uses the WinForms NotifyIcon, which works alongside WPF on the same UI thread.
/// </summary>
internal sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _masterItem;
    private readonly ToolStripMenuItem _configItem;
    private readonly Icon _onIcon;
    private readonly Icon _offIcon;
    private IReadOnlyList<(Guid Id, string Name)> _configs = Array.Empty<(Guid, string)>();
    private Guid _activeId;

    public TrayService()
    {
        _onIcon = LoadIcon("tray-on.ico");
        _offIcon = LoadIcon("tray-off.ico");

        _menu = new ContextMenuStrip
        {
            Renderer = new MidnightRenderer(),
            Font = new Font("Segoe UI", 9.5f),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Padding(4),
        };

        var open = new ToolStripMenuItem("Open " + AppInfo.Name) { Font = new Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke();
        _masterItem = new ToolStripMenuItem("Master Enable") { CheckOnClick = false };
        _masterItem.Click += (_, _) => MasterToggleRequested?.Invoke();
        _configItem = new ToolStripMenuItem("Current Config");
        var settings = new ToolStripMenuItem("Settings");
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke();

        _menu.Items.AddRange(new ToolStripItem[]
        {
            open,
            new ToolStripSeparator(),
            _masterItem,
            _configItem,
            settings,
            new ToolStripSeparator(),
            exit,
        });
        _menu.Opening += (_, _) => RebuildConfigMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _onIcon,
            Text = AppInfo.Name,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };
    }

    public event Action? OpenRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public event Action? MasterToggleRequested;

    public event Action<Guid>? ConfigSelected;

    public void Update(bool masterEnabled, string configName, IReadOnlyList<(Guid Id, string Name)> configs, Guid activeId)
    {
        _configs = configs;
        _activeId = activeId;
        _masterItem.Checked = masterEnabled;
        _masterItem.Text = masterEnabled ? "Master Enable: ON" : "Master Enable: OFF";
        _configItem.Text = "Current Config: " + configName;
        _notifyIcon.Icon = masterEnabled ? _onIcon : _offIcon;

        // Tooltip text is limited to 127 characters.
        string tip = $"{AppInfo.Name}\n{(masterEnabled ? "● Enabled" : "○ Disabled")} · {configName}";
        _notifyIcon.Text = tip.Length > 127 ? tip[..127] : tip;
        RebuildConfigMenu();
    }

    public void ShowNotification(string title, string text)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(4000, title, text, ToolTipIcon.None);
        }
        catch (Exception ex)
        {
            Log.Warn("Tray notification failed.", ex);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _onIcon.Dispose();
        _offIcon.Dispose();
    }

    private void RebuildConfigMenu()
    {
        _configItem.DropDownItems.Clear();
        foreach (var (id, name) in _configs)
        {
            var item = new ToolStripMenuItem(name) { Checked = id == _activeId };
            Guid captured = id;
            item.Click += (_, _) => ConfigSelected?.Invoke(captured);
            _configItem.DropDownItems.Add(item);
        }

        if (_configItem.DropDown is ToolStripDropDownMenu dropDown)
        {
            dropDown.Renderer = _menu.Renderer;
            dropDown.ShowImageMargin = false;
            dropDown.ShowCheckMargin = true;
        }
    }

    private static Icon LoadIcon(string name)
    {
        var info = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"));
        using var stream = info!.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    private sealed class MidnightRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Text = Color.FromArgb(240, 242, 245);
        private static readonly Color DimText = Color.FromArgb(110, 115, 125);

        public MidnightRenderer()
            : base(new MidnightColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : DimText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var pen = new Pen(Text, 1.8f);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int cx = r.Left + (r.Width / 2);
            int cy = r.Top + (r.Height / 2);
            e.Graphics.DrawLines(pen, new[] { new Point(cx - 4, cy), new Point(cx - 1, cy + 3), new Point(cx + 4, cy - 3) });
        }
    }

    private sealed class MidnightColors : ProfessionalColorTable
    {
        private static readonly Color Background = Color.FromArgb(17, 19, 23);
        private static readonly Color Hover = Color.FromArgb(36, 39, 46);
        private static readonly Color Border = Color.FromArgb(46, 50, 57);

        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color CheckBackground => Background;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Background;
    }
}
