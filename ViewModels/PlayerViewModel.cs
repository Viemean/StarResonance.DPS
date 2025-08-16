using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarResonance.DPS.Models;
using StarResonance.DPS.Services;

namespace StarResonance.DPS.ViewModels;
/// <summary>
/// 表示单个玩家数据及其在UI中状态的视图模型。
/// </summary>
/// <param name="uid">玩家的唯一ID。</param>
/// <param name="localizationService">用于UI文本本地化的服务。</param>
/// <param name="notificationService">用于显示通知的服务。</param>
public partial class PlayerViewModel(
    long uid,
    LocalizationService localizationService,
    INotificationService notificationService) : ObservableObject
{
    [ObservableProperty] private string? _accurateCritDamageText;
    [ObservableProperty] private string? _accurateCritHealingText;
    [ObservableProperty] private string? _damageDisplayPercentage;
    [ObservableProperty] private string? _dpsDisplayPercentage;

    [ObservableProperty] private string? _hpsDisplayPercentage;
    private UserData? _data;
    private string _fightDuration = "0:00";

    [ObservableProperty] private int _fightPoint;

    [ObservableProperty] private string? _healingDisplayPercentage;
    private bool _isExpanded;

    [ObservableProperty] private bool _isIdle;
    [ObservableProperty] private string _name = null!;
    [ObservableProperty] private Brush _nameColor = Brushes.White;
    [ObservableProperty] private string _profession = null!;
    [ObservableProperty] private int _rank;

    [ObservableProperty] private bool _showSeparatorAfter;
    [ObservableProperty] private double _takenDamage;

    [ObservableProperty] private string? _takenDamageDisplayPercentage;
    [ObservableProperty] private double _totalDamage;
    [ObservableProperty] private double _totalDps;
    [ObservableProperty] private double _totalHealing;
    [ObservableProperty] private double _totalHps;

    /// <summary>
    ///     一个锁标志，防止对同一个玩家同时发起多个数据请求。
    /// </summary>
    public bool IsFetchingSkillData { get; set; }

    /// <summary>
    /// 用于从快照数据创建玩家视图模型的构造函数。
    /// </summary>
    /// <param name="snapshot">包含玩家原始数据和技能数据的快照对象。</param>
    /// <param name="fightDuration">战斗持续时间的字符串表示。</param>
    /// <param name="localizationService">本地化服务实例。</param>
    /// <param name="notificationService">通知服务实例。</param>
    public PlayerViewModel(PlayerSnapshot snapshot, string fightDuration, LocalizationService localizationService,
            INotificationService notificationService)
        // 修正：从 SkillData 中安全地获取 Uid，如果不存在则默认为0
        : this(snapshot.SkillData?.Uid ?? 0, localizationService, notificationService)
    {
        Update(snapshot.UserData, 0, fightDuration);
        RawSkillData = snapshot.SkillData;
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
    }

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

    /// <summary>
    /// 控制暴击伤害分析文本的可见性。
    /// </summary>
    public Visibility CritDamageVisibility =>
        !string.IsNullOrEmpty(AccurateCritDamageText) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 控制暴击治疗分析文本的可见性。
    /// </summary>
    public Visibility CritHealingVisibility =>
        !string.IsNullOrEmpty(AccurateCritHealingText) ? Visibility.Visible : Visibility.Collapsed;

    public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;
    public ObservableCollection<SkillViewModel> Skills { get; } = new();

    public long Uid { get; } = uid;

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

    public string ToolTipText
    {
        get
        {
            if (_data == null) return string.Empty;

            var sb = new StringBuilder();
            var unknownText = localizationService["Unknown"] ?? "未知";

            // 基础信息
            sb.AppendLine($"{localizationService["Tooltip_PlayerId"] ?? "角色ID: "}{Uid}");
            sb.AppendLine($"{localizationService["Tooltip_PlayerName"] ?? "角色昵称: "}{Name}");

            //静态显示角色等级、臂章等级和最大生命值
            var attr = RawSkillData?.Attr;

            var levelText = attr is { Level: > 0 } ? attr.Level.ToString() : unknownText;
            sb.AppendLine($"{(localizationService["Tooltip_CharacterLevel"] ?? "角色等级:").TrimEnd(':')} {levelText}");

            var rankLevelText = attr is { RankLevel: > 0 } ? attr.RankLevel.ToString() : unknownText;
            sb.AppendLine($"{(localizationService["Tooltip_RankLevel"] ?? "臂章等级:").TrimEnd(':')} {rankLevelText}");

            var maxHpText = attr is { MaxHp: > 0 } ? attr.MaxHp.ToString("N0") : unknownText;
            sb.AppendLine($"{(localizationService["Tooltip_MaxHP"] ?? "最大生命值:").TrimEnd(':')} {maxHpText}");

            // 评分和职业
            sb.AppendLine($"{localizationService["Tooltip_Score"] ?? "评分: "}{FightPoint}");
            sb.AppendLine($"{localizationService["Tooltip_Profession"] ?? "职业: "}{Profession}");

            // 暴击率和幸运率
            var critRate = _data.TotalCount.Total > 0 ? (double)_data.TotalCount.Critical / _data.TotalCount.Total : 0;
            sb.AppendLine($"{localizationService["Tooltip_CritRate"] ?? "暴击率: "}{critRate:P1}");

            var luckyRate = _data.TotalCount.Total > 0 ? (double)_data.TotalCount.Lucky / _data.TotalCount.Total : 0;
            sb.AppendLine($"{localizationService["Tooltip_LuckyRate"] ?? "幸运率: "}{luckyRate:P1}");

            return sb.ToString().TrimEnd();
        }
    }

    public string DisplayProfession
    {
        get
        {
            if (string.IsNullOrEmpty(Profession)) return string.Empty;
            var parts = Profession.Split('-');
            return parts.Length > 1 ? parts[1] : parts[0];
        }
    }

    public void NotifyTooltipUpdate() => OnPropertyChanged(nameof(ToolTipText));

    public string CopyableString
    {
        get
        {
            if (_data == null) return string.Empty;
            var critRate = _data.TotalCount.Total > 0 ? (double)_data.TotalCount.Critical / _data.TotalCount.Total : 0;
            var luckyRate = _data.TotalCount.Total > 0 ? (double)_data.TotalCount.Lucky / _data.TotalCount.Total : 0;

            var rankLabel = localizationService["Rank"] ?? "#";
            var nameLabel = localizationService["CharacterName"] ?? "Name";
            var scoreLabel = localizationService["Score"] ?? "Score";
            var professionLabel = localizationService["Profession"] ?? "Profession";
            var totalDamageLabel = localizationService["TotalDamage"] ?? "Damage";
            var totalHealingLabel = localizationService["TotalHealing"] ?? "Healing";
            var dpsLabel = localizationService["TotalDPS"] ?? "DPS";
            var hpsLabel = localizationService["TotalHPS"] ?? "HPS";
            var takenDamageLabel = localizationService["TakenDamage"] ?? "DmgTaken";
            var critRateLabel = (localizationService["Tooltip_CritRate"] ?? "Crit Rate: ").TrimEnd(':', ' ');
            var luckyRateLabel = (localizationService["Tooltip_LuckyRate"] ?? "Lucky Rate: ").TrimEnd(':', ' ');
            var durationLabel = localizationService["FightDuration"] ?? "Duration";

            return $"{rankLabel}: {Rank}, " +
                   $"{nameLabel}: {DisplayName}, " +
                   $"{scoreLabel}: {FightPoint}, " +
                   $"{professionLabel}: {Profession}, " +
                   $"{totalDamageLabel}: {TotalDamage:F0}, " +
                   $"{totalHealingLabel}: {TotalHealing:F0}, " +
                   $"{dpsLabel}: {TotalDps:F2}, " +
                   $"{hpsLabel}: {TotalHps:F2}, " +
                   $"{takenDamageLabel}: {TakenDamage:F0}, " +
                   $"{critRateLabel}: {critRate:P1}, " +
                   $"{luckyRateLabel}: {luckyRate:P1}, " +
                   $"{durationLabel}: {_fightDuration}";
        }
    }

    public void CalculateAccurateCritDamage(IReadOnlyDictionary<string, SkillData> skillsData)
    {
        const int minSamples = 2;
        var multipliers = new List<(double multiplier, double weight)>();

        // 筛选出同时具有普通和暴击记录的、可供分析的有效伤害技能
        var validSkills = skillsData.Values.Where(s =>
            s is { Type: "伤害", CountBreakdown: { Normal: >= minSamples, Critical: >= minSamples } }).ToList();

        foreach (var skill in validSkills)
        {
            var avgNormal = skill.DamageBreakdown.Normal / skill.CountBreakdown.Normal;
            if (avgNormal <= 0) continue; // 避免除以零

            var totalCritDamage = skill.DamageBreakdown.Critical + skill.DamageBreakdown.CritLucky;
            var avgCrit = totalCritDamage / skill.CountBreakdown.Critical;

            // 记录每个技能的暴击倍率及其权重（权重为该技能的总伤害）
            multipliers.Add((avgCrit / avgNormal, skill.TotalDamage));
        }

        // 基于所有有效技能的数据，进行加权平均计算
        if (multipliers.Count != 0)
        {
            var totalWeight = multipliers.Sum(t => t.weight);
            if (totalWeight > 0)
            {
                var totalWeightedMultiplier = multipliers.Sum(t => t.multiplier * t.weight);
                var weightedAvgMultiplier = totalWeightedMultiplier / totalWeight;
                var bonus = weightedAvgMultiplier - 1;
                AccurateCritDamageText = $"{bonus:P1}";
            }
            else
            {
                AccurateCritDamageText = localizationService["NotApplicable"] ?? "数据不足";
            }
        }
        else
        {
            AccurateCritDamageText = localizationService["NotApplicable"] ?? "数据不足";
        }

        OnPropertyChanged(nameof(CritDamageVisibility));
        OnPropertyChanged(nameof(ToolTipText));
    }

    public void CalculateAccurateCritHealing(IReadOnlyDictionary<string, SkillData> skillsData)
    {
        const int minSamples = 2;
        var multipliers = new List<(double multiplier, double weight)>();

        // 筛选出同时具有普通和暴击记录的、可供分析的有效治疗技能
        var validSkills = skillsData.Values.Where(s =>
            s is { Type: "治疗", CountBreakdown: { Normal: >= minSamples, Critical: >= minSamples } }).ToList();

        foreach (var skill in validSkills)
        {
            var avgNormal = skill.DamageBreakdown.Normal / skill.CountBreakdown.Normal;
            if (avgNormal <= 0) continue; // 避免除以零

            var totalCritHealing = skill.DamageBreakdown.Critical + skill.DamageBreakdown.CritLucky;
            var avgCrit = totalCritHealing / skill.CountBreakdown.Critical;

            // 记录每个技能的暴疗倍率及其权重（权重为该技能的总治疗量）
            multipliers.Add((avgCrit / avgNormal, skill.TotalDamage));
        }

        // 基于所有有效技能的数据，进行加权平均计算
        if (multipliers.Count != 0)
        {
            var totalWeight = multipliers.Sum(t => t.weight);
            if (totalWeight > 0)
            {
                var totalWeightedMultiplier = multipliers.Sum(t => t.multiplier * t.weight);
                var weightedAvgMultiplier = totalWeightedMultiplier / totalWeight;
                var bonus = weightedAvgMultiplier - 1;
                AccurateCritHealingText = $"{bonus:P1}";
            }
            else
            {
                AccurateCritHealingText = localizationService["NotApplicable"] ?? "数据不足";
            }
        }
        else
        {
            AccurateCritHealingText = localizationService["NotApplicable"] ?? "数据不足";
        }

        OnPropertyChanged(nameof(CritHealingVisibility));
        OnPropertyChanged(nameof(ToolTipText));
    }

    [RelayCommand]
    private void CopyData()
    {
        try
        {
            Clipboard.SetText(CopyableString);
            notificationService.ShowNotification(localizationService["CopySuccess"] ?? "复制成功!");
        }
        catch
        {
            notificationService.ShowNotification(localizationService["CopyFailed"] ?? "复制失败: 无法访问剪贴板");
        }
    }

    [ObservableProperty] private bool _isLoadingSkills;

    public void SetLoadingSkills(bool isLoading)
    {
        IsLoadingSkills = isLoading;
    }

    public void Update(UserData data, int rank, string fightDuration)
    {
        // 在更新前保存与Tooltip相关的旧数据以供比较
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

        // 检查Tooltip依赖的数据是否真的发生了变化
        if (oldName != Name ||
            oldProfession != Profession ||
            oldFightPoint != FightPoint ||
            oldTotalCount?.Total != data.TotalCount.Total ||
            oldTotalCount?.Critical != data.TotalCount.Critical ||
            oldTotalCount?.Lucky != data.TotalCount.Lucky)
        {
            // 仅在必要时才通知UI更新Tooltip
            OnPropertyChanged(nameof(ToolTipText));
        }

        OnComputedPropertiesChanged();
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
}