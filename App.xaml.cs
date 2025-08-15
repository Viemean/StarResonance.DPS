using System.Globalization;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using StarResonance.DPS.Services;
using StarResonance.DPS.ViewModels;
using StarResonance.DPS.Views;

namespace StarResonance.DPS;

public partial class App
{
    private const string AppMutexName = "StarResonance.DPS-SingleInstanceMutex";
    private Mutex? _appMutex;

    public new static App Current => (App)Application.Current;

    private IServiceProvider Services { get; } = ConfigureServices();

    /// <summary>
    ///     配置应用程序的服务。
    /// </summary>
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // 注册服务 (Services)
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ApiService>();
        // 将 MainViewModel 自身注册为 INotificationService 的实现
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<MainViewModel>());


        // 注册视图 (Views)
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _appMutex = new Mutex(true, AppMutexName, out var isNewInstance);

        if (!isNewInstance)
        {
            MessageBox.Show("程序已经在运行中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var localizationService = Services.GetRequiredService<LocalizationService>();
        // 初始化本地化服务
        var currentCulture = CultureInfo.CurrentUICulture;
        var supportedCultures = localizationService.SupportedLanguages.ToList();
        localizationService.CurrentCulture = supportedCultures.All(c => c.Name != currentCulture.Name)
            ? supportedCultures.First(c => c.Name == "zh-CN")
            : currentCulture;

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = Services.GetRequiredService<MainViewModel>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _appMutex?.ReleaseMutex();
        _appMutex?.Dispose();
        base.OnExit(e);
    }
}