using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ChatPulse.Ui;

/// <summary>Collapses the element when the bound bool is <c>true</c>.</summary>
public sealed class InverseBoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows the element only when the bound string is non-empty.</summary>
public sealed class NonEmptyToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Turns a 0..1 fraction into a star <see cref="GridLength"/>, with <see cref="RemainderStar"/>
/// taking the rest. A width cannot be computed from a fraction alone — the container's size is
/// unknown to a converter — so the proportion is expressed through the grid instead.
/// </summary>
public sealed class FractionStar : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new GridLength(Math.Clamp(value as double? ?? 0, 0, 1), GridUnitType.Star);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RemainderStar : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new GridLength(1 - Math.Clamp(value as double? ?? 0, 0, 1), GridUnitType.Star);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Compares the bound value with the parameter — drives the segmented controls.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return false;
        if (parameter is null) return false;

        // XAML parameters arrive as strings even when the source is an int or an enum.
        return string.Equals(
            value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
