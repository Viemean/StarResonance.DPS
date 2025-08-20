using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StarResonance.DPS.Models;
using StarResonance.DPS.Services;

namespace StarResonance.DPS.ViewModels;

/// <summary>
///     表示单个玩家数据及其在UI中状态的视图模型。
/// </summary>
public class PlayerViewModel : ObservableObject
{
    // --- 服务字段 ---
    private readonly LocalizationService _localizationService;
    private readonly INotificationService _notificationService;

    // --- 手动实现的属性 ---
    private string? _accurateCritDamageText;

    private string? _accurateCritHealingText;

    // --- 缓存字段 ---
    private string? _cachedCopyableString;
    private string? _cachedToolTipText;

    private string? _damageDisplayPercentage;

    private UserData? _data;

    private string? _dpsDisplayPercentage;

    private string _fightDuration = "0:00";

    private int _fightPoint;

    private string? _healingDisplayPercentage;

    private string? _hpsDisplayPercentage;

    private bool _isExpanded;

    private bool _isIdle;

    private bool _isLoadingSkills;

    private bool _isMatchInFilter = true;

    private string _name = null!;

    private Brush _nameColor = Brushes.White;

    private string _profession = null!;

    private int _rank;

    private bool _showSeparatorAfter;

    private double _takenDamage;

    private string? _takenDamageDisplayPercentage;

    private double _totalDamage;

    private double _totalDps;

    private double _totalHealing;

    private double _totalHps;

    /// <summary>
    ///     表示单个玩家数据及其在UI中状态的视图模型。
    /// </summary>
    /// <param name="uid">玩家的唯一ID。</param>
    /// <param name="localizationService">用于UI文本本地化的服务。</param>
    /// <param name="notificationService">用于显示通知的服务。</param>
    public PlayerViewModel(
        long uid,
        LocalizationService localizationService,
        INotificationService notificationService)
    {
        Uid = uid;
        _localizationService = localizationService;
        _notificationService = notificationService;

        CopyDataCommand = new RelayCommand(_ => CopyData());
    }


    /// <summary>
    ///     用于从快照数据创建玩家视图模型的构造函数。
    /// </summary>
    /// <param name="snapshot">包含玩家原始数据和技能数据的快照对象。</param>
    /// <param name="fightDuration">战斗持续时间的字符串表示。</param>
    /// <param name="localizationService">本地化服务实例。</param>
    /// <param name="notificationService">通知服务实例。</param>
    public PlayerViewModel(PlayerSnapshot snapshot, string fightDuration, LocalizationService localizationService,
            INotificationService notificationService)
        // 从 SkillData 中安全地获取 Uid，如果不存在则默认为0
        : this(snapshot.SkillData?.Uid ?? 0, localizationService, notificationService)
    {
        Update(snapshot.UserData, 0, fightDuration);
        RawSkillData = snapshot.SkillData;

        // 从快照中恢复百分比数据
        DamagePercent = snapshot.DamagePercent;
        HealingPercent = snapshot.HealingPercent;

        //直接访问 RawSkillData.Skills
        if (RawSkillData?.Skills == null) return;
        var playerTotalValue = TotalDamage + TotalHealing;
        //直接访问 RawSkillData.Skills
        var skills = RawSkillData.Skills.Values
            .OrderByDescending(s => s.TotalDamage)
            .Take(6)
            .Select(s => new SkillViewModel(s, playerTotalValue));
        foreach (var skillVm in skills) Skills.Add(skillVm);
        // 直接访问 RawSkillData.Skills
        CalculateAccurateCritDamage(RawSkillData.Skills);
        CalculateAccurateCritHealing(RawSkillData.Skills);

        // 在快照模式下预先计算并缓存
        _cachedToolTipText = BuildToolTipText();
        _cachedCopyableString = BuildCopyableString();
        if (NameColor.CanFreeze) NameColor.Freeze();
    }

    public string? AccurateCritDamageText
    {
        get => _accurateCritDamageText;
        set => SetProperty(ref _accurateCritDamageText, value);
    }

    public string? AccurateCritHealingText
    {
        get => _accurateCritHealingText;
        set => SetProperty(ref _accurateCritHealingText, value);
    }

    public string? DamageDisplayPercentage
    {
        get => _damageDisplayPercentage;
        set => SetProperty(ref _damageDisplayPercentage, value);
    }

