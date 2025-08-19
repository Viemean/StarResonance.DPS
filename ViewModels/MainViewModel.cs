using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StarResonance.DPS.Models;
using StarResonance.DPS.Services;

namespace StarResonance.DPS.ViewModels;

/// <summary>
///     主窗口的视图模型，负责处理所有UI逻辑、数据状态和与服务的交互。
/// </summary>
public class MainViewModel : ObservableObject, IAsyncDisposable, INotificationService
{
    public enum SearchMode
    {
        ById,
        ByName
    }

    //闲置时长
    private const int IdleTimeoutSeconds = 30;

    private readonly ApiService _apiService;
    private readonly DispatcherTimer _clockTimer;
    private readonly object _dataLock = new();
    private readonly DispatcherTimer _fightTimer;
    private readonly DispatcherTimer _notificationTimer;

    private readonly PropertyChangedEventHandler _onLocalizationPropertyChanged;
    private readonly Dictionary<string, PlayerViewModel> _playerCache = new();

    private readonly Dictionary<string, DateTime> _playerEntryTimes = new(); //追踪玩家首次出现的时间
    private readonly DispatcherTimer _searchDebounceTimer; // 防抖计时器
    private readonly DispatcherTimer _skillUpdateTimer; // 用于实时更新展开的技能列表
    private readonly string _stateFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dps_state.json");
    private readonly DispatcherTimer _stateSaveTimer;

    private readonly DispatcherTimer _uiUpdateTimer;

    private string _backendUrl = "ws://localhost:8989";

    private Brush _connectionStatusColor = Brushes.Orange;

    private string _connectionStatusText = "正在连接...";

    private string _countdownText = "倒计时";
    private TimeSpan _countdownTimeLeft;
    private DispatcherTimer? _countdownTimer;

    private string _currentTime = DateTime.Now.ToLongTimeString();

    private string _customCountdownMinutes = "10";
    private int _elapsedSeconds;
    private PlayerViewModel? _expandedPlayer;

    private string _fightDurationText = "0:00";

    private double _fontOpacity = 1.0;

    private double _fontSize = 14;

    private bool _isCountdownOptionsPopupOpen;

    private bool _isCountdownRunning;

    private bool _isCustomCountdownPopupOpen;
    private bool _isFightActive;


    private bool _isLocked;

    private bool _isNotificationVisible;

    private bool _isPaused;

    private bool _isPauseOnExitEnabled = true;

    private bool _isPauseOnSnapshotEnabled = true;

    private bool _isSettingsVisible;

    private bool _isSmartIdleModeEnabled;

    private bool _isSortingPaused;

    private DateTime _lastCombatActivityTime;
    private ApiResponse? _latestReceivedData;


    private string _lockIconContent = "🔓";

    private string _lockMenuHeaderText = "锁定";

    private string _notificationText = "";

    private string _pauseButtonText = "暂停";

    private Brush _pauseStatusColor = Brushes.LimeGreen;

    private ObservableCollection<PlayerViewModel> _players = [];

    private ICollectionView _playersView;

    private string _searchFilterText = string.Empty;

    private FontFamily _selectedFontFamily;

    private SearchModeItem? _selectedSearchModeItem;

    private string? _sortColumn;

    private ListSortDirection _sortDirection = ListSortDirection.Descending;

    private string? _takenDamageSumTooltip;

    private string? _totalDamageSumTooltip;

    private string? _totalDpsSumTooltip;

    private string? _totalHealingSumTooltip;

    private string? _totalHpsSumTooltip;

    private int _uiUpdateInterval = 500;

    private double _windowHeight = 350;

    private double _windowLeft = 100;

    private double _windowOpacity = 0.85;

    private double _windowTop = 100;

    private double _windowWidth = 700;


