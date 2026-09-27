using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SynapseMouse.App.Converters;

/// <summary>true → Visible, false → Collapsed (Invert reverses).</summary>
internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is Visibility.Visible) ^ Invert;
}

internal sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>null/empty → Collapsed.</summary>
internal sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Binds an enum property to a RadioButton: ConverterParameter is the enum member name.</summary>
internal sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null && parameter is string name && string.Equals(value.ToString(), name, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string name)
        {
            Type enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (enumType.IsEnum && Enum.TryParse(enumType, name, out object? result))
            {
                return result!;
            }
        }

        return Binding.DoNothing;
    }
}

/// <summary>0 → Visible, anything else → Collapsed (for "nothing here yet" hints).</summary>
internal sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
