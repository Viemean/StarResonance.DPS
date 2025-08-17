using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using StarResonance.DPS.ViewModels;

namespace StarResonance.DPS.Views;

public partial class MainWindow
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnMainWindowLoaded;
        Closing += OnMainWindowClosing;
        StateChanged += MainWindow_OnStateChanged;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);


    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsLocked)) return;
        if (_viewModel!.IsLocked)
            SetWindowClickThrough();
        else
            ClearWindowClickThrough();
    }

    private void SetWindowClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLongPtrW(hwnd, GwlExstyle);
        _ = SetWindowLongPtrW(hwnd, GwlExstyle, new IntPtr(extendedStyle.ToInt64() | WsExTransparent));
    }

    private void ClearWindowClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLongPtrW(hwnd, GwlExstyle);
        _ = SetWindowLongPtrW(hwnd, GwlExstyle, new IntPtr(extendedStyle.ToInt64() & ~WsExTransparent));
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
        if (WindowState == WindowState.Minimized) Hide();
    }

    private void TaskbarIcon_OnTrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        // 仅在窗口最小化或不可见时才执行恢复操作
        if (WindowState == WindowState.Minimized || !IsVisible) RestoreWindow();
    }

    private void RestoreMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        RestoreWindow();
    }

    private void MinimizeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void LockMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel!.ToggleLock();
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel = (MainViewModel)DataContext;
            await _viewModel.InitializeAsync();
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
        {
            if (_viewModel == null) return;
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