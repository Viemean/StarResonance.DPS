using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace StarResonance.DPS.Converters;

public class FontFamilyNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FontFamily fontFamily) return "Unknown Font";

        // 尝试获取当前UI语言对应的字体名称
        var lang = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        return fontFamily.FamilyNames.TryGetValue(lang, out var name)
            ? name
            : fontFamily.Source;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}