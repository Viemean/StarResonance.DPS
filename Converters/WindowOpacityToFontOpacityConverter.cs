using System.Globalization;
using System.Windows.Data;

namespace StarResonance.DPS.Converters;

public class WindowOpacityToFontOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double windowOpacity) return windowOpacity < 0.8 ? 0.8 : windowOpacity;
        return 1.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}