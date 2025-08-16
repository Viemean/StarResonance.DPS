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
    [ObservableProperty] private ICollectionView _playersView;

    //闲置时长
    private const int IdleTimeoutSeconds = 30;

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

    private readonly Dictionary<string, DateTime> _playerEntryTimes = new(); //追踪玩家首次出现的时间

    private readonly DispatcherTimer _uiUpdateTimer;
    private readonly DispatcherTimer _skillUpdateTimer; // 用于实时更新展开的技能列表


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
    [NotifyPropertyChangedFor(nameof(FightDurationVisibility))]
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

    partial void OnIsSmartIdleModeEnabledChanged(bool value)
    {
        _ = value;
        // 重新应用排序规则，这会添加或移除按 IsIdle 排序的规则
        ApplySorting();

        // 立即刷新整个列表的显示，包括排序、排名和百分比
        UpdatePlayerList();
    }

    partial void OnPlayersChanged(ObservableCollection<PlayerViewModel> value)
    {
        PlayersView = CollectionViewSource.GetDefaultView(value);
    }

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

    public Visibility FightDurationVisibility =>
        !IsPaused && !IsCountdownRunning ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NotificationVisibility => IsNotificationVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVisibility => IsSettingsVisible ? Visibility.Visible : Visibility.Collapsed;
    public bool IsHitTestVisible => !IsLocked;
    public double FontOpacity => WindowOpacity < 0.8 ? 0.8 : WindowOpacity;

    public MainViewModel(ApiService apiService, LocalizationService localizationService)
    {
        _skillUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _skillUpdateTimer.Tick += SkillUpdateTimer_Tick;
        _playersView = CollectionViewSource.GetDefaultView(Players);
        _apiService = apiService;
        Localization = localizationService;
        SystemFonts = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
        _selectedFontFamily = new FontFamily("Microsoft YaHei");

        //初始化 Players 集合的默认视图
        PlayersView = CollectionViewSource.GetDefaultView(Players);

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

        // 初始化时设置默认排序
        SortColumn = "TotalDps";
        ApplySorting();

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _notificationTimer.Tick += (_, _) => IsNotificationVisible = false;

        ConnectionStatusText = Localization["Connecting"] ?? "正在连接...";
        PauseButtonText = Localization["Pause"] ?? "暂停";
    }

    private async void SkillUpdateTimer_Tick(object? sender, EventArgs e)
    {
        // 如果存在一个已展开的玩家，并且我们不在快照模式下，则刷新其技能数据
        if (_expandedPlayer != null && !IsInSnapshotMode)
        {
            await FetchAndProcessSkillDataAsync(_expandedPlayer);
        }
    }

    public IEnumerable<FontFamily> SystemFonts { get; }

    public LocalizationService Localization { get; }
    public string AccurateCritDamageLabelText => Localization["AccurateCritDamageLabel"] ?? "精确暴伤加成 (加权):";

    /// <summary>
    /// 将当前的排序规则应用到 PlayersView。
    /// </summary>
    private void ApplySorting()
    {
        // 在修改 SortDescriptions 之前，最好先切换到UI线程
        Application.Current.Dispatcher.Invoke(() =>
        {
            PlayersView.SortDescriptions.Clear();

            // 规则1：如果启用了闲置模式，总是先按是否闲置排序（不闲置的在前）
            if (IsSmartIdleModeEnabled)
            {
                PlayersView.SortDescriptions.Add(new SortDescription(nameof(PlayerViewModel.IsIdle),
                    ListSortDirection.Ascending));
            }

            // 规则2：根据用户选择的列进行主排序
            if (!string.IsNullOrEmpty(SortColumn))
            {
                PlayersView.SortDescriptions.Add(new SortDescription(SortColumn, SortDirection));
            }
        });
    }

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
        _skillUpdateTimer.Stop();
        _stateSaveTimer.Stop();
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
            _skillUpdateTimer.Stop(); // [修改] 折叠时停止定时器
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

        // [修改] 展开后启动定时器
        _skillUpdateTimer.Start();
    }

    private async Task FetchAndProcessSkillDataAsync(PlayerViewModel player)
    {
        //增加快照模式下的逻辑
        if (IsInSnapshotMode)
        {
            // 在快照模式下，从已加载的 RawSkillData 填充技能列表
            await Application.Current.Dispatcher.InvokeAsync(() => player.Skills.Clear());

            if (player.RawSkillData?.Skills != null)
            {
                var playerTotalValue = player.TotalDamage + player.TotalHealing;
                var skills = player.RawSkillData.Skills.Values
                    .OrderByDescending(s => s.TotalDamage)
                    .Take(6)
                    .Select(s => new SkillViewModel(s, playerTotalValue));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var skillVm in skills)
                    {
                        player.Skills.Add(skillVm);
                    }
                });
            }

            return; // 快照模式处理完毕，直接返回
        }

        // 实时模式的逻辑
        try
        {
            player.IsFetchingSkillData = true;
            // 在UI线程上更新加载状态
            await Application.Current.Dispatcher.InvokeAsync(() => player.SetLoadingSkills(true));

            var skillDataResponse = await _apiService.GetSkillDataAsync(player.Uid);
            if (skillDataResponse?.Data != null)
            {
                player.RawSkillData = skillDataResponse.Data;
                player.LastSkillDataFetchTime = DateTime.UtcNow;
                player.NotifyTooltipUpdate();

                {
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

            // 新增：手动通知UI更新PlayerCount属性
            OnPropertyChanged(nameof(PlayerCount));

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
                var changes = ProcessDataChanges(dataToProcess);

                // 如果有任何有效的数据变化（伤害、治疗等）
                if (changes.HasDataChanged)
                {
                    _lastCombatActivityTime = DateTime.UtcNow; // 更新最后活跃时间
                    // 如果战斗计时器未运行且当前非暂停状态，则启动计时器
                    if (!_isFightActive && !IsPaused)
                    {
                        _isFightActive = true;
                        _fightTimer.Start();
                    }
                }

                // 如果有玩家“唤醒”或列表结构发生变化，才立即刷新列表
                if (changes.ListNeedsRefresh)
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
            // 战斗首次启动逻辑 (保持不变)
            if (!_isFightActive && !IsPaused)
                if (data.User.Values.Any(u => u.TotalDamage.Total > 0 || u.TotalHealing.Total > 0))
                {
                    _isFightActive = true;
                    _lastCombatActivityTime = DateTime.UtcNow;
                    // 启动计时器是UI操作，需要异步调度
                    await Application.Current.Dispatcher.InvokeAsync(() => _fightTimer.Start());
                }

            // [修改] 核心修改：在后台构建新列表，实现原子性UI更新

            var newPlayerList = new List<PlayerViewModel>();
            var newPlayerCache = new Dictionary<string, PlayerViewModel>();
            var newPlayerEntryTimes = new Dictionary<string, DateTime>();

            foreach (var (key, userData) in data.User)
            {
                long.TryParse(key, out var uid);

                // 尝试重用已存在的 PlayerViewModel 实例，以保留UI状态（如展开详情）
                if (_playerCache.TryGetValue(key, out var playerVm))
                {
                    newPlayerCache.Add(key, playerVm);
                    if (_playerEntryTimes.TryGetValue(key, out var entryTime))
                    {
                        newPlayerEntryTimes.Add(key, entryTime);
                    }
                }
                else
                {
                    // 创建新玩家实例
                    playerVm = new PlayerViewModel(uid, Localization, this);
                    newPlayerCache.Add(key, playerVm);
                    newPlayerEntryTimes.Add(key, DateTime.UtcNow);
                    _ = FetchInitialSkillDataAsync(playerVm);
                }

                // 更新数据并添加到临时列表
                playerVm.Update(userData, 0, FightDurationText); // Rank稍后在UpdatePlayerList中重新计算
                newPlayerList.Add(playerVm);
            }

            // 在UI线程上执行一次性的状态替换
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 1. 原子地替换后台缓存
                _playerCache.Clear();
                foreach (var item in newPlayerCache) _playerCache.Add(item.Key, item.Value);

                _playerEntryTimes.Clear();
                foreach (var item in newPlayerEntryTimes) _playerEntryTimes.Add(item.Key, item.Value);

                Players = new ObservableCollection<PlayerViewModel>(newPlayerList);

                ApplySorting();
                UpdatePlayerList();
                OnPropertyChanged(nameof(PlayerCount));
            });
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Error processing data: {e}");
        }
    }

    private (bool HasDataChanged, bool ListNeedsRefresh) ProcessDataChanges(ApiResponse data)
    {
        var listStructureChanged = false;
        var aPlayerWokeUp = false;
        var hasDataChanged = false; // 追踪是否有数据变化

        var activeKeys = new HashSet<string>(data.User.Keys);

        // 处理离开的玩家
        var removedKeys = _playerCache.Keys.Except(activeKeys).ToList();
        if (removedKeys.Count > 0)
        {
            listStructureChanged = true;
            foreach (var key in removedKeys)
            {
                if (!_playerCache.TryGetValue(key, out var playerToRemove)) continue;
                Players.Remove(playerToRemove);
                _playerCache.Remove(key);
                _playerEntryTimes.Remove(key);
            }
        }

        // 添加或更新玩家数据
        foreach (var (key, userData) in data.User)
        {
            long.TryParse(key, out var uid);

            if (!_playerCache.TryGetValue(key, out var playerVm))
            {
                listStructureChanged = true;
                playerVm = new PlayerViewModel(uid, Localization, this);
                _playerCache.Add(key, playerVm);
                Players.Add(playerVm);
                _playerEntryTimes.Add(key, DateTime.UtcNow);
                _ = FetchInitialSkillDataAsync(playerVm);

                hasDataChanged = true;
                playerVm.LastActiveTime = DateTime.UtcNow;
            }
            else
            {
                // Case 2: 已有玩家
                // 检查数据是否有实际变化
                if (Math.Abs(playerVm.TotalDamage - userData.TotalDamage.Total) > 1E-6 ||
                    Math.Abs(playerVm.TotalHealing - userData.TotalHealing.Total) > 1E-6 ||
                    Math.Abs(playerVm.TakenDamage - userData.TakenDamage) > 1E-6)
                {
                    hasDataChanged = true;
                    playerVm.LastActiveTime = DateTime.UtcNow;

                    // 检查玩家是否从闲置状态被唤醒
                    if (playerVm.IsIdle)
                    {
                        playerVm.IsIdle = false;
                        aPlayerWokeUp = true; // 标记需要立即重排
                    }
                }
            }

            //无论是新玩家还是旧玩家，playerVm 都绝对不为 null
            playerVm.Update(userData, playerVm.Rank, FightDurationText);
        }

        // [修改] 始终通知 PlayerCount 可能已更改，确保UI实时同步
        OnPropertyChanged(nameof(PlayerCount));

        // 返回一个包含两个布尔值的元组
        return (hasDataChanged, aPlayerWokeUp || listStructureChanged);
    }

    /// <summary>
    ///     根据当前排序设置，对玩家列表进行排序、计算百分比并更新UI。
    ///     同时处理闲置玩家的逻辑。
    /// </summary>
    private void UpdatePlayerList()
    {
        var playersForCalcs = IsSmartIdleModeEnabled
            ? Players.Where(p => !p.IsIdle).ToList()
            : Players.ToList();

        if (playersForCalcs.Count == 0)
        {
            // 如果没有活跃玩家，则清空所有百分比
            foreach (var p in Players)
            {
                p.DamageDisplayPercentage = null;
                p.HealingDisplayPercentage = null;
                p.DpsDisplayPercentage = null;
                p.HpsDisplayPercentage = null;
                p.TakenDamageDisplayPercentage = null;
            }

            UpdateColumnTotals();
            return;
        }

        // 1. 计算所有需要用到的团队总和
        var totalDamage = playersForCalcs.Sum(p => p.TotalDamage);
        var totalHealing = playersForCalcs.Sum(p => p.TotalHealing);
        var totalDps = playersForCalcs.Sum(p => p.TotalDps);
        var totalHps = playersForCalcs.Sum(p => p.TotalHps);
        var totalTakenDamage = playersForCalcs.Sum(p => p.TakenDamage);

        foreach (var player in Players)
        {
            // 2. 首先清空所有非固定的百分比，确保切换排序时旧的百分比会消失
            player.DamageDisplayPercentage = null;
            player.HealingDisplayPercentage = null;
            player.DpsDisplayPercentage = null;
            player.HpsDisplayPercentage = null;

            // 如果玩家处于闲置状态，则不计算任何百分比
            if (IsSmartIdleModeEnabled && player.IsIdle)
            {
                player.TakenDamageDisplayPercentage = null;
                continue;
            }

            // 3. [固定显示] 始终计算并显示承伤百分比
            if (totalTakenDamage > 0)
            {
                var takenPct = player.TakenDamage / totalTakenDamage * 100;
                player.TakenDamageDisplayPercentage = takenPct >= 1 ? $" {takenPct:F0}%" : null;
            }
            else
            {
                player.TakenDamageDisplayPercentage = null;
            }

            // 4. [动态显示] 根据当前排序列 (SortColumn) 计算并显示对应的百分比
            switch (SortColumn)
            {
                case SortableColumns.TotalDamage:
                    if (totalDamage > 0)
                    {
                        var pct = player.TotalDamage / totalDamage * 100;
                        player.DamageDisplayPercentage = pct >= 1 ? $" {pct:F0}%" : null;
                    }

                    break;
                case SortableColumns.TotalHealing:
                    if (totalHealing > 0)
                    {
                        var pct = player.TotalHealing / totalHealing * 100;
                        player.HealingDisplayPercentage = pct >= 1 ? $" {pct:F0}%" : null;
                    }

                    break;
                case SortableColumns.TotalDps:
                    if (totalDps > 0)
                    {
                        var pct = player.TotalDps / totalDps * 100;
                        player.DpsDisplayPercentage = pct >= 1 ? $" {pct:F0}%" : null;
                    }

                    break;
                case SortableColumns.TotalHps:
                    if (totalHps > 0)
                    {
                        var pct = player.TotalHps / totalHps * 100;
                        player.HpsDisplayPercentage = pct >= 1 ? $" {pct:F0}%" : null;
                    }

                    break;
            }
        }

        // 更新UI视图、排名和表头总计 (这部分逻辑保持不变)
        Application.Current.Dispatcher.Invoke(() => PlayersView.Refresh());

        var rank = 1;
        foreach (var item in PlayersView)
        {
            if (item is PlayerViewModel player)
            {
                player.Rank = (IsSmartIdleModeEnabled && player.IsIdle) ? 0 : rank++;
            }
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

        ApplySorting();
        UpdatePlayerList();
    }

    [RelayCommand]
    public async Task ResetDataAsync()
    {
        _skillUpdateTimer.Stop();
        _expandedPlayer = null;
        if (IsInSnapshotMode)
        {
            IsInSnapshotMode = false;
            LoadedSnapshotFileName = "";

            // 重置战斗状态
            _fightTimer.Stop();
            _elapsedSeconds = 0;
            FightDurationText = "0:00";
            _isFightActive = false;
            _lastCombatActivityTime = DateTime.MinValue;

            // 清空列表
            Players.Clear();
            _playerCache.Clear();
            OnPropertyChanged(nameof(PlayerCount));

            var (success, isPaused) = await _apiService.GetPauseStateAsync();
            if (success)
            {
                // 如果服务器当前是暂停的，则同步UI的暂停状态
                if (isPaused)
                {
                    IsPaused = true;
                    ShowNotification("已返回实时模式 (服务暂停中)");
                }
                else
                {
                    // 如果服务器未暂停，则执行与首次启动一致的逻辑
                    IsPaused = false;
                    var initialData = await _apiService.GetInitialDataAsync();
                    if (initialData != null)
                    {
                        await ProcessData(initialData);
                    }

                    ShowNotification("已返回实时模式");
                }
            }
            else
            {
                // 如果查询失败，默认恢复非暂停状态
                IsPaused = false;
                ShowNotification("已返回实时模式 (无法获取服务状态)");
            }

            // [修改] 在UI被初始数据填充完毕后，再重新连接到实时数据流
            _apiService.DataReceived += OnDataReceived;
            _uiUpdateTimer.Start();
            await _apiService.ConnectAsync();

            // 确保最终玩家计数被更新
            OnPropertyChanged(nameof(PlayerCount));

            return;
        }

// ...
        // 以下是处理非快照模式下的重置逻辑
        _fightTimer.Stop();
        _elapsedSeconds = 0;
        FightDurationText = "0:00";
        _isFightActive = false;
        _lastCombatActivityTime = DateTime.MinValue;

        //告知后端服务重置数据
        var successReset = await _apiService.ResetDataAsync();
        if (!successReset)
        {
            ShowNotification("重置数据失败");
            return;
        }

        // 立即清空本地UI和缓存
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Players.Clear();
            _playerCache.Clear();
            OnPropertyChanged(nameof(PlayerCount));
            UpdateColumnTotals(); // 同时清空表头悬浮统计
        });

        // 立即通过HTTP请求获取一次完整的当前数据
        var refreshedData = await _apiService.GetInitialDataAsync();
        if (refreshedData != null)
        {
            // 使用ProcessData方法将数据一次性加载到UI
            await ProcessData(refreshedData);
        }
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
    ///     当新玩家首次出现时，异步获取其基础属性数据（如等级、臂章等）用于Tooltip显示。
    /// </summary>
    private async Task FetchInitialSkillDataAsync(PlayerViewModel player)
    {
        // 如果玩家已经有数据或正在获取，则跳过
        if (player.RawSkillData != null || player.IsFetchingSkillData) return;

        try
        {
            player.IsFetchingSkillData = true;
            var skillDataResponse = await _apiService.GetSkillDataAsync(player.Uid);
            if (skillDataResponse?.Data != null)
            {
                player.RawSkillData = skillDataResponse.Data;
                // 数据获取后，手动通知Tooltip更新
                player.NotifyTooltipUpdate();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load initial skill data for {player.Name}: {ex.Message}");
        }
        finally
        {
            player.IsFetchingSkillData = false;
        }
    }

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