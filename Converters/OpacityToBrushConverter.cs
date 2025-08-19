using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StarResonance.DPS.Converters;

public class OpacityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double opacity)
            return Brushes.Transparent;

        // 默认基础颜色为黑色
        var baseColor = Colors.Black;
        if (parameter is string colorString)
        {
            try
            {
                // 尝试从参数解析颜色
                baseColor = (Color)ColorConverter.ConvertFromString(colorString);
            }
            catch
            {
                // 解析失败则使用默认黑色
            }
        }
        
        // 确保透明度值在 0.0 到 1.0 之间
        opacity = Math.Clamp(opacity, 0.0, 1.0);

        // 计算最终的 alpha (透明度) 通道值
        var alpha = (byte)(opacity * 255);

        // 创建并返回一个新地带有透明度的画刷
        var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        brush.Freeze(); // 冻结画刷以提升性能
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}