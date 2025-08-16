using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StarResonance.DPS.Models;
using StarResonance.DPS.Services;

namespace StarResonance.DPS.ViewModels;

public class AppState
{
    public int ElapsedSeconds { get; init; }
    public bool IsFightActive { get; init; }
    public double WindowOpacity { get; init; } = 0.85;
    public double FontSize { get; init; } = 14;
    public string FontFamilySource { get; init; } = "Microsoft YaHei";
    public bool IsSmartIdleModeEnabled { get; init; }
    public string BackendUrl { get; init; } = "ws://localhost:8989";
    public int UiUpdateInterval { get; init; } = 500;
    public string? CultureName { get; init; }
    public bool PauseOnExit { get; init; } = true;
    public bool PauseOnSnapshot { get; init; } = true;


    //用于保存排序规则
    public string? SortColumn { get; init; }
    public ListSortDirection SortDirection { get; init; }
}

/// <summary>
///     主窗口的视图模型，负责处理所有UI逻辑、数据状态和与服务的交互。
/// </summary>
public partial class MainViewModel : ObservableObject, IAsyncDisposable, INotificationService
{
    //闲置时长
    private const int IdleTimeoutSeconds = 30;

    private static readonly Dictionary<string, Func<PlayerViewModel, IComparable>> Sorters = new()
    {
        [SortableColumns.Name] = p => p.DisplayName,
        [SortableColumns.FightPoint] = p => p.FightPoint,
        [SortableColumns.Profession] = p => p.Profession,
        [SortableColumns.TotalDamage] = p => p.TotalDamage,
        [SortableColumns.TotalHealing] = p => p.TotalHealing,
        [SortableColumns.TotalDps] = p => p.TotalDps,
        [SortableColumns.TotalHps] = p => p.TotalHps,
        [SortableColumns.TakenDamage] = p => p.TakenDamage
    };

    // 新增：缓存并重用 JsonSerializerOptions 实例以优化性能
    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ApiService _apiService;
    private readonly DispatcherTimer _clockTimer;
    private readonly object _dataLock = new();
    private readonly DispatcherTimer _fightTimer;
    private readonly DispatcherTimer _notificationTimer;

    private readonly PropertyChangedEventHandler _onLocalizationPropertyChanged;
    private readonly Dictionary<string, PlayerViewModel> _playerCache = new();
    private readonly string _stateFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dps_state.json");
    private readonly DispatcherTimer _stateSaveTimer;

    private readonly DispatcherTimer _proactiveDataFetchTimer; //用于主动获取数据的定时器
    private readonly Dictionary<string, DateTime> _playerEntryTimes = new(); //追踪玩家首次出现的时间
    private readonly DispatcherTimer _listRefreshTimer; //用于低频刷新整个列表排序和百分比的定时器

    private readonly DispatcherTimer _uiUpdateTimer;

