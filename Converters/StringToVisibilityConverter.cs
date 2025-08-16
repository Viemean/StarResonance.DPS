using System.Globalization;
using System.Windows;
using System.Windows.Data;
using StarResonance.DPS.Services;

namespace StarResonance.DPS.Converters;

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrEmpty(text))
        {
            return Visibility.Collapsed;
        }

        // 尝试从应用程序资源中获取本地化服务
        if (Application.Current.TryFindResource("LocalizationService") is not LocalizationService localization)
            return Visibility.Visible;
        // 如果文本内容与本地化的“不适用”文本相同，则隐藏
        return text.Equals(localization["NotApplicable"], StringComparison.OrdinalIgnoreCase) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}