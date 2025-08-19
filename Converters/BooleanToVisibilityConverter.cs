using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StarResonance.DPS.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        var boolValue = value is true;

        if (parameter is string str && str.Equals("inverse", StringComparison.OrdinalIgnoreCase))
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotImplementedException("ConvertBack is not implemented for BooleanToVisibilityConverter.");
    }
}