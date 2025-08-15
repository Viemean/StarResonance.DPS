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
    private bool _isMinimized;
    private double _restoreTop, _restoreLeft, _restoreHeight, _restoreWidth;
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnMainWindowLoaded;
        Closing += OnMainWindowClosing;
        StateChanged += MainWindow_OnStateChanged;
    }

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
        var extendedStyle = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(hwnd, GwlExstyle, extendedStyle | WsExTransparent);
    }

    private void ClearWindowClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(hwnd, GwlExstyle, extendedStyle & ~WsExTransparent);
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
            if (!_isMinimized)
            {
                _restoreTop = Top;
                _restoreLeft = Left;
                _restoreHeight = Height;
                _restoreWidth = Width;
                _isMinimized = true;
            }

            Hide();
        }
        else if (WindowState == WindowState.Normal)
        {
            _isMinimized = false;
        }
    }

    private void TaskbarIcon_OnTrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        RestoreWindow();
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
        Top = _restoreTop;
        Left = _restoreLeft;
        Height = _restoreHeight;
        Width = _restoreWidth;
        Activate();
    }

    private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel = (MainViewModel)DataContext;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _restoreTop = Top;
            _restoreLeft = Left;
            _restoreHeight = Height;
            _restoreWidth = Width;
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error on main window loaded: {ex.Message}");
            MessageBox.Show($"应用程序加载失败: {ex.Message}", "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

// File: Views/MainWindow.xaml.cs

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
        await _viewModel!.TogglePauseAsync();
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel!.ResetDataAsync();
    }


    private void CustomCountdownPopup_Opened(object sender, EventArgs e)
    {
        CountdownTextBox.Focus();
        CountdownTextBox.SelectAll();
    }

    private async void AbortCountdownButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel!.AbortCountdownAsync();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}