    public MainViewModel(ApiService apiService, LocalizationService localizationService)
    {
        OpenCountdownOptionsCommand = new RelayCommand(_ => OpenCountdownOptions());
        OpenCustomCountdownCommand = new RelayCommand(_ => OpenCustomCountdown());
        CancelCustomCountdownCommand = new RelayCommand(_ => CancelCustomCountdown());
        SortByCommand = new RelayCommand(columnName => SortBy((string)columnName!));
        ToggleLockCommand = new RelayCommand(_ => ToggleLock());
        ToggleSettingsCommand = new RelayCommand(_ => ToggleSettings());
        ExitApplicationCommand = new RelayCommand(_ => ExitApplication());
        IncreaseFontSizeCommand = new RelayCommand(_ => IncreaseFontSize());
        DecreaseFontSizeCommand = new RelayCommand(_ => DecreaseFontSize());

        StartCustomCountdownCommand = new AsyncRelayCommand(async _ => await StartCustomCountdown());
        ConnectToBackendCommand = new AsyncRelayCommand(async _ => await ConnectToBackendAsync());
        TogglePlayerExpansionCommand =
            new AsyncRelayCommand(async player => await TogglePlayerExpansion((PlayerViewModel)player!));
        StartCountdownCommand = new AsyncRelayCommand(async seconds => await StartCountdown((string?)seconds));

        _skillUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _skillUpdateTimer.Tick += SkillUpdateTimer_Tick;
        _playersView = CollectionViewSource.GetDefaultView(Players);
        _apiService = apiService;
        Localization = localizationService;
        // 初始化 SnapshotViewModel 并订阅事件
        SnapshotViewModel = new SnapshotViewModel(this); // this 作为 INotificationService 传入
        SnapshotViewModel.RequestDataForSave += OnRequestDataForSave;
        SnapshotViewModel.SnapshotLoaded += OnSnapshotLoaded;
        SnapshotViewModel.ExitedSnapshotMode += OnExitedSnapshotMode;
        SnapshotViewModel.PropertyChanged += SnapshotViewModel_PropertyChanged;

        SystemFonts = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
        _selectedFontFamily = new FontFamily("Microsoft YaHei");

        //初始化 Players 集合的默认视图
        PlayersView = CollectionViewSource.GetDefaultView(Players);

        _onLocalizationPropertyChanged = (_, e) =>
        {
            OnPropertyChanged(string.IsNullOrEmpty(e.PropertyName) ? string.Empty : nameof(Localization));
            // 当语言文化改变时，更新依赖本地化的UI元素
            if (e.PropertyName != nameof(LocalizationService.CurrentCulture) &&
                !string.IsNullOrEmpty(e.PropertyName)) return;
            UpdateLocalizedSearchModes();

            // 遍历当前所有玩家ViewModel，使其缓存的本地化字符串失效并触发UI更新。
            foreach (var player in Players) player.InvalidateLocalizedStrings();
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

        // 初始化防抖计时器 (使用弃元 '_' 消除警告)
        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            ApplyFilter();
        };
        UpdateLocalizedSearchModes();

        // 初始化时设置默认排序
        SortColumn = "TotalDps";
        ApplySorting();

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _notificationTimer.Tick += (_, _) => IsNotificationVisible = false;

        ConnectionStatusText = Localization["Connecting"] ?? "正在连接...";
        PauseButtonText = Localization["Pause"] ?? "暂停";
    }

    public string BackendUrl
    {
        get => _backendUrl;
        set => SetProperty(ref _backendUrl, value);
    }

    public Brush ConnectionStatusColor
    {
        get => _connectionStatusColor;
        private set => SetProperty(ref _connectionStatusColor, value);
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => SetProperty(ref _connectionStatusText, value);
    }

    public string CountdownText
    {
        get => _countdownText;
        private set => SetProperty(ref _countdownText, value);
    }

    public string CurrentTime
    {
        get => _currentTime;
        private set => SetProperty(ref _currentTime, value);
    }

    public string CustomCountdownMinutes
    {
        get => _customCountdownMinutes;
        set
        {
            if (!SetProperty(ref _customCountdownMinutes, value)) return;
            if (!double.TryParse(value, out var minutes)) return;
            if (!(minutes > 60)) return;
            // 更简单的方式是直接在 set 之前校验，但为了保持原逻辑，我们这样做：
            _customCountdownMinutes = "60";
            OnPropertyChanged(); // 再次通知UI更新为 "60"
        }
    }

    public string FightDurationText
    {
        get => _fightDurationText;
        private set => SetProperty(ref _fightDurationText, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    public bool IsCountdownOptionsPopupOpen
    {
        get => _isCountdownOptionsPopupOpen;
        set => SetProperty(ref _isCountdownOptionsPopupOpen, value);
    }

    private bool IsCountdownRunning
    {
        get => _isCountdownRunning;
        set
        {
            if (!SetProperty(ref _isCountdownRunning, value)) return;
            OnPropertyChanged(nameof(CountdownRunningVisibility));
            OnPropertyChanged(nameof(RealtimeModeVisibility));
            OnPropertyChanged(nameof(FightDurationVisibility));
        }
    }

    public bool IsCustomCountdownPopupOpen
    {
        get => _isCustomCountdownPopupOpen;
        set => SetProperty(ref _isCustomCountdownPopupOpen, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (!SetProperty(ref _isLocked, value)) return;
            LockMenuHeaderText = value ? Localization["Unlock"] ?? "解锁" : Localization["Lock"] ?? "锁定";
            LockIconContent = value ? "🔒" : "🔓";
            if (value) ShowNotification("可通过托盘图标右键解锁");
            OnPropertyChanged(nameof(IsHitTestVisible));
        }
    }

    private bool IsNotificationVisible
    {
        get => _isNotificationVisible;
        set
        {
            if (SetProperty(ref _isNotificationVisible, value)) OnPropertyChanged(nameof(NotificationVisibility));
        }
    }

    private bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (!SetProperty(ref _isPaused, value)) return;
            PauseStatusColor = value ? Brushes.Red : Brushes.LimeGreen;
            PauseButtonText = value ? Localization["Resume"] ?? "恢复" : Localization["Pause"] ?? "暂停";
            OnPropertyChanged(nameof(PauseStatusVisibility));
            OnPropertyChanged(nameof(FightDurationVisibility));
        }
    }