    public string? DpsDisplayPercentage
    {
        get => _dpsDisplayPercentage;
        private set => SetProperty(ref _dpsDisplayPercentage, value);
    }

    public int FightPoint
    {
        get => _fightPoint;
        set => SetProperty(ref _fightPoint, value);
    }

    public string? HealingDisplayPercentage
    {
        get => _healingDisplayPercentage;
        set => SetProperty(ref _healingDisplayPercentage, value);
    }

    public string? HpsDisplayPercentage
    {
        get => _hpsDisplayPercentage;
        private set => SetProperty(ref _hpsDisplayPercentage, value);
    }

    public bool IsIdle
    {
        get => _isIdle;
        set => SetProperty(ref _isIdle, value);
    }

    public bool IsMatchInFilter
    {
        get => _isMatchInFilter;
        set => SetProperty(ref _isMatchInFilter, value);
    }

    public bool IsLoadingSkills
    {
        get => _isLoadingSkills;
        set => SetProperty(ref _isLoadingSkills, value);
    }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public Brush NameColor
    {
        get => _nameColor;
        set => SetProperty(ref _nameColor, value);
    }

    public string Profession
    {
        get => _profession;
        set => SetProperty(ref _profession, value);
    }

    public int Rank
    {
        get => _rank;
        set => SetProperty(ref _rank, value);
    }

    public bool ShowSeparatorAfter
    {
        get => _showSeparatorAfter;
        set => SetProperty(ref _showSeparatorAfter, value);
    }

    public double TakenDamage
    {
        get => _takenDamage;
        set => SetProperty(ref _takenDamage, value);
    }

    public string? TakenDamageDisplayPercentage
    {
        get => _takenDamageDisplayPercentage;
        set => SetProperty(ref _takenDamageDisplayPercentage, value);
    }

    public double TotalDamage
    {
        get => _totalDamage;
        set => SetProperty(ref _totalDamage, value);
    }

    public double TotalDps
    {
        get => _totalDps;
        set => SetProperty(ref _totalDps, value);
    }

    public double TotalHealing
    {
        get => _totalHealing;
        set => SetProperty(ref _totalHealing, value);
    }

    public double TotalHps
    {
        get => _totalHps;
        set => SetProperty(ref _totalHps, value);
    }

    public double DamagePercent { get; set; }
    public double HealingPercent { get; set; }
    public int MaxHp { get; private set; }

    public ICommand CopyDataCommand { get; }

    /// <summary>
    ///     一个锁标志，防止对同一个玩家同时发起多个数据请求。
    /// </summary>
    public bool IsFetchingSkillData { get; set; }

    public UserData? UserData { get; private set; }
    public SkillApiResponseData? RawSkillData { get; set; }
    public string DisplayName => Name;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;

            // 当玩家详情被展开时，设置标志，停止后续的自动刷新
            if (value)
            {
            }

