using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SynapseMouse.App.Controls;
using SynapseMouse.App.Services;
using SynapseMouse.App.ViewModels;

namespace SynapseMouse.App.Views;

internal partial class TesterView : UserControl
{
    private TesterViewModel? _viewModel;

    public TesterView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as TesterViewModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as TesterViewModel);
    }

    private void Attach(TesterViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.TimelineUpdated -= OnTimelineUpdated;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.TimelineUpdated += OnTimelineUpdated;
        }
    }

    private void OnTimelineUpdated(IReadOnlyList<TimelineSpan> spans, IReadOnlyList<TimelineMark> marks, double now) =>
        Timeline.Update(spans, marks, now);

    private void OnPadMouseDown(object sender, MouseButtonEventArgs e)
    {
        // WPF reports ClickCount using the Windows double-click time and area, i.e. real Windows detection.
        if (e.ClickCount == 2)
        {
            _viewModel?.OnPadDoubleClick(WindowsMouseSettings.GetSessionDoubleClickTime());
        }

        e.Handled = true;
    }
}
