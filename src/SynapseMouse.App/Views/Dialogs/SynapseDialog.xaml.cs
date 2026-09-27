using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SynapseMouse.App.Views.Dialogs;

/// <summary>Midnight-styled message / confirm / prompt / picker dialog.</summary>
internal partial class SynapseDialog : Window
{
    private DispatcherTimer? _countdown;
    private int _secondsLeft;

    public SynapseDialog()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
    }

    public string InputText => InputBox.Text;

    public object? SelectedChoice => ChoiceList.SelectedItem;

    public static SynapseDialog Create(Window? owner, string title, string message, string primary, string? secondary, bool danger)
    {
        var dialog = new SynapseDialog();
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true;
        }

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        dialog.PrimaryButton.Content = primary;
        if (secondary is null)
        {
            dialog.SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            dialog.SecondaryButton.Content = secondary;
        }

        if (danger)
        {
            dialog.PrimaryButton.Style = (Style)dialog.FindResource("Button.Danger");
        }

        return dialog;
    }

    public void ShowInput(string initial)
    {
        InputBox.Text = initial;
        InputBox.Visibility = Visibility.Visible;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public void ShowChoices(IEnumerable<object> choices)
    {
        ChoiceList.ItemsSource = choices.ToList();
        ChoiceList.Visibility = Visibility.Visible;
    }

    /// <summary>Automatically cancels after the given time unless the primary button is pressed.</summary>
    public void StartCountdown(int seconds, string format)
    {
        _secondsLeft = seconds;
        CountdownText.Visibility = Visibility.Visible;
        CountdownText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, format, _secondsLeft);
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            _secondsLeft--;
            if (_secondsLeft <= 0)
            {
                _countdown.Stop();
                DialogResult = false;
                return;
            }

            CountdownText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, format, _secondsLeft);
        };
        Loaded += (_, _) =>
        {
            _countdown.Start();
            PrimaryButton.Focus();
        };
        Closed += (_, _) => _countdown.Stop();
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (ChoiceList.Visibility == Visibility.Visible && ChoiceList.SelectedItem is null)
        {
            return;
        }

        DialogResult = true;
    }

    private void OnSecondary(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnChoiceDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ChoiceList.SelectedItem is not null)
        {
            DialogResult = true;
        }
    }
}
