using System.Diagnostics;
using System.Windows;
using StarResonance.DPS.ViewModels;

namespace StarResonance.DPS.Views;

public partial class TitleBarView
{
    // 添加一个字段来持有 MainViewModel 的引用
    private MainViewModel? _viewModel;

    public TitleBarView()
    {
        InitializeComponent();
        // 当 DataContext 变化时，更新我们的 _viewModel 字段
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 将 DataContext 转换为 MainViewModel 类型并保存
        _viewModel = e.NewValue as MainViewModel;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        var parentWindow = Window.GetWindow(this);
        if (parentWindow != null) parentWindow.WindowState = WindowState.Minimized;
    }

    // --- 以下是从 MainWindow.xaml.cs 迁移过来的方法 ---

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel == null) return;
            try
            {
                await _viewModel.TogglePauseAsync();
            }
            catch (Exception error)
            {
                Debug.WriteLine($"Error on PauseButton_Click: {error.Message}");
            }
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
            if (_viewModel == null) return;
            try
            {
                await _viewModel.ResetDataAsync();
            }
            catch (Exception error)
            {
                Debug.WriteLine($"Error on ResetButton_Click: {error.Message}");
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Error on ResetButton_Click: {error.Message}");
        }
    }

    private async void AbortCountdownButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel == null) return;
            try
            {
                await _viewModel.AbortCountdownAsync();
            }
            catch (Exception error)
            {
                Debug.WriteLine($"Error on abort countdown: {error.Message}");
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Error on abort countdown: {error.Message}");
        }
    }

    private void CustomCountdownPopup_Opened(object sender, EventArgs e)
    {
        CountdownTextBox.Focus();
        CountdownTextBox.SelectAll();
    }
}