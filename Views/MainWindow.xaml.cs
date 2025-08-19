using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using StarResonance.DPS.Services;
using Microsoft.Extensions.DependencyInjection;
using StarResonance.DPS.ViewModels;

namespace StarResonance.DPS.Views;

public partial class MainWindow
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;

    private MainViewModel? _viewModel;
    private TrayIconService? _trayIconService;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnMainWindowLoaded;
        Closing += OnMainWindowClosing;
        StateChanged += MainWindow_OnStateChanged;
    }

    /// <summary>
    /// 获取指定窗口的扩展样式。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <param name="nIndex">要检索的值的偏移量，GWL_EXSTYLE 表示扩展样式。</param>
    /// <returns>函数的返回值是请求的值。</returns>
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    /// <summary>
    /// 修改指定窗口的扩展样式。
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <param name="nIndex">要设置的值的偏移量。</param>
    /// <param name="dwNewLong">新的值。</param>
    /// <returns>函数的返回值是指定偏移量的前一个值。</returns>
    [LibraryImport("user32.dll")]
    private static partial IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);


    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 仅当 IsLocked 属性发生变化时才执行操作
        if (e.PropertyName != nameof(MainViewModel.IsLocked)) return;

        // 根据 IsLocked 的值，启用或禁用鼠标穿透
        if (_viewModel!.IsLocked)
            SetWindowClickThrough();
        else
            ClearWindowClickThrough();
    }

    /// <summary>
    /// 启用窗口的鼠标穿透功能。
    /// 添加 WS_EX_TRANSPARENT 样式后，窗口将不再接收鼠标事件，
    /// 所有鼠标点击都会“穿透”到下方的窗口。
    /// </summary>
    private void SetWindowClickThrough()
    {
        // 获取当前WPF窗口的句柄(HWND)
        var hwnd = new WindowInteropHelper(this).Handle;

        //  获取当前窗口的扩展样式
        var extendedStyle = GetWindowLongPtrW(hwnd, GwlExstyle);

        // 这样可以在保留原有样式的基础上，增加鼠标穿透特性
        var newExtendedStyle = extendedStyle.ToInt64() | WsExTransparent;

        // 将新的样式应用到窗口
        _ = SetWindowLongPtrW(hwnd, GwlExstyle, new IntPtr(newExtendedStyle));
    }

    /// <summary>
    /// 禁用窗口的鼠标穿透功能，恢复正常交互。
    /// 移除 WS_EX_TRANSPARENT 样式后，窗口将能正常响应鼠标事件。
    /// </summary>
    private void ClearWindowClickThrough()
    {
        // 获取当前WPF窗口的句柄(HWND)
        var hwnd = new WindowInteropHelper(this).Handle;

        // 获取当前窗口的扩展样式
        var extendedStyle = GetWindowLongPtrW(hwnd, GwlExstyle);

        var newExtendedStyle = extendedStyle.ToInt64() & ~WsExTransparent;

        _ = SetWindowLongPtrW(hwnd, GwlExstyle, new IntPtr(newExtendedStyle));
    }

    private async void Player_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is ListViewItem { DataContext: PlayerViewModel player })
                await _viewModel!.TogglePlayerExpansionCommand.ExecuteAsync(player);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error on player double click: {ex.Message}");
        }
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel = (MainViewModel)DataContext;
            // 订阅ViewModel的属性变更通知，这是连接视图逻辑和视图模型状态的关键
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            await _viewModel.InitializeAsync();
            // 初始化托盘图标服务 !!!
            _trayIconService = ((App)Application.Current).Services.GetService<TrayIconService>();
            _trayIconService?.Initialize();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error on main window loaded: {ex.Message}");
            MessageBox.Show($"应用程序加载失败: {ex.Message}", "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        try
        {   //销毁托盘图标
            _trayIconService?.Dispose();
            if (_viewModel == null) return;
            // 在窗口关闭时取消订阅，防止内存泄漏
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            await _viewModel.PauseOnExitAsync();
            await _viewModel.DisposeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error on main window closing: {ex.Message}");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source is ButtonBase) return;
        if (_viewModel is { IsSettingsVisible: true })
        {
            _viewModel.IsSettingsVisible = false;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel!.ToggleLock();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel!.ToggleSettings();
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel!.TogglePauseAsync();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Error on PauseButton_Click: {error.Message}");
        }
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel!.ResetDataAsync();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Error on ResetButton_Click: {error.Message}");
        }
    }


    private void CustomCountdownPopup_Opened(object sender, EventArgs e)
    {
        CountdownTextBox.Focus();
        CountdownTextBox.SelectAll();
    }

    private async void AbortCountdownButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel!.AbortCountdownAsync();
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Error on abort countdown: {error.Message}");
        }
    }
}