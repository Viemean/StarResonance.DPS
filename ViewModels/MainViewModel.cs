using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    public int UiUpdateInterval { get; init; } = 250;
    public string? CultureName { get; init; }
    public bool PauseOnExit { get; init; } = true;

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
        { SortableColumns.Name, p => p.DisplayName },
        { SortableColumns.FightPoint, p => p.FightPoint },
        { SortableColumns.Profession, p => p.Profession },
        { SortableColumns.TotalDamage, p => p.TotalDamage },
        { SortableColumns.TotalHealing, p => p.TotalHealing },
        { SortableColumns.TotalDps, p => p.TotalDps },
        { SortableColumns.TotalHps, p => p.TotalHps },
        { SortableColumns.TakenDamage, p => p.TakenDamage }
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
    [ObservableProperty] private bool _isCountdownRunning;
    [ObservableProperty] private bool _isCustomCountdownPopupOpen;
    private bool _isFightActive;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _isNotificationVisible;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isPauseOnExitEnabled = true;
    [ObservableProperty] private bool _isSettingsVisible;
    [ObservableProperty] private bool _isSmartIdleModeEnabled;
    private DateTime _lastCombatActivityTime;
    private ApiResponse? _latestReceivedData;
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
    [ObservableProperty] private int _uiUpdateInterval = 250;
    [ObservableProperty] private double _windowOpacity = 0.85;

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
        Localization.PropertyChanged -= _onLocalizationPropertyChanged;

        _clockTimer.Stop();
        _uiUpdateTimer.Stop();
        _fightTimer.Stop();
        _countdownTimer?.Stop();
        _notificationTimer.Stop();
        _stateSaveTimer.Stop();
        SaveState();
        await _apiService.DisposeAsync();
        GC.SuppressFinalize(this);
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

    public void ShowNotification(string message)
    {
        NotificationText = message;
        IsNotificationVisible = true;
        _notificationTimer.Stop();
        _notificationTimer.Start();
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
        if (_expandedPlayer != null && _expandedPlayer != player)
        {
            _expandedPlayer.IsExpanded = false;
        }

        if (player.IsExpanded)
        {
            player.IsExpanded = false;
            _expandedPlayer = null;
        }
        else
        {
            player.IsExpanded = true;
            _expandedPlayer = player;
            await LoadSkillData(player);
        }
    }

    private async Task LoadSkillData(PlayerViewModel player)
    {
        try
        {
            var skillDataResponse = await _apiService.GetSkillDataAsync(player.Uid);
            if (skillDataResponse?.Data?.Skills != null)
            {
                var playerTotalValue = player.TotalDamage + player.TotalHealing;
                var skills = skillDataResponse.Data.Skills.Values
                    .OrderByDescending(s => s.TotalDamage)
                    .Take(6)
                    .Select(s => new SkillViewModel(s, playerTotalValue));
                Application.Current.Dispatcher.Invoke(() =>
                {
                    player.Skills.Clear();
                    foreach (var skillVm in skills) player.Skills.Add(skillVm);
                });

                //调用重命名后的精确计算方法
                player.CalculateAccurateCritDamage(skillDataResponse.Data.Skills);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load skill data: {ex.Message}");
            ShowNotification("加载技能数据失败");
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
        ApiResponse? dataToProcess;
        lock (_dataLock)
        {
            dataToProcess = _latestReceivedData;
            _latestReceivedData = null;
        }

        if (dataToProcess == null) return;

        // 此方法现在只负责一件事：检查是否有变化，并更新时间戳
        var hasChanged = false;
        if (dataToProcess.User is { Count: > 0 })
        {
            if (dataToProcess.User.Count != _playerCache.Count) hasChanged = true;
            else
                foreach (var (key, newUserData) in dataToProcess.User)
                {
                    if (_playerCache.TryGetValue(key, out var playerVm))
                    {
                        if (!(Math.Abs(playerVm.TotalDamage - newUserData.TotalDamage.Total) > 1E-6) &&
                            !(Math.Abs(playerVm.TotalHealing - newUserData.TotalHealing.Total) > 1E-6) &&
                            !(Math.Abs(playerVm.TakenDamage - newUserData.TakenDamage) > 1E-6)) continue;
                        hasChanged = true;
                        break;
                    }

                    if (!(newUserData.TotalDamage.Total > 0) && !(newUserData.TotalHealing.Total > 0) &&
                        !(newUserData.TakenDamage > 0)) continue;
                    hasChanged = true;
                    break;
                }
        }

        if (hasChanged)
        {
            _lastCombatActivityTime = DateTime.UtcNow;
            // 战斗首次启动
            if (!_isFightActive && !IsPaused)
            {
                _isFightActive = true;
                if (!_fightTimer.IsEnabled) _fightTimer.Start();
            }
        }

        _ = ProcessData(dataToProcess);
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
            if (data.User == null) return;

            // 战斗首次启动逻辑
            if (!_isFightActive && !IsPaused)
                if (data.User.Values.Any(u => u.TotalDamage.Total > 0 || u.TotalHealing.Total > 0))
                {
                    _isFightActive = true;
                    _lastCombatActivityTime = DateTime.UtcNow;
                    // 启动计时器是UI操作，需要异步调度
                    await Application.Current.Dispatcher.InvokeAsync(() => _fightTimer.Start());
                }

            // --- UI 更新现在可以直接在后台线程中进行 ---
            var activeKeys = new HashSet<string>(data.User.Keys);

            // ToList() 创建一个副本，避免在遍历时修改集合
            foreach (var existingKey in _playerCache.Keys.Except(activeKeys).ToList())
            {
                if (!_playerCache.TryGetValue(existingKey, out var playerToRemove)) continue;
                Players.Remove(playerToRemove);
                _playerCache.Remove(existingKey);
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

    /// <summary>
    ///     根据当前排序设置，对玩家列表进行排序、计算百分比并更新UI。
    ///     同时处理闲置玩家的逻辑。
    /// </summary>
    private void UpdatePlayerList()
    {
        if (IsSmartIdleModeEnabled)
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
}