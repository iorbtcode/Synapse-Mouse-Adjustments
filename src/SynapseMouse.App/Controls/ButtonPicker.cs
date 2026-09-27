using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SynapseMouse.Core.Models;

namespace SynapseMouse.App.Controls;

/// <summary>Row of pill toggles for choosing which mouse buttons a feature applies to.</summary>
internal sealed class ButtonPicker : StackPanel
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(MouseButtonFlags), typeof(ButtonPicker),
        new FrameworkPropertyMetadata(MouseButtonFlags.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ButtonPicker)d).Sync()));

    public static readonly DependencyProperty ShowSideButtonsProperty = DependencyProperty.Register(
        nameof(ShowSideButtons), typeof(bool), typeof(ButtonPicker), new PropertyMetadata(true, (d, _) => ((ButtonPicker)d).Build()));

    private readonly List<(MouseButton Button, ToggleButton Toggle)> _toggles = new();
    private bool _syncing;

    public ButtonPicker()
    {
        Orientation = Orientation.Horizontal;
        Build();
    }

    public MouseButtonFlags Value
    {
        get => (MouseButtonFlags)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool ShowSideButtons
    {
        get => (bool)GetValue(ShowSideButtonsProperty);
        set => SetValue(ShowSideButtonsProperty, value);
    }

    private void Build()
    {
        Children.Clear();
        _toggles.Clear();
        var buttons = ShowSideButtons
            ? new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.XButton1, MouseButton.XButton2 }
            : new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle };
        var style = (Style)Application.Current.FindResource("Chip");
        foreach (var button in buttons)
        {
            var toggle = new ToggleButton { Content = button.ShortName(), Style = style, ToolTip = button.DisplayName() };
            toggle.Checked += (_, _) => OnToggle();
            toggle.Unchecked += (_, _) => OnToggle();
            _toggles.Add((button, toggle));
            Children.Add(toggle);
        }

        Sync();
    }

    private void Sync()
    {
        _syncing = true;
        try
        {
            foreach (var (button, toggle) in _toggles)
            {
                toggle.IsChecked = Value.Has(button);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnToggle()
    {
        if (_syncing)
        {
            return;
        }

        var value = Value;
        foreach (var (button, toggle) in _toggles)
        {
            value = toggle.IsChecked == true ? value | button.ToFlag() : value & ~button.ToFlag();
        }

        Value = value;
    }
}
