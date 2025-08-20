using System.Windows.Media;
using StarResonance.DPS.Models;

namespace StarResonance.DPS.ViewModels;

public class SkillViewModel : ObservableObject
{
    // 使用静态只读画刷避免重复内存分配 ---
    private static readonly Brush FireBrush = Brushes.OrangeRed;
    private static readonly Brush IceBrush = Brushes.DeepSkyBlue;
    private static readonly Brush ThunderBrush = Brushes.Yellow;
    private static readonly Brush ForestBrush = Brushes.LimeGreen;
    private static readonly Brush WindBrush = Brushes.Turquoise;
    private static readonly Brush LightBrush = Brushes.Gold;
    private static readonly Brush DarkBrush = Brushes.MediumPurple;
    private static readonly Brush PhysicalBrush = Brushes.WhiteSmoke;
    private static readonly Brush DefaultBrush = Brushes.White;
    private static readonly Brush DamageBrush = Brushes.Yellow;
    private static readonly Brush HealingBrush = Brushes.LimeGreen;

    public SkillViewModel(SkillData skillData, double playerTotal)
    {
        DisplayName = skillData.DisplayName.ToString() ?? "未知技能";
        ElementType = skillData.ElementType;

        ElementColor = skillData.ElementType switch
        {
            { } s when s.Contains('火') => FireBrush,
            { } s when s.Contains('冰') => IceBrush,
            { } s when s.Contains('雷') => ThunderBrush,
            { } s when s.Contains('森') => ForestBrush,
            { } s when s.Contains('风') => WindBrush,
            { } s when s.Contains('光') => LightBrush,
            { } s when s.Contains('暗') => DarkBrush,
            "⚔️" => PhysicalBrush,
            _ => DefaultBrush
        };

        var percentage = playerTotal > 0 ? skillData.TotalDamage / playerTotal : 0;
        PercentageText = $"{percentage:P1}";

        if (skillData.Type == "伤害")
        {
            TotalValueText = MainViewModel.FormatNumber(skillData.TotalDamage);
            TypeColor = DamageBrush;
        }
        else
        {
            TotalValueText = MainViewModel.FormatNumber(skillData.TotalDamage);
            TypeColor = HealingBrush;
        }
    }

    public string DisplayName { get; }
    public string ElementType { get; }
    public string TotalValueText { get; }
    public string PercentageText { get; }
    public Brush TypeColor { get; }
    public Brush ElementColor { get; }
}