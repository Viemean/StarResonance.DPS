using System.Globalization;
using System.Reflection;
using System.Resources;
using StarResonance.DPS.ViewModels;
namespace StarResonance.DPS.Services;

public class LocalizationService : ObservableObject
{
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    public LocalizationService()
    {
        _resourceManager = new ResourceManager("StarResonance.DPS.Resources.Strings", Assembly.GetExecutingAssembly());
        _currentCulture = CultureInfo.CurrentUICulture;
        ApplyCulture(_currentCulture);
    }

    public string? this[string key] => _resourceManager.GetString(key, _currentCulture);

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (SetProperty(ref _currentCulture, value))
            {
                ApplyCulture(value);
                OnPropertyChanged(string.Empty);
            }
        }
    }

    public IEnumerable<CultureInfo> SupportedLanguages { get; } = new List<CultureInfo>
    {
        new("zh-CN"),
        new("en-US"),
        new("ja-JP")
    };

    private static void ApplyCulture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}