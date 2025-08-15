using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StarResonance.DPS.Converters;

public class SortColumnVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // 检查传入的值是否是两个非空的字符串
        if (values is [string sortColumn, string commandParameter, ..])
            // 如果是，则进行比较并返回结果
            return sortColumn.Equals(commandParameter, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

        // 对于任何其他情况（参数数量不对、类型不对、有null值），都直接返回折叠
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        // 这个转换器不需要反向转换，所以直接抛出异常
        throw new NotImplementedException();
    }
}