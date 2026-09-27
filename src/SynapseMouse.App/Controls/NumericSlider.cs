using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SynapseMouse.App.Controls;

/// <summary>
/// A slider with an editable numeric box and unit. The box accepts values beyond the slider's range up to
/// <see cref="Maximum"/>; every value is clamped and rounded before it reaches the bound property.
/// </summary>
internal sealed class NumericSlider : Grid
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumericSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(NumericSlider), new PropertyMetadata(0.0, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(NumericSlider), new PropertyMetadata(100.0, OnRangeChanged));

    public static readonly DependencyProperty SliderMaximumProperty = DependencyProperty.Register(
        nameof(SliderMaximum), typeof(double), typeof(NumericSlider), new PropertyMetadata(double.NaN, OnRangeChanged));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(NumericSlider), new PropertyMetadata(1.0, OnRangeChanged));

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(NumericSlider), new PropertyMetadata(0, OnValueChanged));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(NumericSlider), new PropertyMetadata(string.Empty, (d, e) => ((NumericSlider)d)._unit.Text = (string)e.NewValue));

    private readonly Slider _slider;
    private readonly TextBox _box;
    private readonly TextBlock _unit;
    private bool _updating;

    public NumericSlider()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        MinWidth = 280;

        _slider = new Slider { VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 14, 0) };
        _slider.ValueChanged += (_, e) =>
        {
            if (!_updating)
            {
                Commit(e.NewValue);
            }
        };
        Children.Add(_slider);

        _box = new TextBox
        {
            Width = 64,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(6, 5, 6, 5),
        };
        _box.KeyDown += OnBoxKeyDown;
        _box.LostKeyboardFocus += (_, _) => CommitText();
        _box.GotKeyboardFocus += (_, _) => _box.SelectAll();

        _unit = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0),
            FontSize = 12,
            MinWidth = 22,
            Foreground = (Brush)Application.Current.FindResource("Brush.TextSecondary"),
        };

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_box);
        right.Children.Add(_unit);
        SetColumn(right, 1);
        Children.Add(right);

        UpdateRange();
        UpdateText();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Upper end of the slider track (defaults to <see cref="Maximum"/>).</summary>
    public double SliderMaximum
    {
        get => (double)GetValue(SliderMaximumProperty);
        set => SetValue(SliderMaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (NumericSlider)d;
        control._updating = true;
        try
        {
            control._slider.Value = control.Value;
        }
        finally
        {
            control._updating = false;
        }

        if (!control._box.IsKeyboardFocusWithin)
        {
            control.UpdateText();
        }
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((NumericSlider)d).UpdateRange();

    private void UpdateRange()
    {
        _updating = true;
        try
        {
            _slider.Minimum = Minimum;
            _slider.Maximum = double.IsNaN(SliderMaximum) ? Maximum : SliderMaximum;
            _slider.TickFrequency = Step;
            _slider.SmallChange = Step;
            _slider.LargeChange = Step * 5;
            _slider.Value = Value;
        }
        finally
        {
            _updating = false;
        }
    }

    private void Commit(double raw)
    {
        double value = Math.Round(Math.Clamp(raw, Minimum, Maximum), Decimals, MidpointRounding.AwayFromZero);
        if (value != Value)
        {
            Value = value;
        }

        UpdateText();
    }

    private void CommitText()
    {
        string text = _box.Text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            Commit(parsed);
        }
        else
        {
            UpdateText();
        }
    }

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitText();
                _box.SelectAll();
                e.Handled = true;
                break;
            case Key.Escape:
                UpdateText();
                e.Handled = true;
                break;
            case Key.Up:
                Commit(Value + Step);
                e.Handled = true;
                break;
            case Key.Down:
                Commit(Value - Step);
                e.Handled = true;
                break;
        }
    }

    private void UpdateText() =>
        _box.Text = Value.ToString("F" + Math.Clamp(Decimals, 0, 4).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);
}
