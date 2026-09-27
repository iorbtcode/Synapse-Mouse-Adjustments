using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using SynapseMouse.App.Infrastructure;
using SynapseMouse.App.Services;

namespace SynapseMouse.App.ViewModels;

/// <summary>Sidebar entry.</summary>
internal sealed class NavItem : ViewModelBase
{
    private readonly MainViewModel _owner;
    private bool _isSelected;

    public NavItem(MainViewModel owner, PageViewModel page, string iconKey)
    {
        _owner = owner;
        Page = page;
        Icon = (Geometry)Application.Current.FindResource(iconKey);
    }

    public PageViewModel Page { get; }

    public string Title => Page.Title;

    public Geometry Icon { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value) && value)
            {
                _owner.Navigate(Page.Key);
            }
        }
    }

    internal void SetSelected(bool value) => SetField(ref _isSelected, value, nameof(IsSelected));
}

/// <summary>Root view model: navigation plus the always-visible master/status header.</summary>
internal sealed class MainViewModel : ViewModelBase
{
    private readonly AppController _controller;
    private PageViewModel _currentPage;
    private bool _windowVisible;

    public MainViewModel(AppController controller)
    {
        _controller = controller;
        Dashboard = new DashboardViewModel(controller, this);
        var pages = new (PageViewModel Page, string Icon)[]
        {
            (Dashboard, "Icon.Dashboard"),
            (new MouseViewModel(controller), "Icon.Mouse"),
            (new ButtonsViewModel(controller), "Icon.Buttons"),
            (new ClickViewModel(controller), "Icon.Click"),
            (new DpiViewModel(controller), "Icon.Dpi"),
            (new PollingViewModel(controller), "Icon.Polling"),
            (new ScrollViewModel(controller), "Icon.Scroll"),
            (new ConfigsViewModel(controller), "Icon.Configs"),
            (new TesterViewModel(controller), "Icon.Tester"),
            (new SettingsViewModel(controller), "Icon.Settings"),
        };
        Pages = new ObservableCollection<NavItem>(pages.Select(p => new NavItem(this, p.Page, p.Icon)));
        _currentPage = Dashboard;
        Pages[0].SetSelected(true);

        controller.StateChanged += (_, _) => RefreshHeader();
        controller.ActiveConfigChanged += (_, _) => RefreshHeader();
        controller.ConfigListChanged += (_, _) => RefreshHeader();
    }

    public DashboardViewModel Dashboard { get; }

    public ObservableCollection<NavItem> Pages { get; }

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set
        {
            var previous = _currentPage;
            if (SetField(ref _currentPage, value))
            {
                previous.SetActive(false);
                value.SetActive(_windowVisible);
            }
        }
    }

    public string AppTitle => AppInfo.Name;

    public string Version => "v" + AppInfo.Version;

    public bool MasterEnabled
    {
        get => _controller.MasterEnabled;
        set => _controller.SetMaster(value);
    }

    public string MasterText => _controller.MasterEnabled ? "● ENABLED" : "○ DISABLED";

    public string ConfigName => _controller.ActiveConfig.Name;

    public void Navigate(string key)
    {
        var item = Pages.FirstOrDefault(p => p.Page.Key == key) ?? Pages[0];
        foreach (var p in Pages)
        {
            p.SetSelected(ReferenceEquals(p, item));
        }

        CurrentPage = item.Page;
    }

    /// <summary>Called by the window when it is shown/hidden so live pages stop work while hidden.</summary>
    public void SetWindowVisible(bool visible)
    {
        _windowVisible = visible;
        _currentPage.SetActive(visible);
    }

    private void RefreshHeader()
    {
        OnPropertyChanged(nameof(MasterEnabled));
        OnPropertyChanged(nameof(MasterText));
        OnPropertyChanged(nameof(ConfigName));
    }
}