    [ObservableProperty] private string _backendUrl = "ws://localhost:8989";
    [ObservableProperty] private Brush _connectionStatusColor = Brushes.Orange;
    [ObservableProperty] private string _connectionStatusText = "正在连接...";
    [ObservableProperty] private string _countdownText = "倒计时";
    private TimeSpan _countdownTimeLeft;
    private DispatcherTimer? _countdownTimer;
    [ObservableProperty] private string _currentTime = DateTime.Now.ToLongTimeString();
    [ObservableProperty] private string _customCountdownMinutes = "10";
    private int _elapsedSeconds;
    private PlayerViewModel? _expandedPlayer;
    [ObservableProperty] private string _fightDurationText = "0:00";
    [ObservableProperty] private double _fontSize = 14;
    [ObservableProperty] private bool _isCountdownOptionsPopupOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountdownRunningVisibility))]
    [NotifyPropertyChangedFor(nameof(RealtimeModeVisibility))]
    // 倒计时按钮的可见性也受此影响
    private bool _isCountdownRunning;

    [ObservableProperty] private bool _isCustomCountdownPopupOpen;
    private bool _isFightActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SnapshotModeVisibility))]
    [NotifyPropertyChangedFor(nameof(RealtimeModeVisibility))]
    private bool _isInSnapshotMode;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsHitTestVisible))]
    private bool _isLocked;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NotificationVisibility))]
    private bool _isNotificationVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseStatusVisibility))]
    [NotifyPropertyChangedFor(nameof(FightDurationVisibility))]
    private bool _isPaused;

    [ObservableProperty] private bool _isPauseOnExitEnabled = true;

    //控制进入快照时是否暂停服务的选项，默认为 true
    [ObservableProperty] private bool _isPauseOnSnapshotEnabled = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SettingsVisibility))]
    private bool _isSettingsVisible;

    [ObservableProperty] private bool _isSmartIdleModeEnabled;
    private DateTime _lastCombatActivityTime;
    private ApiResponse? _latestReceivedData;

    [ObservableProperty] private string _loadedSnapshotFileName = "";
    [ObservableProperty] private string _lockIconContent = "🔓";
    [ObservableProperty] private string _lockMenuHeaderText = "锁定";
    [ObservableProperty] private string _notificationText = "";
    [ObservableProperty] private string _pauseButtonText = "暂停";
    [ObservableProperty] private Brush _pauseStatusColor = Brushes.LimeGreen;
    [ObservableProperty] private ObservableCollection<PlayerViewModel> _players = [];
    [ObservableProperty] private FontFamily _selectedFontFamily;
    [ObservableProperty] private string? _sortColumn;
    [ObservableProperty] private ListSortDirection _sortDirection = ListSortDirection.Descending;
    [ObservableProperty] private string? _takenDamageSumTooltip;
    [ObservableProperty] private string? _totalDamageSumTooltip;
    [ObservableProperty] private string? _totalDpsSumTooltip;
    [ObservableProperty] private string? _totalHealingSumTooltip;
    [ObservableProperty] private string? _totalHpsSumTooltip;
    [ObservableProperty] private int _uiUpdateInterval = 500;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FontOpacity))]
    private double _windowOpacity = 0.85;

    //用于替换 IValueConverter 的计算属性
    public Visibility SnapshotModeVisibility => IsInSnapshotMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RealtimeModeVisibility => IsInSnapshotMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CountdownRunningVisibility => IsCountdownRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PauseStatusVisibility => IsPaused ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FightDurationVisibility => !IsPaused ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NotificationVisibility => IsNotificationVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVisibility => IsSettingsVisible ? Visibility.Visible : Visibility.Collapsed;
    public bool IsHitTestVisible => !IsLocked;
    public double FontOpacity => WindowOpacity < 0.8 ? 0.8 : WindowOpacity;

    public MainViewModel(ApiService apiService, LocalizationService localizationService)
    {
        _apiService = apiService;
        Localization = localizationService;
        SystemFonts = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
        _selectedFontFamily = new FontFamily("Microsoft YaHei");

        _onLocalizationPropertyChanged = (_, e) =>
        {
            OnPropertyChanged(string.IsNullOrEmpty(e.PropertyName) ? string.Empty : nameof(Localization));
        };


        Localization.PropertyChanged += _onLocalizationPropertyChanged;
        BindingOperations.EnableCollectionSynchronization(Players, _dataLock); //启用线程安全的集合绑定 

        _apiService.DataReceived += OnDataReceived;
        _apiService.OnConnected += OnApiServiceConnected;
        _apiService.OnDisconnected += OnApiServiceDisconnected;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => CurrentTime = DateTime.Now.ToLongTimeString();
        _clockTimer.Start();
        _uiUpdateTimer = new DispatcherTimer();
        _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
        UpdateTimerInterval();
        _uiUpdateTimer.Start();
        _fightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fightTimer.Tick += FightTimer_Tick;
        _stateSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _stateSaveTimer.Tick += (_, _) => SaveState();
        _stateSaveTimer.Start();
        SortColumn = "TotalDps";
        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _notificationTimer.Tick += (_, _) => IsNotificationVisible = false;

        // 将定时器间隔从1秒延长到3秒，降低检查频率
        _proactiveDataFetchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _proactiveDataFetchTimer.Tick += ProactiveDataFetchTimer_Tick;
        _proactiveDataFetchTimer.Start();

        //初始化列表整体刷新定时器，每2秒执行一次
        _listRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _listRefreshTimer.Tick += (_, _) => UpdatePlayerList();
        _listRefreshTimer.Start();

        ConnectionStatusText = Localization["Connecting"] ?? "正在连接...";
        PauseButtonText = Localization["Pause"] ?? "暂停";
    }

    public IEnumerable<FontFamily> SystemFonts { get; }

    public LocalizationService Localization { get; }
    public string AccurateCritDamageLabelText => Localization["AccurateCritDamageLabel"] ?? "精确暴伤加成 (加权):";


    //玩家总数
    public int PlayerCount => Players.Count;

    public async ValueTask DisposeAsync()
    {
        _apiService.DataReceived -= OnDataReceived;
        _apiService.OnConnected -= OnApiServiceConnected;
        _apiService.OnDisconnected -= OnApiServiceDisconnected;
        Localization.PropertyChanged -= _onLocalizationPropertyChanged;

        _clockTimer.Stop();
        _uiUpdateTimer.Stop();
        _fightTimer.Stop();
        _countdownTimer?.Stop();
        _notificationTimer.Stop();
        _stateSaveTimer.Stop();
        _proactiveDataFetchTimer.Stop(); //停止新定时器
        _listRefreshTimer.Stop(); // 停止列表刷新定时器
        SaveState();
        await _apiService.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public void ShowNotification(string message)
    {
        NotificationText = message;
        IsNotificationVisible = true;
        _notificationTimer.Stop();
        _notificationTimer.Start();
    }

    private void OnApiServiceConnected()
    {
        ConnectionStatusText = Localization["Connected"] ?? "已连接";
        ConnectionStatusColor = Brushes.LimeGreen;
    }

    private void OnApiServiceDisconnected()
    {
        ConnectionStatusText = Localization["Disconnected"] ?? "已断开";
        ConnectionStatusColor = Brushes.Red;
    }

    /// <summary>
    ///     打开倒计时选项弹窗。
    /// </summary>
    [RelayCommand]
    private void OpenCountdownOptions()
    {
        IsCountdownOptionsPopupOpen = true;
    }

    /// <summary>
    ///     打开自定义倒计时弹窗。
    /// </summary>
    [RelayCommand]
    private void OpenCustomCountdown()
    {
        IsCountdownOptionsPopupOpen = false;
        IsCustomCountdownPopupOpen = true;
    }

    /// <summary>
    ///     开始一个自定义时间的倒计时。
    /// </summary>
    [RelayCommand]
    private async Task StartCustomCountdown()
    {
        try
        {
            if (double.TryParse(CustomCountdownMinutes, out var minutes) && minutes > 0)
                await StartCountdown(((int)(minutes * 60)).ToString());
            else
                ShowNotification("请输入有效的分钟数");
        }
        finally
        {
            IsCustomCountdownPopupOpen = false;
        }
    }

    /// <summary>
    ///     取消自定义倒计时并关闭弹窗。
    /// </summary>
    [RelayCommand]
    private void CancelCustomCountdown()
    {
        IsCustomCountdownPopupOpen = false;
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                var json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<AppState>(json);
                if (state != null)
                {
                    _elapsedSeconds = state.ElapsedSeconds;
                    _isFightActive = state.IsFightActive;
                    var timeSpan = TimeSpan.FromSeconds(_elapsedSeconds);
                    FightDurationText = timeSpan.TotalHours >= 1
                        ? timeSpan.ToString(@"h\:mm\:ss")
                        : timeSpan.ToString(@"m\:ss");
                    if (_isFightActive && !IsPaused) _fightTimer.Start();
                    WindowOpacity = state.WindowOpacity;
                    FontSize = state.FontSize;
                    var loadedFont = SystemFonts.FirstOrDefault(f =>
                        f.Source.Equals(state.FontFamilySource, StringComparison.OrdinalIgnoreCase));
                    SelectedFontFamily = loadedFont ?? new FontFamily("Microsoft YaHei");
                    IsSmartIdleModeEnabled = state.IsSmartIdleModeEnabled;
                    BackendUrl = state.BackendUrl;
                    UiUpdateInterval = state.UiUpdateInterval;
                    IsPauseOnExitEnabled = state.PauseOnExit;
                    IsPauseOnSnapshotEnabled = state.PauseOnSnapshot;

                    //加载已保存的排序设置
                    if (!string.IsNullOrEmpty(state.SortColumn))
                    {
                        SortColumn = state.SortColumn;
                        SortDirection = state.SortDirection;
                    }

                    if (!string.IsNullOrEmpty(state.CultureName))
                        Localization.CurrentCulture = new CultureInfo(state.CultureName);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load state: {ex.Message}");
        }

        SaveState();
    }

    private void SaveState()
    {
        try
        {
            var state = new AppState
            {
                ElapsedSeconds = _elapsedSeconds,
                IsFightActive = _isFightActive,
                WindowOpacity = WindowOpacity,
                FontSize = FontSize,
                FontFamilySource = SelectedFontFamily.Source,
                IsSmartIdleModeEnabled = IsSmartIdleModeEnabled,
                BackendUrl = BackendUrl,
                UiUpdateInterval = UiUpdateInterval,
                CultureName = Localization.CurrentCulture.Name,
                PauseOnExit = IsPauseOnExitEnabled,
                PauseOnSnapshot = IsPauseOnSnapshotEnabled,
                //保存当前的排序设置
                SortColumn = SortColumn,
                SortDirection = SortDirection
            };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save state: {ex.Message}");
        }
    }

    private void FightTimer_Tick(object? sender, EventArgs e)
    {
        // 检查距离上次实际战斗数据变化是否在2秒窗口内
        if (!((DateTime.UtcNow - _lastCombatActivityTime).TotalSeconds <= 2)) return;
        // 只有在活跃期内，才累加秒数并更新UI
        _elapsedSeconds++;
        var timeSpan = TimeSpan.FromSeconds(_elapsedSeconds);
        FightDurationText = timeSpan.TotalHours >= 1
            ? timeSpan.ToString(@"h\:mm\:ss")
            : timeSpan.ToString(@"m\:ss");
        // 如果超过2秒不活跃，则不执行任何操作，计时器会平滑地“冻结”。
    }

    partial void OnIsLockedChanged(bool value)
    {
        LockMenuHeaderText = value ? Localization["Unlock"] ?? "解锁" : Localization["Lock"] ?? "锁定";
        LockIconContent = value ? "🔒" : "🔓";
        if (value) ShowNotification("可通过托盘图标右键解锁");
    }

    partial void OnIsPausedChanged(bool value)
    {
        PauseStatusColor = value ? Brushes.Red : Brushes.LimeGreen;
        PauseButtonText = value ? Localization["Resume"] ?? "恢复" : Localization["Pause"] ?? "暂停";
    }

    partial void OnUiUpdateIntervalChanged(int value)
    {
        _ = value;
        UpdateTimerInterval();
    }

    private void UpdateColumnTotals()
    {
        var playersForTotals = IsSmartIdleModeEnabled ? Players.Where(p => !p.IsIdle).ToList() : Players.ToList();
        if (playersForTotals.Count == 0)
        {
            TotalDamageSumTooltip = null;
            TotalHealingSumTooltip = null;
            TotalDpsSumTooltip = null;
            TotalHpsSumTooltip = null;
            TakenDamageSumTooltip = null;
            return;
        }

        var totalDamage = playersForTotals.Sum(p => p.TotalDamage);
        var totalHealing = playersForTotals.Sum(p => p.TotalHealing);
        var totalDps = playersForTotals.Sum(p => p.TotalDps);
        var totalHps = playersForTotals.Sum(p => p.TotalHps);
        var totalTakenDamage = playersForTotals.Sum(p => p.TakenDamage);
        TotalDamageSumTooltip =
            $"{Localization["TotalDamage"] ?? "总伤害"}: {FormatNumber(totalDamage)}\n{Localization["TotalDamage"] ?? "总伤害"}: {totalDamage:N0}";
        TotalHealingSumTooltip =
            $"{Localization["TotalHealing"] ?? "总治疗"}: {FormatNumber(totalHealing)}\n{Localization["TotalHealing"] ?? "总治疗"}: {totalHealing:N0}";
        TotalDpsSumTooltip =
            $"{Localization["TotalDPS"] ?? "总DPS"}: {FormatNumber(totalDps)}\n{Localization["TotalDPS"] ?? "总DPS"}: {totalDps:N2}";
        TotalHpsSumTooltip =
            $"{Localization["TotalHPS"] ?? "总HPS"}: {FormatNumber(totalHps)}\n{Localization["TotalHPS"] ?? "总HPS"}: {totalHps:N2}";
        TakenDamageSumTooltip =
            $"{Localization["TakenDamage"] ?? "承伤"}: {FormatNumber(totalTakenDamage)}\n{Localization["TakenDamage"] ?? "承伤"}: {totalTakenDamage:N0}";
    }

    public static string FormatNumber(double num)
    {
        return num switch
        {
            >= 1_000_000_000 => $"{num / 1_000_000_000:F2}G",
            >= 1_000_000 => $"{num / 1_000_000:F2}M",
            >= 10_000 => $"{num / 10_000:F1}W", // [新增] “万”单位格式
            >= 1_000 => $"{num / 1_000:F2}K",
            _ => num.ToString("F0")
        };
    }

    [RelayCommand]
    private async Task ConnectToBackendAsync()
    {
        ConnectionStatusText = Localization["Connecting"] ?? "正在连接...";
        ConnectionStatusColor = Brushes.Orange;
        await _apiService.ReinitializeAsync(BackendUrl);
        var isRunning = await _apiService.CheckServiceRunningAsync();
        if (isRunning)
        {
            await ResetDataAsync();
            await _apiService.ConnectAsync();
            ShowNotification("连接成功！");
        }
        else
        {
            ConnectionStatusText = Localization["Disconnected"] ?? "已断开";
            ConnectionStatusColor = Brushes.Red;
            ShowNotification("连接失败, 请检查后端地址或服务状态");
        }
    }

    [RelayCommand]
    private void ExportToCsv()
    {
        if (!Players.Any())
        {
            ShowNotification("没有数据可以导出");
            return;
        }

        var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var confirmationMessage = $"是否要将当前列表中的所有数据导出为CSV文件？\n\n文件将保存在:\n{currentDirectory}";
        var result = MessageBox.Show(confirmationMessage, "导出确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            var fileName = $"StarResonanceDPS_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            var filePath = Path.Combine(currentDirectory, fileName);
            var sb = new StringBuilder();
            foreach (var player in Players.OrderBy(p => p.Rank)) sb.AppendLine(player.CopyableString);
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            ShowNotification($"成功导出到: {filePath}");
        }
        catch (Exception ex)
        {
            ShowNotification($"导出失败: {ex.Message}");
            Debug.WriteLine($"CSV Export failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task TogglePlayerExpansion(PlayerViewModel player)
    {
        // 如果点击的玩家已经展开，则将其折叠
        if (player.IsExpanded)
        {
            player.IsExpanded = false;
            _expandedPlayer = null;
            return;
        }

        // 如果当前有其他玩家处于展开状态，先将其折叠
        if (_expandedPlayer is not null && _expandedPlayer != player)
        {
            _expandedPlayer.IsExpanded = false;
        }

        // 展开被点击的玩家
        player.IsExpanded = true;
        _expandedPlayer = player;

        // 无论是主动获取的还是第一次展开，都强制刷新一次以获取最新数据
        await FetchAndProcessSkillDataAsync(player);
    }

    private async Task FetchAndProcessSkillDataAsync(PlayerViewModel player)
    {
        // 如果处于快照模式，则不主动获取数据
        if (IsInSnapshotMode) return;

        try
        {
            player.IsFetchingSkillData = true;
            // 在UI线程上更新加载状态
            await Application.Current.Dispatcher.InvokeAsync(() => player.SetLoadingSkills(true));

            var skillDataResponse = await _apiService.GetSkillDataAsync(player.Uid);
            if (skillDataResponse?.Data?.Skills != null)
            {
                player.RawSkillData = skillDataResponse.Data;
                player.LastSkillDataFetchTime = DateTime.UtcNow;

                var playerTotalValue = player.TotalDamage + player.TotalHealing;
                var skills = skillDataResponse.Data.Skills.Values
                    .OrderByDescending(s => s.TotalDamage)
                    .Take(6)
                    .Select(s => new SkillViewModel(s, playerTotalValue));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    player.Skills.Clear();
                    foreach (var skillVm in skills)
                    {
                        player.Skills.Add(skillVm);
                    }
                });

                player.CalculateAccurateCritDamage(skillDataResponse.Data.Skills);
                player.CalculateAccurateCritHealing(skillDataResponse.Data.Skills);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load skill data for {player.Name}: {ex.Message}");
        }
        finally
        {
            player.IsFetchingSkillData = false;
            await Application.Current.Dispatcher.InvokeAsync(() => player.SetLoadingSkills(false));
        }
    }

    [RelayCommand]
    private async Task SaveSnapshotAsync()
    {
        if (!Players.Any())
        {
            ShowNotification("没有数据可以保存");
            return;
        }

        // 确保所有玩家的技能数据都已获取
        foreach (var player in _playerCache.Values.Where(p => p.RawSkillData == null))
        {
            var skillData = await _apiService.GetSkillDataAsync(player.Uid);
            if (skillData?.Data != null) player.RawSkillData = skillData.Data;
        }

        var snapshot = new SnapshotData
        {
            ElapsedSeconds = _elapsedSeconds,
            Players = _playerCache.Values.Select(p => new PlayerSnapshot
            {
                UserData = p.UserData!,
                SkillData = p.RawSkillData
            }).ToList()
        };

        try
        {
            var fileName = $"StarResonance.DPS-{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

            var json = JsonSerializer.Serialize(snapshot, SnapshotSerializerOptions);

            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
            ShowNotification($"快照已保存: {fileName}");
        }
        catch (Exception ex)
        {
            ShowNotification($"保存失败: {ex.Message}");
        }
    }

    [RelayCommand]
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
                ShowNotification("无法解析快照文件");
                return;
            }

            if (IsPauseOnSnapshotEnabled) await _apiService.SetPauseStateAsync(true);

            _apiService.DataReceived -= OnDataReceived;

            _fightTimer.Stop();
            _uiUpdateTimer.Stop(); // 停止UI刷新计时器

            _elapsedSeconds = snapshot.ElapsedSeconds;
            var timeSpan = TimeSpan.FromSeconds(_elapsedSeconds);
            FightDurationText =
                timeSpan.TotalHours >= 1 ? timeSpan.ToString(@"h\:mm\:ss") : timeSpan.ToString(@"m\:ss");
            _isFightActive = true;

            Players.Clear();
            _playerCache.Clear();

            foreach (var playerSnapshot in snapshot.Players)
            {
                var playerVm = new PlayerViewModel(playerSnapshot, FightDurationText, Localization, this);
                var key = playerVm.Uid.ToString();
                _playerCache.Add(key, playerVm);
                Players.Add(playerVm);
            }

            UpdatePlayerList();

            var fileNameToShow = Path.GetFileName(openFileDialog.FileName);
            if (fileNameToShow.StartsWith("StarResonance.DPS-"))
                fileNameToShow = fileNameToShow["StarResonance.DPS-".Length..];

            LoadedSnapshotFileName = fileNameToShow;
            IsInSnapshotMode = true;
            ShowNotification("快照加载成功");
        }
        catch (Exception ex)
        {
            ShowNotification($"加载失败: {ex.Message}");
        }
    }

    private void UpdateTimerInterval()
    {
        _uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(UiUpdateInterval);
    }

    private void OnDataReceived(ApiResponse data)
    {
        lock (_dataLock)
        {
            _latestReceivedData = data;
        }
    }

    private void UiUpdateTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            ApiResponse? dataToProcess;
            lock (_dataLock)
            {
                dataToProcess = _latestReceivedData;
                _latestReceivedData = null;
            }

            if (dataToProcess != null)
            {
                var listChanged = ProcessDataChanges(dataToProcess);
                // 仅当有玩家“唤醒”或列表结构发生变化时，才立即刷新列表
                if (listChanged)
                {
                    UpdatePlayerList();
                }
            }

            // 无论有无新数据，都执行一次闲置状态检查
            CheckForIdlePlayers();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred in UiUpdateTimer_Tick: {ex.Message}");
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            LoadState();
            var initialData = await _apiService.GetInitialDataAsync();
            if (initialData != null) await ProcessData(initialData);
            var (success, isPaused) = await _apiService.GetPauseStateAsync();
            if (success) IsPaused = isPaused;
            await _apiService.ConnectAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Initialization failed: {ex.Message}");
            ShowNotification("初始化失败");
        }
    }

    /// <summary>
    ///     处理从API服务接收到的实时数据。
    /// </summary>
    /// <param name="data">从服务器接收到的最新API响应数据。</param>
    private async Task ProcessData(ApiResponse data)
    {
        try
        {
            // 战斗首次启动逻辑
            if (!_isFightActive && !IsPaused)
                if (data.User.Values.Any(u => u.TotalDamage.Total > 0 || u.TotalHealing.Total > 0))
                {
                    _isFightActive = true;
                    _lastCombatActivityTime = DateTime.UtcNow;
                    // 启动计时器是UI操作，需要异步调度
                    await Application.Current.Dispatcher.InvokeAsync(() => _fightTimer.Start());
                }

            var activeKeys = new HashSet<string>(data.User.Keys);

            // 处理离开的玩家
            foreach (var existingKey in _playerCache.Keys.Except(activeKeys).ToList())
            {
                if (!_playerCache.TryGetValue(existingKey, out var playerToRemove)) continue;
                Players.Remove(playerToRemove);
                _playerCache.Remove(existingKey);
                _playerEntryTimes.Remove(existingKey); // 新增：移除玩家的进入时间记录
            }

            // 添加或更新玩家数据
            foreach (var (key, userData) in data.User)
            {
                long.TryParse(key, out var uid);
                if (!_playerCache.TryGetValue(key, out var playerVm))
                {
                    playerVm = new PlayerViewModel(uid, Localization, this);
                    _playerCache.Add(key, playerVm);
                    Players.Add(playerVm);
                    _playerEntryTimes.Add(key, DateTime.UtcNow); // 新增：记录新玩家的进入时间
                }

                // 更新闲置模式所需的时间戳
                if (Math.Abs(playerVm.TotalDamage - userData.TotalDamage.Total) > 1E-6 ||
                    Math.Abs(playerVm.TotalHealing - userData.TotalHealing.Total) > 1E-6)
                    playerVm.LastActiveTime = DateTime.UtcNow;

                playerVm.Update(userData, playerVm.Rank, FightDurationText);
            }

            //将最终的UI更新操作异步调度到UI线程
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                UpdatePlayerList();
                OnPropertyChanged(nameof(PlayerCount));
            });
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Error processing data: {e}");
        }
    }

    private bool ProcessDataChanges(ApiResponse data)
    {
        var listStructureChanged = false;
        var aPlayerWokeUp = false;

        var activeKeys = new HashSet<string>(data.User.Keys);

        // 处理离开的玩家
        var removedKeys = _playerCache.Keys.Except(activeKeys).ToList();
        if (removedKeys.Count > 0)
        {
            listStructureChanged = true;
            foreach (var key in removedKeys)
            {
                if (_playerCache.TryGetValue(key, out var playerToRemove))
                {
                    Players.Remove(playerToRemove);
                    _playerCache.Remove(key);
                    _playerEntryTimes.Remove(key);
                }
            }
        }

        // 添加或更新玩家数据
        foreach (var (key, userData) in data.User)
        {
            long.TryParse(key, out var uid);

            // 使用 if-else 结构明确 playerVm 的作用域
            if (!_playerCache.TryGetValue(key, out var playerVm))
            {
                // Case 1: 新玩家
                listStructureChanged = true;
                playerVm = new PlayerViewModel(uid, Localization, this);
                _playerCache.Add(key, playerVm);
                Players.Add(playerVm);
                _playerEntryTimes.Add(key, DateTime.UtcNow);

                // 新玩家的数据肯定有变化，直接更新活跃时间
                playerVm.LastActiveTime = DateTime.UtcNow;

                // 新玩家的 IsIdle 默认为 false，无需检查唤醒
            }
            else
            {
                // Case 2: 已有玩家
                // 检查数据是否有实际变化
                if (Math.Abs(playerVm.TotalDamage - userData.TotalDamage.Total) > 1E-6 ||
                    Math.Abs(playerVm.TotalHealing - userData.TotalHealing.Total) > 1E-6 ||
                    Math.Abs(playerVm.TakenDamage - userData.TakenDamage) > 1E-6)
                {
                    playerVm.LastActiveTime = DateTime.UtcNow;

                    // 检查玩家是否从闲置状态被唤醒
                    if (playerVm.IsIdle)
                    {
                        playerVm.IsIdle = false;
                        aPlayerWokeUp = true; // 标记需要立即重排
                    }
                }
            }

            // 在这里，无论是新玩家还是旧玩家，playerVm 都绝对不为 null
            playerVm.Update(userData, playerVm.Rank, FightDurationText);
        }

        if (listStructureChanged)
        {
            OnPropertyChanged(nameof(PlayerCount));
        }

        // 如果有玩家被唤醒或列表结构变化，返回true以触发立即刷新
        return aPlayerWokeUp || listStructureChanged;
    }

    /// <summary>
    ///     根据当前排序设置，对玩家列表进行排序、计算百分比并更新UI。
    ///     同时处理闲置玩家的逻辑。
    /// </summary>
    private void UpdatePlayerList()
    {
        // 仅当处于实时模式且用户启用了闲置模式时，才计算玩家的闲置状态
        if (IsSmartIdleModeEnabled && !IsInSnapshotMode)
        {
            var now = DateTime.UtcNow;
            foreach (var player in _playerCache.Values)
                player.IsIdle = (now - player.LastActiveTime).TotalSeconds > IdleTimeoutSeconds;
        }
        else
        {
            foreach (var player in _playerCache.Values) player.IsIdle = false;
        }

        var playersForCalcs = IsSmartIdleModeEnabled
            ? Players.Where(p => !p.IsIdle).ToList()
            : Players.ToList();
        if (playersForCalcs.Count == 0)
        {
            foreach (var p in Players)
            {
                p.DamageDisplayPercentage = null;
                p.HealingDisplayPercentage = null;
                p.TakenDamageDisplayPercentage = null;
            }

            UpdateColumnTotals();
            return;
        }

        var totalDamage = playersForCalcs.Sum(p => p.TotalDamage);
        var totalHealing = playersForCalcs.Sum(p => p.TotalHealing);
        var totalTakenDamage = playersForCalcs.Sum(p => p.TakenDamage);
        var showDamagePercent = SortColumn is "TotalDamage" or "TotalDps";
        var showHealingPercent = SortColumn is "TotalHealing" or "TotalHps";

        foreach (var player in Players)
        {
            if (IsSmartIdleModeEnabled && player.IsIdle)
            {
                player.DamageDisplayPercentage = null;
                player.HealingDisplayPercentage = null;
                player.TakenDamageDisplayPercentage = null;
                continue;
            }

            if (totalTakenDamage > 0)
            {
                var takenPct = player.TakenDamage / totalTakenDamage * 100;
                //只有当承伤占比 >= 1% 时才显示
                player.TakenDamageDisplayPercentage = takenPct >= 1 ? $" {takenPct:F0}%" : null;
            }
            else
            {
                player.TakenDamageDisplayPercentage = null;
            }

            if (showDamagePercent && totalDamage > 0)
            {
                var damagePct = player.TotalDamage / totalDamage * 100;
                //只有当伤害占比 >= 1% 时才显示
                player.DamageDisplayPercentage = damagePct >= 1 ? $" {damagePct:F0}%" : null;
            }
            else
            {
                player.DamageDisplayPercentage = null;
            }

            if (showHealingPercent && totalHealing > 0)
            {
                var healingPct = player.TotalHealing / totalHealing * 100;
                //只有当治疗占比 >= 1% 时才显示
                player.HealingDisplayPercentage = healingPct >= 1 ? $" {healingPct:F0}%" : null;
            }
            else
            {
                player.HealingDisplayPercentage = null;
            }
        }

        if (!Sorters.TryGetValue(SortColumn ?? "TotalDps", out var keySelector)) keySelector = Sorters["TotalDps"];
        var sortedQuery = SortDirection == ListSortDirection.Ascending
            ? Players.OrderBy(p => p.IsIdle).ThenBy(keySelector)
            : Players.OrderBy(p => p.IsIdle).ThenByDescending(keySelector);
        var sortedPlayers = sortedQuery.ToList();
        foreach (var p in Players) p.ShowSeparatorAfter = false;
        if (IsSmartIdleModeEnabled)
        {
            var lastActivePlayer = sortedPlayers.LastOrDefault(p => !p.IsIdle);
            if (lastActivePlayer != null) lastActivePlayer.ShowSeparatorAfter = true;
        }

        for (var i = 0; i < sortedPlayers.Count; i++)
        {
            var player = sortedPlayers[i];
            player.Rank = player.IsIdle ? 0 : i + 1;
            var originalIndex = Players.IndexOf(player);
            if (originalIndex != i) Players.Move(originalIndex, i);
        }

        UpdateColumnTotals();
    }

    [RelayCommand]
    private void SortBy(string columnName)
    {
        if (SortColumn == columnName)
        {
            SortDirection = SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            SortColumn = columnName;
            SortDirection = ListSortDirection.Descending;
        }

        UpdatePlayerList();
    }

    [RelayCommand]
    public async Task ResetDataAsync()
    {
        if (IsInSnapshotMode)
        {
            IsInSnapshotMode = false;
            LoadedSnapshotFileName = "";

            // 新增：在返回实时模式前，彻底重置战斗状态
            _fightTimer.Stop();
            _elapsedSeconds = 0;
            FightDurationText = "0:00";
            _isFightActive = false; // 这是最关键的修复
            _lastCombatActivityTime = DateTime.MinValue;

            // 清空当前的快照数据
            Players.Clear();
            _playerCache.Clear();
            OnPropertyChanged(nameof(PlayerCount));

            // 重新订阅实时数据事件
            _apiService.DataReceived += OnDataReceived;
            _uiUpdateTimer.Start(); // 重新启动UI刷新计时器

            // 向后端服务发送“恢复”指令，并同步本地UI状态
            await _apiService.SetPauseStateAsync(false);
            IsPaused = false;

            // 像程序启动时一样，主动获取一次当前数据
            // 后续的数据更新将通过 UiUpdateTimer_Tick 自动触发计时器启动
            var initialData = await _apiService.GetInitialDataAsync();
            if (initialData != null) await ProcessData(initialData);

            // 确保WebSocket连接也已恢复
            await _apiService.ConnectAsync();
            ShowNotification("已返回实时模式");
            return;
        }

        _fightTimer.Stop();
        _elapsedSeconds = 0;
        FightDurationText = "0:00";
        _isFightActive = false;
        _lastCombatActivityTime = DateTime.MinValue;
        var success = await _apiService.ResetDataAsync();
        if (success)
            Application.Current.Dispatcher.Invoke(() =>
            {
                Players.Clear();
                _playerCache.Clear();
            });
        else
            ShowNotification("重置数据失败");
    }

    [RelayCommand]
    public async Task TogglePauseAsync()
    {
        var targetPauseState = !IsPaused;
        var originalState = IsPaused;
        IsPaused = targetPauseState;
        if (targetPauseState)
            _fightTimer.Stop();
        else if (_isFightActive) _fightTimer.Start();
        var success = await _apiService.SetPauseStateAsync(targetPauseState);
        if (!success)
        {
            IsPaused = originalState;
            ShowNotification("暂停/恢复操作失败");
        }
    }

    [RelayCommand]
    private async Task StartCountdown(string? secondsStr)
    {
        if (IsCountdownRunning) return;
        if (!int.TryParse(secondsStr, out var seconds) || seconds <= 0)
        {
            ShowNotification("请输入有效的倒计时秒数");
            return;
        }

        await ResetDataAsync();
        if (IsPaused) await TogglePauseAsync();
        _countdownTimeLeft = TimeSpan.FromSeconds(seconds);
        IsCountdownRunning = true;
        CountdownText = $"{_countdownTimeLeft:mm\\:ss}";
        ShowNotification("倒计时开始");
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += CountdownTimer_Tick;
        _countdownTimer.Start();
        // 关闭弹窗
        IsCountdownOptionsPopupOpen = false;
    }

    private async void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            _countdownTimeLeft = _countdownTimeLeft.Add(TimeSpan.FromSeconds(-1));
            CountdownText = $"{_countdownTimeLeft:mm\\:ss}";
            if (!(_countdownTimeLeft.TotalSeconds <= 0)) return;
            _countdownTimer?.Stop();
            IsCountdownRunning = false;
            CountdownText = "倒计时";
            if (!IsPaused) await TogglePauseAsync();
            ShowNotification("倒计时结束, DPS统计已暂停");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in countdown timer tick: {ex.Message}");
            ShowNotification("倒计时出错");
        }
    }

    [RelayCommand]
    public async Task AbortCountdownAsync()
    {
        if (!IsCountdownRunning) return;
        _countdownTimer?.Stop();
        IsCountdownRunning = false;
        CountdownText = "倒计时";
        if (!IsPaused) await TogglePauseAsync();
        ShowNotification("倒计时已中断");
    }

    [RelayCommand]
    public void ToggleLock()
    {
        IsLocked = !IsLocked;
    }

    [RelayCommand]
    public void ToggleSettings()
    {
        IsSettingsVisible = !IsSettingsVisible;
    }

    public async Task PauseOnExitAsync()
    {
        // 仅当复选框被选中且当前未暂停时，才发送暂停指令
        if (IsPauseOnExitEnabled && !IsPaused) await _apiService.SetPauseStateAsync(true);
    }

    [RelayCommand]
    private static void ExitApplication()
    {
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private void IncreaseFontSize()
    {
        if (FontSize < 24) FontSize++;
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        if (FontSize > 10) FontSize--;
    }

    public static class SortableColumns
    {
        public const string Name = nameof(PlayerViewModel.DisplayName);
        public const string FightPoint = nameof(PlayerViewModel.FightPoint);
        public const string Profession = nameof(PlayerViewModel.Profession);
        public const string TotalDamage = nameof(PlayerViewModel.TotalDamage);
        public const string TotalHealing = nameof(PlayerViewModel.TotalHealing);
        public const string TotalDps = nameof(PlayerViewModel.TotalDps);
        public const string TotalHps = nameof(PlayerViewModel.TotalHps);
        public const string TakenDamage = nameof(PlayerViewModel.TakenDamage);
    }

    /// <summary>
    /// 定时检查并主动获取玩家的详细数据。
    /// </summary>
    private async void ProactiveDataFetchTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // 创建一个副本进行遍历，避免在循环中修改集合
            var playersToProcess = _playerCache.Values.ToList();

            foreach (var player in playersToProcess)
            {
                // 如果玩家处于闲置、已被手动展开过或正在获取数据，则跳过
                if (player.IsIdle || player.HasBeenExpanded || player.IsFetchingSkillData)
                {
                    continue;
                }

                var now = DateTime.UtcNow;

                // 策略1：初次获取 (新玩家进入列表3秒后)
                if (player.RawSkillData == null)
                {
                    if (_playerEntryTimes.TryGetValue(player.Uid.ToString(), out var entryTime) &&
                        (now - entryTime) > TimeSpan.FromSeconds(3))
                    {
                        await FetchAndProcessSkillDataAsync(player);
                    }
                }
                // 策略2：周期性刷新 (距离上次获取超过1分钟)
                else if ((now - player.LastSkillDataFetchTime) > TimeSpan.FromMinutes(1))
                {
                    await FetchAndProcessSkillDataAsync(player);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred in ProactiveDataFetchTimer_Tick: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查所有玩家，将长时间无数据变化的玩家标记为闲置。
    /// </summary>
    private void CheckForIdlePlayers()
    {
        var now = DateTime.UtcNow;
        foreach (var player in _playerCache.Values)
        {
            // 如果玩家当前不闲置，但上次活跃时间已经超过阈值，则将其设为闲置
            if (player.IsIdle || !((now - player.LastActiveTime).TotalSeconds > IdleTimeoutSeconds)) continue;
            player.IsIdle = true;
            // 将闲置玩家的百分比清空
            player.DamageDisplayPercentage = null;
            player.HealingDisplayPercentage = null;
            player.TakenDamageDisplayPercentage = null;
        }
    }
}