    public bool IsPauseOnExitEnabled
    {
        get => _isPauseOnExitEnabled;
        set => SetProperty(ref _isPauseOnExitEnabled, value);
    }

    public bool IsPauseOnSnapshotEnabled
    {
        get => _isPauseOnSnapshotEnabled;
        set => SetProperty(ref _isPauseOnSnapshotEnabled, value);
    }

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        set
        {
            if (SetProperty(ref _isSettingsVisible, value)) OnPropertyChanged(nameof(SettingsVisibility));
        }
    }

    public bool IsSmartIdleModeEnabled
    {
        get => _isSmartIdleModeEnabled;
        set
        {
            if (!SetProperty(ref _isSmartIdleModeEnabled, value)) return;
            ApplySorting();
            RefreshAndSortPlayerList();
        }
    }

    public string LockIconContent
    {
        get => _lockIconContent;
        private set => SetProperty(ref _lockIconContent, value);
    }

    public string LockMenuHeaderText
    {
        get => _lockMenuHeaderText;
        private set => SetProperty(ref _lockMenuHeaderText, value);
    }

    public string NotificationText
    {
        get => _notificationText;
        private set => SetProperty(ref _notificationText, value);
    }

    public string PauseButtonText
    {
        get => _pauseButtonText;
        private set => SetProperty(ref _pauseButtonText, value);
    }

    public Brush PauseStatusColor
    {
        get => _pauseStatusColor;
        private set => SetProperty(ref _pauseStatusColor, value);
    }

    private ObservableCollection<PlayerViewModel> Players
    {
        get => _players;
        set
        {
            if (SetProperty(ref _players, value)) PlayersView = CollectionViewSource.GetDefaultView(value);
        }
    }

    public ICollectionView PlayersView
    {
        get => _playersView;
        private set => SetProperty(ref _playersView, value);
    }

    public string SearchFilterText
    {
        get => _searchFilterText;
        set
        {
            if (!SetProperty(ref _searchFilterText, value)) return;
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
    }

    public SearchModeItem? SelectedSearchModeItem
    {
        get => _selectedSearchModeItem;
        set
        {
            if (!SetProperty(ref _selectedSearchModeItem, value)) return;
            ApplyFilter();
            OnPropertyChanged(nameof(SearchPlaceholderText));
        }
    }

    public FontFamily SelectedFontFamily
    {
        get => _selectedFontFamily;
        set => SetProperty(ref _selectedFontFamily, value);
    }

    public string? SortColumn
    {
        get => _sortColumn;
        set => SetProperty(ref _sortColumn, value);
    }

    public ListSortDirection SortDirection
    {
        get => _sortDirection;
        private set => SetProperty(ref _sortDirection, value);
    }

    public string? TakenDamageSumTooltip
    {
        get => _takenDamageSumTooltip;
        private set => SetProperty(ref _takenDamageSumTooltip, value);
    }

    public string? TotalDamageSumTooltip
    {
        get => _totalDamageSumTooltip;
        private set => SetProperty(ref _totalDamageSumTooltip, value);
    }

    public string? TotalDpsSumTooltip
    {
        get => _totalDpsSumTooltip;
        private set => SetProperty(ref _totalDpsSumTooltip, value);
    }

    public string? TotalHealingSumTooltip
    {
        get => _totalHealingSumTooltip;
        private set => SetProperty(ref _totalHealingSumTooltip, value);
    }

    public string? TotalHpsSumTooltip
    {
        get => _totalHpsSumTooltip;
        private set => SetProperty(ref _totalHpsSumTooltip, value);
    }

    public int UiUpdateInterval
    {
        get => _uiUpdateInterval;
        set
        {
            if (SetProperty(ref _uiUpdateInterval, value)) UpdateTimerInterval();
        }
    }

    public double WindowHeight
    {
        get => _windowHeight;
        set => SetProperty(ref _windowHeight, value);
    }

    public double WindowLeft
    {
        get => _windowLeft;
        set => SetProperty(ref _windowLeft, value);
    }

    public double WindowOpacity
    {
        get => _windowOpacity;
        set => SetProperty(ref _windowOpacity, value);
    }

    public double WindowTop
    {
        get => _windowTop;
        set => SetProperty(ref _windowTop, value);
    }

    public double WindowWidth
    {
        get => _windowWidth;
        set => SetProperty(ref _windowWidth, value);
    }

    // --- 手动实现的命令 ---
    public ICommand OpenCountdownOptionsCommand { get; }
    public ICommand OpenCustomCountdownCommand { get; }
    public ICommand StartCustomCountdownCommand { get; }
    public ICommand CancelCustomCountdownCommand { get; }
    public ICommand ConnectToBackendCommand { get; }
    public ICommand TogglePlayerExpansionCommand { get; }
    public ICommand SortByCommand { get; }
    public ICommand StartCountdownCommand { get; }
    public ICommand ToggleLockCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand ExitApplicationCommand { get; }
    public ICommand IncreaseFontSizeCommand { get; }
    public ICommand DecreaseFontSizeCommand { get; }
    public SnapshotViewModel SnapshotViewModel { get; }

    public ObservableCollection<SearchModeItem> LocalizedSearchModes { get; } = new();

    public string SearchPlaceholderText => SelectedSearchModeItem?.Mode switch
    {
        SearchMode.ById => Localization["Placeholder_ById"] ?? "...", // 移除了多余的 '?'
        SearchMode.ByName => Localization["Placeholder_ByName"] ?? "...", // 移除了多余的 '?'
        _ => "搜索..."
    };

    public Visibility SnapshotModeVisibility =>
        SnapshotViewModel.IsInSnapshotMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RealtimeModeVisibility =>
        SnapshotViewModel.IsInSnapshotMode ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CountdownRunningVisibility => IsCountdownRunning ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PauseStatusVisibility => IsPaused ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FightDurationVisibility =>
        !IsPaused && !IsCountdownRunning ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NotificationVisibility => IsNotificationVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVisibility => IsSettingsVisible ? Visibility.Visible : Visibility.Collapsed;
    public bool IsHitTestVisible => !IsLocked;

    public double FontOpacity
    {
        get => _fontOpacity;
        set => SetProperty(ref _fontOpacity, value);
    }

    public IEnumerable<FontFamily> SystemFonts { get; }

    public LocalizationService Localization { get; }

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

    private void SnapshotViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 检查是否是 IsInSnapshotMode 属性发生了变化
        if (e.PropertyName != nameof(SnapshotViewModel.IsInSnapshotMode)) return;
        // 如果是，就通知UI更新所有依赖于这个状态的属性
        OnPropertyChanged(nameof(RealtimeModeVisibility));
        OnPropertyChanged(nameof(SnapshotModeVisibility));
    }

    private void UpdateLocalizedSearchModes()
    {
        var currentMode = SelectedSearchModeItem?.Mode ?? SearchMode.ByName;
        LocalizedSearchModes.Clear();
        LocalizedSearchModes.Add(new SearchModeItem
            { Mode = SearchMode.ByName, DisplayName = Localization["SearchMode_ByName"] ?? "Name" });
        LocalizedSearchModes.Add(new SearchModeItem
            { Mode = SearchMode.ById, DisplayName = Localization["SearchMode_ById"] ?? "ID" });

        SelectedSearchModeItem = LocalizedSearchModes.FirstOrDefault(i => i.Mode == currentMode);
    }

    private async Task<SnapshotData?> OnRequestDataForSave()
    {
        // 确保所有玩家的技能数据都已获取
        foreach (var player in _playerCache.Values.Where(p => p.RawSkillData == null))
        {
            var skillData = await _apiService.GetSkillDataAsync(player.Uid);
            if (skillData?.Data != null) player.RawSkillData = skillData.Data;
        }

        return new SnapshotData
        {
            ElapsedSeconds = _elapsedSeconds,
            Players = _playerCache.Values.Select(p => new PlayerSnapshot
            {
                UserData = p.UserData!,
                SkillData = p.RawSkillData,
                DamagePercent = p.DamagePercent,
                HealingPercent = p.HealingPercent
            }).ToList()
        };
    }

    private async void OnSnapshotLoaded(SnapshotData snapshot)
    {
        try
        {
            // 1. 暂停实时服务和计时器
            if (IsPauseOnSnapshotEnabled)
            {
                await _apiService.SetPauseStateAsync(true);
                _apiService.DataReceived -= OnDataReceived;
            }

            _fightTimer.Stop();
            _uiUpdateTimer.Stop();

            // 2. 加载快照核心数据
            _elapsedSeconds = snapshot.ElapsedSeconds;
            var timeSpan = TimeSpan.FromSeconds(_elapsedSeconds);
            FightDurationText =
                timeSpan.TotalHours >= 1 ? timeSpan.ToString(@"h\:mm\:ss") : timeSpan.ToString(@"m\:ss");
            _isFightActive = true;

            var newPlayerList = snapshot.Players
                .Select(ps => new PlayerViewModel(ps, FightDurationText, Localization, this)).ToList();
            var newPlayerCache = newPlayerList.ToDictionary(p => p.Uid.ToString(), p => p);

            _playerCache.Clear();
            foreach (var entry in newPlayerCache) _playerCache.Add(entry.Key, entry.Value);
            Players = new ObservableCollection<PlayerViewModel>(newPlayerList);

            RefreshAndSortPlayerList();
            OnPropertyChanged(nameof(PlayerCount));

            // 3. 成功加载后的收尾工作 (从 finally 移至此处)
            if (IsPauseOnSnapshotEnabled) IsPaused = true; // 更新UI为暂停状态
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Error loading snapshot: {e.Message}");
            // 确保添加了 "Error_LoadingSnapshot" 到你的 resx 文件中
            ShowNotification(Localization["Error_LoadingSnapshot"] ?? "加载快照时出错");

            // 如果加载失败，最好是直接退出快照模式
            SnapshotViewModel.ExitSnapshotModeCommand.Execute(null);
        }
    }

    private async void OnExitedSnapshotMode()
    {
        try
        {
            // 这是从旧的 ResetDataAsync 方法中处理退出快照模式的逻辑
            _fightTimer.Stop();
            _elapsedSeconds = 0;
            FightDurationText = "0:00";
            _isFightActive = false;
            _lastCombatActivityTime = DateTime.MinValue;

            Players.Clear();
            _playerCache.Clear();

            if (IsPauseOnSnapshotEnabled)
            {
                var (success, isPaused) = await _apiService.GetPauseStateAsync();
                if (success && isPaused) IsPaused = true;
                else IsPaused = false;
            }

            var initialData = await _apiService.GetInitialDataAsync();
            if (initialData != null) await ProcessData(initialData);

            _apiService.DataReceived += OnDataReceived;
            _uiUpdateTimer.Start();
            await _apiService.ConnectAsync();
            ShowNotification("已返回实时模式");
            OnPropertyChanged(nameof(PlayerCount));
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Error exiting snapshot mode: {e.Message}");
        }
    }


    private void ApplyFilter()
    {
        if (SelectedSearchModeItem is null) return;

        var filterText = SearchFilterText.Trim();

        if (string.IsNullOrEmpty(filterText))
        {
            foreach (var player in Players) player.IsMatchInFilter = true;

            return;
        }

        foreach (var player in Players)
            player.IsMatchInFilter = SelectedSearchModeItem.Mode == SearchMode.ByName
                ? player.DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase)
                : player.Uid.ToString().StartsWith(filterText);
    } //用于替换 IValueConverter 的计算属性


    private async void SkillUpdateTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // 如果存在一个已展开的玩家，并且我们不在快照模式下，则刷新其技能数据
            if (_expandedPlayer != null && !SnapshotViewModel.IsInSnapshotMode)
                await FetchAndProcessSkillDataAsync(_expandedPlayer);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Error updating skills: {error.Message}");
            ShowNotification(Localization["Error_UpdatingSkills"] ?? "更新技能数据时出错");
        }
    }

    /// <summary>
    ///     将当前的排序规则应用到 PlayersView。
    /// </summary>
    private void ApplySorting()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            PlayersView.SortDescriptions.Clear();

            // 如果启用了闲置模式，总是先按是否闲置排序（不闲置的在前）
            if (IsSmartIdleModeEnabled)
                PlayersView.SortDescriptions.Add(new SortDescription(nameof(PlayerViewModel.IsIdle),
                    ListSortDirection.Ascending));

            // 根据用户选择的列进行主排序
            if (!string.IsNullOrEmpty(SortColumn))
                PlayersView.SortDescriptions.Add(new SortDescription(SortColumn, SortDirection));
        });
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
    private void OpenCountdownOptions()
    {
        IsCountdownOptionsPopupOpen = true;
    }

    /// <summary>
    ///     打开自定义倒计时弹窗。
    /// </summary>
    private void OpenCustomCountdown()
    {
        IsCountdownOptionsPopupOpen = false;
        IsCustomCountdownPopupOpen = true;
    }


    private async Task StartCustomCountdown()
    {
        if (double.TryParse(CustomCountdownMinutes, out var minutes) && minutes is > 0 and <= 60)
        {
            await StartCountdown(((int)(minutes * 60)).ToString());
            IsCustomCountdownPopupOpen = false;
        }
        else
        {
            ShowNotification(Localization["Error_InvalidCountdownMinutes"] ?? "请输入一个有效的分钟数 (1-60)");
        }
    }

    /// <summary>
    ///     取消自定义倒计时并关闭弹窗。
    /// </summary>
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
                    FontOpacity = state.FontOpacity > 0 ? state.FontOpacity : 1.0;
                    FontSize = state.FontSize;
                    var loadedFont = SystemFonts.FirstOrDefault(f =>
                        f.Source.Equals(state.FontFamilySource, StringComparison.OrdinalIgnoreCase));
                    SelectedFontFamily = loadedFont ?? new FontFamily("Microsoft YaHei");
                    IsSmartIdleModeEnabled = state.IsSmartIdleModeEnabled;
                    BackendUrl = state.BackendUrl;
                    UiUpdateInterval = state.UiUpdateInterval;
                    IsPauseOnExitEnabled = state.PauseOnExit;
                    IsPauseOnSnapshotEnabled = state.PauseOnSnapshot;
                    WindowTop = state.WindowTop;
                    WindowLeft = state.WindowLeft;
                    WindowHeight = state.WindowHeight > 0 ? state.WindowHeight : 350; // 防止加载到0或负数
                    WindowWidth = state.WindowWidth > 0 ? state.WindowWidth : 700;

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
                FontOpacity = FontOpacity,
                FontSize = FontSize,
                FontFamilySource = SelectedFontFamily.Source,
                IsSmartIdleModeEnabled = IsSmartIdleModeEnabled,
                BackendUrl = BackendUrl,
                UiUpdateInterval = UiUpdateInterval,
                CultureName = Localization.CurrentCulture.Name,
                PauseOnExit = IsPauseOnExitEnabled,
                PauseOnSnapshot = IsPauseOnSnapshotEnabled,
                SortColumn = SortColumn,
                SortDirection = SortDirection,
                WindowTop = WindowTop,
                WindowLeft = WindowLeft,
                WindowHeight = WindowHeight,
                WindowWidth = WindowWidth
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

    /// <summary>
    ///     将一个double类型的数值格式化为易于阅读的字符串，使用K, M, G等单位。
    /// </summary>
    /// <param name="num">要格式化的数值。</param>
    /// <returns>格式化后的字符串。</returns>
    public static string FormatNumber(double num)
    {
        return num switch
        {
            >= 1_000_000_000 => $"{num / 1_000_000_000:0.##}G",
            >= 1_000_000 => $"{num / 1_000_000:0.##}M",
            >= 10_000 => $"{num / 10_000:0.#}W",
            >= 1_000 => $"{num / 1_000:0.##}K",
            _ => num.ToString("N0")
        };
    }


    private async Task ConnectToBackendAsync()
    {
        // 检查是否已连接到相同的地址
        if (_apiService.IsConnected && _apiService.CurrentUrl.Equals(BackendUrl, StringComparison.OrdinalIgnoreCase))
        {
            ShowNotification("已经连接到该服务地址");
            return;
        }

        ConnectionStatusText = Localization["Connecting"] ?? "正在连接...";
        ConnectionStatusColor = Brushes.Orange;
        await _apiService.ReinitializeAsync(BackendUrl);
        var isRunning = await _apiService.CheckServiceRunningAsync();
        if (isRunning)
        {
            _fightTimer.Stop();
            _elapsedSeconds = 0;
            FightDurationText = "0:00";
            _isFightActive = false;
            _lastCombatActivityTime = DateTime.MinValue;
            // 清空本地玩家列表
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Players.Clear();
                _playerCache.Clear();
                OnPropertyChanged(nameof(PlayerCount));
                UpdateColumnTotals();
            });
            // 从新服务器获取初始数据
            var initialData = await _apiService.GetInitialDataAsync();
            if (initialData != null) await ProcessData(initialData);

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


    private async Task TogglePlayerExpansion(PlayerViewModel player)
    {
        if (player.IsExpanded)
        {
            player.IsExpanded = false;
            _expandedPlayer = null;
            _skillUpdateTimer.Stop();

            _isSortingPaused = false;
            RefreshAndSortPlayerList();
            return;
        }

        // 如果当前有其他玩家处于展开状态，先将其折叠
        if (_expandedPlayer is not null && _expandedPlayer != player) _expandedPlayer.IsExpanded = false;

        // 展开被点击的玩家
        player.IsExpanded = true;
        _expandedPlayer = player;
        _isSortingPaused = true; //展开详情时，启动排序暂停

        // 无论是主动获取的还是第一次展开，都强制刷新一次以获取最新数据
        await FetchAndProcessSkillDataAsync(player);

        // 展开后启动定时器
        _skillUpdateTimer.Start();
    }

    private async Task FetchAndProcessSkillDataAsync(PlayerViewModel player)
    {
        //增加快照模式下的逻辑
        if (SnapshotViewModel.IsInSnapshotMode)
        {
            // 在快照模式下，从已加载的 RawSkillData 填充技能列表
            await Application.Current.Dispatcher.InvokeAsync(() => player.Skills.Clear());

            if (player.RawSkillData?.Skills == null) return; // 快照模式处理完毕，直接返回
            var playerTotalValue = player.TotalDamage + player.TotalHealing;
            var skills = player.RawSkillData.Skills.Values
                .OrderByDescending(s => s.TotalDamage)
                .Take(6)
                .Select(s => new SkillViewModel(s, playerTotalValue));

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var skillVm in skills) player.Skills.Add(skillVm);
                player.NotifySkillsChanged();
            });

            return; // 快照模式处理完毕，直接返回
        }

        try
        {
            player.IsFetchingSkillData = true;
            // 在UI线程上更新加载状态
            await Application.Current.Dispatcher.InvokeAsync(() => player.SetLoadingSkills(true));

            var skillDataResponse = await _apiService.GetSkillDataAsync(player.Uid);

            // 只有当新获取的数据有效且包含技能时，才更新缓存的RawSkillData
            // 这可以防止闲置玩家的空数据覆盖掉最后一次的有效数据
            if (skillDataResponse?.Data?.Skills.Count > 0)
            {
                player.RawSkillData = skillDataResponse.Data;
                player.NotifyTooltipUpdate();
            }

            // 无论如何，都尝试使用当前持有的RawSkillData来更新UI
            if (player.RawSkillData?.Skills != null)
            {
                var playerTotalValue = player.TotalDamage + player.TotalHealing;
                var skills = player.RawSkillData.Skills.Values
                    .OrderByDescending(s => s.TotalDamage)
                    .Take(6)
                    .Select(s => new SkillViewModel(s, playerTotalValue));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    player.Skills.Clear();
                    foreach (var skillVm in skills) player.Skills.Add(skillVm);
                    player.NotifySkillsChanged();
                });

                player.CalculateAccurateCritDamage(player.RawSkillData.Skills);
                player.CalculateAccurateCritHealing(player.RawSkillData.Skills);
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


    private void UpdateTimerInterval()
    {
        _uiUpdateTimer.Interval = TimeSpan.FromMilliseconds(UiUpdateInterval);
    }

    private void OnDataReceived(ApiResponse data)
    {
        if (SnapshotViewModel.IsInSnapshotMode)
        {
            // 在快照模式下，如果仍在接收数据，则将其缓存但不处理
            lock (_dataLock)
            {
            }

            return;
        }

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
                if (changes.ListNeedsRefresh) RefreshAndSortPlayerList();
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
            // 在进行任何网络操作前，使用加载的 BackendUrl 重新初始化 ApiService
            await _apiService.ReinitializeAsync(BackendUrl);
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
                    if (_playerEntryTimes.TryGetValue(key, out var entryTime)) newPlayerEntryTimes.Add(key, entryTime);
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
                RefreshAndSortPlayerList();
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

        // 始终通知 PlayerCount 可能已更改，确保UI实时同步
        OnPropertyChanged(nameof(PlayerCount));

        // 返回一个包含两个布尔值的元组
        return (hasDataChanged, aPlayerWokeUp || listStructureChanged);
    }

    /// <summary>
    ///     对当前玩家列表进行排序、计算百分比并高效地更新UI，取代旧的UpdatePlayerList方法。
    /// </summary>
    private void RefreshAndSortPlayerList()
    {
        if (_isSortingPaused) return;

        // 复制到List<T>以便在内存中高效排序
        var sortedPlayers = Players.ToList();

        // 根据当前设置在内存中排序
        // 主排序规则
        if (!string.IsNullOrEmpty(SortColumn))
        {
            var property = typeof(PlayerViewModel).GetProperty(SortColumn);
            if (property != null)
                sortedPlayers = SortDirection == ListSortDirection.Ascending
                    ? sortedPlayers.OrderBy(p => property.GetValue(p)).ToList()
                    : sortedPlayers.OrderByDescending(p => property.GetValue(p)).ToList();
        }

        // 优先排序规则（闲置模式）
        if (IsSmartIdleModeEnabled) sortedPlayers = sortedPlayers.OrderBy(p => p.IsIdle).ToList();

        // 3. 计算百分比和排名
        var playersForCalcs = IsSmartIdleModeEnabled ? sortedPlayers.Where(p => !p.IsIdle).ToList() : sortedPlayers;

        var totalDamage = playersForCalcs.Sum(p => p.TotalDamage);
        var totalHealing = playersForCalcs.Sum(p => p.TotalHealing);
        var totalDps = playersForCalcs.Sum(p => p.TotalDps);
        var totalHps = playersForCalcs.Sum(p => p.TotalHps);
        var totalTakenDamage = playersForCalcs.Sum(p => p.TakenDamage);

        var rank = 1;
        foreach (var player in sortedPlayers)
        {
            player.Rank = IsSmartIdleModeEnabled && player.IsIdle ? 0 : rank++;

            if (IsSmartIdleModeEnabled && player.IsIdle)
            {
                player.DamagePercent = 0;
                player.HealingPercent = 0;
                player.UpdateDisplayPercentages(0, 0, 0, 0, 0, null);
                continue;
            }

            player.DamagePercent = totalDamage > 0 ? player.TotalDamage / totalDamage : 0;
            player.HealingPercent = totalHealing > 0 ? player.TotalHealing / totalHealing : 0;

            var dpsPct = totalDps > 0 ? player.TotalDps / totalDps * 100 : 0;
            var hpsPct = totalHps > 0 ? player.TotalHps / totalHps * 100 : 0;
            var takenDamagePct = totalTakenDamage > 0 ? player.TakenDamage / totalTakenDamage * 100 : 0;

            player.UpdateDisplayPercentages(player.DamagePercent * 100, player.HealingPercent * 100, dpsPct, hpsPct,
                takenDamagePct, SortColumn);
        }

        // 4. 一次性更新UI
        Players = new ObservableCollection<PlayerViewModel>(sortedPlayers);

        UpdateColumnTotals();
    }


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
        RefreshAndSortPlayerList();
    }


    public async Task ResetDataAsync()
    {
        _skillUpdateTimer.Stop();
        _expandedPlayer = null;
        _isSortingPaused = false; //确保重置时解除排序暂停
        //处理非快照模式下的重置逻辑
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
            // 使用ProcessData方法将数据一次性加载到UI
            await ProcessData(refreshedData);
    }


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


    public async Task AbortCountdownAsync()
    {
        if (!IsCountdownRunning) return;
        _countdownTimer?.Stop();
        IsCountdownRunning = false;
        CountdownText = "倒计时";
        if (!IsPaused) await TogglePauseAsync();
        ShowNotification("倒计时已中断");
    }


    public void ToggleLock()
    {
        IsLocked = !IsLocked;
    }


    private void ToggleSettings()
    {
        IsSettingsVisible = !IsSettingsVisible;
    }

    public async Task PauseOnExitAsync()
    {
        // 仅当复选框被选中且当前未暂停时，才发送暂停指令
        if (IsPauseOnExitEnabled && !IsPaused) await _apiService.SetPauseStateAsync(true);
    }


    private static void ExitApplication()
    {
        Application.Current.Shutdown();
    }


    private void IncreaseFontSize()
    {
        if (FontSize < 24) FontSize++;
    }


    private void DecreaseFontSize()
    {
        if (FontSize > 10) FontSize--;
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
        var needsRefresh = false;

        foreach (var player in _playerCache.Values)
        {
            // 如果玩家当前不闲置，但上次活跃时间已经超过阈值
            if (player.IsIdle || !((now - player.LastActiveTime).TotalSeconds > IdleTimeoutSeconds)) continue;
            player.IsIdle = true;
            // 将闲置玩家的百分比清空
            player.DamageDisplayPercentage = null;
            player.HealingDisplayPercentage = null;
            player.TakenDamageDisplayPercentage = null;

            // 标记列表需要刷新排序
            needsRefresh = true;
        }

        // 如果有玩家的状态变为了闲置，则调用 UpdatePlayerList 来刷新UI
        if (needsRefresh) RefreshAndSortPlayerList();
    }

    // 为本地化的ComboBox创建数据结构
    public class SearchModeItem
    {
        public SearchMode Mode { get; init; }
        public string DisplayName { get; init; } = string.Empty;
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