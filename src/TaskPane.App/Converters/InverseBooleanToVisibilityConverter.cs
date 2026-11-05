using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskPane.App.Converters;

/// <summary>Maps <c>true</c> → Collapsed and <c>false</c> → Visible.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
