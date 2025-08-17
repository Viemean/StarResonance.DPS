using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StarResonance.DPS.Converters;

public class SortColumnVisibilityConverter : IMultiValueConverter
{
    /// <summary>
    ///     根据当前的排序列和参数决定一个UI元素（如排序箭头）是否可见。
    /// </summary>
    /// <param name="values">包含两个字符串的数组：[0]为当前活动排序列的名称, [1]为当前控件绑定的列名参数。</param>
    /// <param name="targetType">目标类型，应为 <see cref="Visibility" />。</param>
    /// <param name="parameter">未使用。</param>
    /// <param name="culture">未使用。</param>
    /// <returns>如果两个字符串忽略大小写相等，则返回 <see cref="Visibility.Visible" />，否则返回 <see cref="Visibility.Collapsed" />。</returns>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is [string sortColumn, string commandParameter])
            return sortColumn.Equals(commandParameter, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}