            OnPropertyChanged(nameof(ExpandedVisibility)); // 通知UI更新
        }
    }

    // 用于技能展开区域的可见性属性
    public Visibility ExpandedVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    public bool HasFewSkills => Skills.Count <= 3;

    /// <summary>
    ///     控制暴击伤害分析文本的可见性。
    /// </summary>
    public Visibility CritDamageVisibility =>
        !string.IsNullOrEmpty(AccurateCritDamageText) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    ///     控制暴击治疗分析文本的可见性。
    /// </summary>
    public Visibility CritHealingVisibility =>
        !string.IsNullOrEmpty(AccurateCritHealingText) ? Visibility.Visible : Visibility.Collapsed;

    public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;
    public ObservableCollection<SkillViewModel> Skills { get; } = new();

    public long Uid { get; }
    public string TotalDamageText => TotalDamage > 0 ? MainViewModel.FormatNumber(TotalDamage) : string.Empty;
    public string TotalHealingText => TotalHealing > 0 ? MainViewModel.FormatNumber(TotalHealing) : string.Empty;
    public string TotalDpsText => TotalDps > 0 ? MainViewModel.FormatNumber(TotalDps) : string.Empty;
    public string TotalHpsText => TotalHps > 0 ? MainViewModel.FormatNumber(TotalHps) : string.Empty;
    public string TakenDamageText => TakenDamage > 0 ? MainViewModel.FormatNumber(TakenDamage) : string.Empty;

    public string TotalDamageTooltip => TotalDamage.ToString("N0");
    public string TotalHealingTooltip => TotalHealing.ToString("N0");
    public string TotalDpsTooltip => TotalDps.ToString("N2");
    public string TotalHpsTooltip => TotalHps.ToString("N2");
    public string TakenDamageTooltip => TakenDamage.ToString("N0");

    //  ToolTipText get访问器
    public string ToolTipText => _cachedToolTipText ?? BuildToolTipText();

    public string DisplayProfession
    {
        get
        {
            if (string.IsNullOrEmpty(Profession)) return string.Empty;
            var parts = Profession.Split('-');
            return parts.Length > 1 ? parts[1] : parts[0];
        }
    }

    //  CopyableString get访问器
    public string CopyableString => _cachedCopyableString ?? BuildCopyableString();

    // 构建ToolTipText的私有方法
    private string BuildToolTipText()
    {
        if (_data == null) return string.Empty;

        var sb = new StringBuilder();
        var unknownText = _localizationService["Unknown"] ?? "未知";

        // 基础信息
        sb.AppendLine($"{_localizationService["Tooltip_PlayerId"] ?? "角色ID: "}{Uid}");
        sb.AppendLine($"{_localizationService["Tooltip_PlayerName"] ?? "角色昵称: "}{Name}");

        //静态显示角色等级、臂章等级和最大生命值
        var attr = RawSkillData?.Attr;

        var levelText = attr is { Level: > 0 } ? attr.Level.ToString() : unknownText;
        sb.AppendLine($"{(_localizationService["Tooltip_CharacterLevel"] ?? "角色等级:").TrimEnd(':')} {levelText}");

        var rankLevelText = attr is { RankLevel: > 0 } ? attr.RankLevel.ToString() : unknownText;
        sb.AppendLine($"{(_localizationService["Tooltip_RankLevel"] ?? "臂章等级:").TrimEnd(':')} {rankLevelText}");

        var maxHpText = MaxHp > 0 ? MaxHp.ToString("N0") : unknownText;
        sb.AppendLine($"{(_localizationService["Tooltip_MaxHP"] ?? "最大生命值:").TrimEnd(':')} {maxHpText}");

        // 评分和职业
        sb.AppendLine($"{_localizationService["Tooltip_Score"] ?? "评分: "}{FightPoint}");
        sb.AppendLine($"{_localizationService["Tooltip_Profession"] ?? "职业: "}{Profession}");

        // 暴击率和幸运率
        var critRate = _data.TotalCount.Total > 0 ? (double)_data.TotalCount.Critical / _data.TotalCount.Total : 0;
        sb.AppendLine($"{_localizationService["Tooltip_CritRate"] ?? "暴击率: "}{critRate:P1}");

        var luckyRate = _data.TotalCount.Total > 0 ? (double)_data.TotalCount.Lucky / _data.TotalCount.Total : 0;
        sb.AppendLine($"{_localizationService["Tooltip_LuckyRate"] ?? "幸运率: "}{luckyRate:P1}");

        return sb.ToString().TrimEnd();
    }

    // 构建CopyableString的私有方法
    private string BuildCopyableString()
    {
        if (_data == null) return string.Empty;
        var critRate = _data.TotalCount.Total > 0 ? (double)_data.TotalCount.Critical / _data.TotalCount.Total : 0;
        var luckyRate = _data.TotalCount.Total > 0 ? (double)_data.TotalCount.Lucky / _data.TotalCount.Total : 0;

        var rankLabel = _localizationService["Rank"] ?? "#";
        var idLabel = (_localizationService["Tooltip_PlayerId"] ?? "角色ID: ").TrimEnd(':', ' '); // 复用Tooltip的翻译
        var nameLabel = _localizationService["CharacterName"] ?? "Name";
        var scoreLabel = _localizationService["Score"] ?? "Score";
        var professionLabel = _localizationService["Profession"] ?? "Profession";
        var totalDamageLabel = _localizationService["TotalDamage"] ?? "Damage";
        var totalHealingLabel = _localizationService["TotalHealing"] ?? "Healing";
        var dpsLabel = _localizationService["TotalDPS"] ?? "DPS";
        var hpsLabel = _localizationService["TotalHPS"] ?? "HPS";
        var takenDamageLabel = _localizationService["TakenDamage"] ?? "DmgTaken";
        var critRateLabel = (_localizationService["Tooltip_CritRate"] ?? "Crit Rate: ").TrimEnd(':', ' ');
        var luckyRateLabel = (_localizationService["Tooltip_LuckyRate"] ?? "Lucky Rate: ").TrimEnd(':', ' ');
        var durationLabel = _localizationService["FightDuration"] ?? "Duration";

        // 在伤害和治疗后添加百分比
        var damageString = TotalDamage > 0 ? $"{TotalDamage:F0} ({DamagePercent:P1})" : $"{TotalDamage:F0}";
        var healingString = TotalHealing > 0 ? $"{TotalHealing:F0} ({HealingPercent:P1})" : $"{TotalHealing:F0}";


        return $"{rankLabel}: {Rank}, " +
               $"{idLabel}: {Uid}, " +
               $"{nameLabel}: {DisplayName}, " +
               $"{scoreLabel}: {FightPoint}, " +
               $"{professionLabel}: {Profession}, " +
               $"{totalDamageLabel}: {damageString}, " +
               $"{totalHealingLabel}: {healingString}, " +
               $"{dpsLabel}: {TotalDps:F2}, " +
               $"{hpsLabel}: {TotalHps:F2}, " +
               $"{takenDamageLabel}: {TakenDamage:F0}, " +
               $"{critRateLabel}: {critRate:P1}, " +
               $"{luckyRateLabel}: {luckyRate:P1}, " +
               $"{durationLabel}: {_fightDuration}";
    }

    public void NotifyTooltipUpdate()
    {
        // 在实时模式下，清除缓存以强制重新计算
        _cachedToolTipText = null;
        OnPropertyChanged(nameof(ToolTipText));
    }

    public void NotifySkillsChanged()
    {
        OnPropertyChanged(nameof(Skills));
        OnPropertyChanged(nameof(HasFewSkills));
    }

    /// <summary>
    ///     计算并返回指定类型技能的精确暴击/暴疗加成百分比文本。
    /// </summary>
    /// <param name="skillType">要计算的技能类型 ("伤害" 或 "治疗")。</param>
    /// <returns>格式化为百分比的加成字符串，或在数据不足时返回"不适用"。</returns>
    private string CalculateAccurateCritBonus(string skillType)
    {
        const int minSamples = 2;
        var multipliers = new List<(double multiplier, double weight)>();

        // 根据传入的skillType筛选出同时具有普通和暴击记录的有效技能
        var validSkills = RawSkillData?.Skills.Values.Where(s =>
            s is { Type: var type, CountBreakdown: { Normal: >= minSamples, Critical: >= minSamples } } &&
            type == skillType).ToList();

        if (validSkills != null)
            multipliers.AddRange(from skill in validSkills
                let avgNormal = skill.DamageBreakdown.Normal / skill.CountBreakdown.Normal
                where !(avgNormal <= 0)
                let totalCritValue = skill.DamageBreakdown.Critical + skill.DamageBreakdown.CritLucky
                let avgCrit = totalCritValue / skill.CountBreakdown.Critical
                select (avgCrit / avgNormal, skill.TotalDamage));

        // 基于所有有效技能的数据，进行加权平均计算
        if (multipliers.Count == 0) return _localizationService["NotApplicable"] ?? "数据不足";
        var totalWeight = multipliers.Sum(t => t.weight);
        if (!(totalWeight > 0)) return _localizationService["NotApplicable"] ?? "数据不足";
        {
            var totalWeightedMultiplier = multipliers.Sum(t => t.multiplier * t.weight);
            var weightedAvgMultiplier = totalWeightedMultiplier / totalWeight;
            var bonus = weightedAvgMultiplier - 1;
            return $"{bonus:P1}";
        }
    }

    /// <summary>
    ///     计算精确的暴击伤害加成。
    /// </summary>
    /// <param name="skillsData">用于计算的技能数据字典。</param>
    public void CalculateAccurateCritDamage(IReadOnlyDictionary<string, SkillData> skillsData)
    {
        AccurateCritDamageText = CalculateAccurateCritBonus("伤害");
        OnPropertyChanged(nameof(CritDamageVisibility));
        OnPropertyChanged(nameof(ToolTipText));
    }

    /// <summary>
    ///     计算精确的暴击治疗加成。
    /// </summary>
    /// <param name="skillsData">用于计算的技能数据字典。</param>
    public void CalculateAccurateCritHealing(IReadOnlyDictionary<string, SkillData> skillsData)
    {
        AccurateCritHealingText = CalculateAccurateCritBonus("治疗");
        OnPropertyChanged(nameof(CritHealingVisibility));
        OnPropertyChanged(nameof(ToolTipText));
    }

    private void CopyData()
    {
        try
        {
            Clipboard.SetText(CopyableString);
            _notificationService.ShowNotification(_localizationService["CopySuccess"] ?? "复制成功!");
        }
        catch
        {
            _notificationService.ShowNotification(_localizationService["CopyFailed"] ?? "复制失败: 无法访问剪贴板");
        }
    }

    public void SetLoadingSkills(bool isLoading)
    {
        IsLoadingSkills = isLoading;
    }

    public void Update(UserData data, int rank, string fightDuration)
    {
        var oldName = Name;
        var oldProfession = Profession;
        var oldFightPoint = FightPoint;
        var oldTotalCount = UserData?.TotalCount;

        UserData = data;
        _data = data;
        _fightDuration = fightDuration;

        Rank = rank;
        Name = string.IsNullOrEmpty(data.Name) ? Uid.ToString() : data.Name;
        FightPoint = data.FightPoint;
        Profession = data.Profession;
        TotalDamage = data.TotalDamage.Total;
        TotalHealing = data.TotalHealing.Total;
        TotalDps = data.TotalDps;
        TotalHps = data.TotalHps;
        TakenDamage = data.TakenDamage;
        MaxHp = data.MaxHp;

        if (oldName != Name ||
            oldProfession != Profession ||
            oldFightPoint != FightPoint ||
            oldTotalCount != data.TotalCount)
        {
            // 在实时模式下，清除缓存以强制重新计算
            _cachedToolTipText = null;
            _cachedCopyableString = null;
            OnPropertyChanged(nameof(ToolTipText));
        }

        OnComputedPropertiesChanged();
    }

    /// <summary>
    ///     批量更新百分比属性
    /// </summary>
    /// <param name="damagePct">总伤害</param>
    /// <param name="healingPct">总治疗</param>
    /// <param name="dpsPct">总DPS</param>
    /// <param name="hpsPct">总HPS</param>
    /// <param name="takenDamagePct">承伤</param>
    /// <param name="sortColumn">排序</param>
    public void UpdateDisplayPercentages(double damagePct, double healingPct, double dpsPct, double hpsPct,
        double takenDamagePct, string? sortColumn)
    {
        DamageDisplayPercentage = null;
        HealingDisplayPercentage = null;
        DpsDisplayPercentage = null;
        HpsDisplayPercentage = null;
        TakenDamageDisplayPercentage = takenDamagePct >= 1 ? $" {takenDamagePct:F0}%" : null;

        switch (sortColumn)
        {
            case MainViewModel.SortableColumns.TotalDamage:
                DamageDisplayPercentage = damagePct >= 1 ? $" {damagePct:F0}%" : null;
                break;
            case MainViewModel.SortableColumns.TotalHealing:
                HealingDisplayPercentage = healingPct >= 1 ? $" {healingPct:F0}%" : null;
                break;
            case MainViewModel.SortableColumns.TotalDps:
                DpsDisplayPercentage = dpsPct >= 1 ? $" {dpsPct:F0}%" : null;
                break;
            case MainViewModel.SortableColumns.TotalHps:
                HpsDisplayPercentage = hpsPct >= 1 ? $" {hpsPct:F0}%" : null;
                break;
        }
    }

    private void OnComputedPropertiesChanged()
    {
        OnPropertyChanged(nameof(CopyableString));
        OnPropertyChanged(nameof(TotalDamageText));
        OnPropertyChanged(nameof(TotalHealingText));
        OnPropertyChanged(nameof(TotalDpsText));
        OnPropertyChanged(nameof(TotalHpsText));
        OnPropertyChanged(nameof(TakenDamageText));
        OnPropertyChanged(nameof(TotalDamageTooltip));
        OnPropertyChanged(nameof(TotalHealingTooltip));
        OnPropertyChanged(nameof(TotalDpsTooltip));
        OnPropertyChanged(nameof(TotalHpsTooltip));
        OnPropertyChanged(nameof(TakenDamageTooltip));
    }

    /// <summary>
    ///     当语言变更时，使缓存的字符串（如Tooltip和复制文本）失效，以便下次访问时重新生成。
    /// </summary>
    public void InvalidateLocalizedStrings()
    {
        _cachedToolTipText = null;
        _cachedCopyableString = null;
        OnPropertyChanged(nameof(ToolTipText));
        OnPropertyChanged(nameof(CopyableString));
    }
}