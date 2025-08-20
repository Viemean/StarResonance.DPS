using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;
using StarResonance.DPS.Models;
using StarResonance.DPS.Services;

namespace StarResonance.DPS.ViewModels;

public class SnapshotViewModel : ObservableObject
{
    // 缓存并重用 JsonSerializerOptions 实例以优化性能
    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };


    private readonly INotificationService _notificationService;

    private bool _isInSnapshotMode;

    private string _loadedSnapshotFileName = "";

    public SnapshotViewModel(INotificationService notificationService)
    {
        _notificationService = notificationService;
        SaveSnapshotCommand = new AsyncRelayCommand(_ => SaveSnapshotAsync());
        LoadSnapshotCommand = new AsyncRelayCommand(_ => LoadSnapshotAsync());
        ExitSnapshotModeCommand = new RelayCommand(_ => ExitSnapshotMode());
    }

    public bool IsInSnapshotMode
    {
        get => _isInSnapshotMode;
        set => SetProperty(ref _isInSnapshotMode, value);
    }

    public string LoadedSnapshotFileName
    {
        get => _loadedSnapshotFileName;
        set => SetProperty(ref _loadedSnapshotFileName, value);
    }

    public ICommand SaveSnapshotCommand { get; }
    public ICommand LoadSnapshotCommand { get; }
    public ICommand ExitSnapshotModeCommand { get; }

    // 用于与 MainViewModel 通信的事件
    public event Func<Task<SnapshotData?>>? RequestDataForSave;
    public event Action<SnapshotData>? SnapshotLoaded;
    public event Action? ExitedSnapshotMode;

    private async Task SaveSnapshotAsync()
    {
        if (RequestDataForSave == null) return;

        var snapshot = await RequestDataForSave.Invoke();
        if (snapshot == null || !snapshot.Players.Any())
        {
            _notificationService.ShowNotification("没有数据可以保存");
            return;
        }

        try
        {
            var fileName = $"StarResonance.DPS-{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            var json = JsonSerializer.Serialize(snapshot, SnapshotSerializerOptions);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
            _notificationService.ShowNotification($"快照已保存: {fileName}");
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification($"保存失败: {ex.Message}");
        }
    }

    private async Task LoadSnapshotAsync()
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
        };

        if (openFileDialog.ShowDialog() != true) return;

        try
        {
            var json = await File.ReadAllTextAsync(openFileDialog.FileName);
            var snapshot = JsonSerializer.Deserialize<SnapshotData>(json);

            if (snapshot == null)
            {
                _notificationService.ShowNotification("无法解析快照文件");
                return;
            }

            SnapshotLoaded?.Invoke(snapshot);

            var fileNameToShow = Path.GetFileName(openFileDialog.FileName);
            if (fileNameToShow.StartsWith("StarResonance.DPS-"))
                fileNameToShow = fileNameToShow["StarResonance.DPS-".Length..];

            LoadedSnapshotFileName = fileNameToShow;
            IsInSnapshotMode = true;
            _notificationService.ShowNotification("快照加载成功");
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification($"加载失败: {ex.Message}");
        }
    }

    private void ExitSnapshotMode()
    {
        IsInSnapshotMode = false;
        LoadedSnapshotFileName = "";
        ExitedSnapshotMode?.Invoke();
    }
}