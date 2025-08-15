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
    [ObservableProperty] private string? _damageDisplayPercentage;
    private UserData? _data;
    private string _fightDuration = "0:00";

    [ObservableProperty] private int _fightPoint;

    [ObservableProperty] private string? _healingDisplayPercentage;
    [ObservableProperty] private bool _isExpanded;

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
    public string DisplayName => Name;

    public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;

    // --- 【新增属性】 ---
    // 用于记录上次学习该玩家技能的时间, 初始化为最小值以确保首次会立即学习
    public DateTime LastLearnedTime { get; set; } = DateTime.MinValue;

    public ObservableCollection<SkillViewModel> Skills { get; } = new();

    public long Uid { get; } = uid;

    public string TotalDamageText => MainViewModel.FormatNumber(TotalDamage);
    public string TotalHealingText => MainViewModel.FormatNumber(TotalHealing);
    public string TotalDpsText => MainViewModel.FormatNumber(TotalDps);
    public string TotalHpsText => MainViewModel.FormatNumber(TotalHps);
    public string TakenDamageText => MainViewModel.FormatNumber(TakenDamage);

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
            return sb.ToString();
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

    public void Update(UserData data, int rank, string fightDuration)
    {
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

    // 增加一个方法来处理复制逻辑
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