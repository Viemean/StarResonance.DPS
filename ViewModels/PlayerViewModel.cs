using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StarResonance.DPS.Models;
using StarResonance.DPS.Services;

namespace StarResonance.DPS.ViewModels;

public partial class PlayerViewModel(
    long uid,
    LocalizationService localizationService,
    INotificationService notificationService) : ObservableObject
{
    [ObservableProperty] private string? _accurateCritDamageText;
    [ObservableProperty] private string? _accurateCritHealingText;
    [ObservableProperty] private string? _damageDisplayPercentage;
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
            OnPropertyChanged(nameof(ExpandedVisibility)); // 通知UI更新
            // 当 IsExpanded 被设置为折叠时清空技能和分析数据
            if (value) return;
            if (Skills.Count > 0) Skills.Clear();
            AccurateCritDamageText = null;
            AccurateCritHealingText = null;
        }
    }

    // 用于技能展开区域的可见性属性
    public Visibility ExpandedVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    // 用于暴击伤害分析的可见性属性
    public Visibility CritDamageVisibility => GetVisibilityForAnalysisText(AccurateCritDamageText);

    // 用于暴击治疗分析的可见性属性
    public Visibility CritHealingVisibility => GetVisibilityForAnalysisText(AccurateCritHealingText);

    // 用于判断分析文本是否应显示的辅助方法
    private Visibility GetVisibilityForAnalysisText(string? text)
    {
        // 仅当文本为null或空时才隐藏控件。
        // 这将使得"不适用"的文本能够正常显示出来。
        return !string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
    }

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
            var critRate = _data.TotalCount.Total > 0 ? (double)_data.TotalCount.Critical / _data.TotalCount.Total : 0;
            var luckyRate = _data.TotalCount.Total > 0 ? (double)_data.TotalCount.Lucky / _data.TotalCount.Total : 0;

            var sb = new StringBuilder();
            sb.AppendLine($"{localizationService["Tooltip_PlayerId"] ?? "角色ID: "}{Uid}");
            sb.AppendLine($"{localizationService["Tooltip_PlayerName"] ?? "角色昵称: "}{Name}");
            sb.AppendLine($"{localizationService["Tooltip_Score"] ?? "评分: "}{FightPoint}");
            sb.AppendLine($"{localizationService["Tooltip_Profession"] ?? "职业: "}{Profession}");
            sb.AppendLine($"{localizationService["Tooltip_CritRate"] ?? "暴击率: "}{critRate:P1}");
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
        const int minSamples = 2; // 最低样本数要求

        // 筛选出同时具有普通和暴击记录的有效伤害技能
        var validDamageSkills = skillsData.Values.Where(s =>
            s.Type == "伤害" &&
            s.CountBreakdown.Normal > 0 &&
            s.CountBreakdown.Critical > 0).ToList();

        if (!validDamageSkills.Any())
        {
            AccurateCritDamageText = localizationService["NotApplicable"] ?? "数据不足";
            OnPropertyChanged(nameof(CritDamageVisibility));
            return;
        }

        // 仅汇总有效技能的数据
        var totalNormalCount = validDamageSkills.Sum(s => s.CountBreakdown.Normal);
        var totalCritCount = validDamageSkills.Sum(s => s.CountBreakdown.Critical);
        var totalNormalDamage = validDamageSkills.Sum(s => s.DamageBreakdown.Normal);
        var totalCritDamage = validDamageSkills.Sum(s => s.DamageBreakdown.Critical + s.DamageBreakdown.CritLucky);

        // 基于汇总后的有效数据进行计算
        if (totalNormalCount >= minSamples && totalCritCount >= minSamples && totalNormalDamage > 0)
        {
            var avgNormal = totalNormalDamage / totalNormalCount;
            var avgCrit = totalCritDamage / totalCritCount;
            var bonus = (avgCrit / avgNormal) - 1;
            AccurateCritDamageText = $"{bonus:P1}";
        }
        else
        {
            AccurateCritDamageText = localizationService["NotApplicable"] ?? "数据不足";
        }

        OnPropertyChanged(nameof(CritDamageVisibility));
    }

    public void CalculateAccurateCritHealing(IReadOnlyDictionary<string, SkillData> skillsData)
    {
        const int minSamples = 2; // 最低样本数要求

        // 筛选出同时具有普通和暴击记录的有效治疗技能
        var validHealingSkills = skillsData.Values.Where(s =>
            s.Type == "治疗" &&
            s.CountBreakdown.Normal > 0 &&
            s.CountBreakdown.Critical > 0).ToList();

        if (!validHealingSkills.Any())
        {
            AccurateCritHealingText = localizationService["NotApplicable"] ?? "数据不足";
            OnPropertyChanged(nameof(CritHealingVisibility));
            return;
        }

        // 仅汇总有效技能的数据
        var totalNormalCount = validHealingSkills.Sum(s => s.CountBreakdown.Normal);
        var totalCritCount = validHealingSkills.Sum(s => s.CountBreakdown.Critical);
        var totalNormalHealing = validHealingSkills.Sum(s => s.DamageBreakdown.Normal);
        var totalCritHealing = validHealingSkills.Sum(s => s.DamageBreakdown.Critical + s.DamageBreakdown.CritLucky);

        // 基于汇总后的有效数据进行计算
        if (totalNormalCount >= minSamples && totalCritCount >= minSamples && totalNormalHealing > 0)
        {
            var avgNormal = totalNormalHealing / totalNormalCount;
            var avgCrit = totalCritHealing / totalCritCount;
            var bonus = (avgCrit / avgNormal) - 1;
            AccurateCritHealingText = $"{bonus:P1}";
        }
        else
        {
            AccurateCritHealingText = localizationService["NotApplicable"] ?? "数据不足";
        }

        OnPropertyChanged(nameof(CritHealingVisibility));
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

    public void Update(UserData data, int rank, string fightDuration)
    {
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

        OnComputedPropertiesChanged();
    }

    private void OnComputedPropertiesChanged()
    {
        OnPropertyChanged(nameof(ToolTipText));
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