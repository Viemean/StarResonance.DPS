using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StarResonance.DPS.Models;

namespace StarResonance.DPS.ViewModels;

public class SkillViewModel : ObservableObject
{
    public SkillViewModel(SkillData skillData, double playerTotal)
    {
        DisplayName = skillData.DisplayName.ToString() ?? "未知技能";
        ElementType = skillData.ElementType;

        // --- 优化: 根据元素类型设置颜色并冻结画刷 ---
        var elementBrush = skillData.ElementType switch
        {
            var s when s.Contains("火") => Brushes.OrangeRed,
            var s when s.Contains("冰") => Brushes.DeepSkyBlue,
            var s when s.Contains("雷") => Brushes.Yellow,
            var s when s.Contains("森") => Brushes.LimeGreen,
            var s when s.Contains("风") => Brushes.Turquoise,
            var s when s.Contains("光") => Brushes.Gold,
            var s when s.Contains("暗") => Brushes.MediumPurple,
            var s when s.Contains("⚔️") => Brushes.WhiteSmoke, // 物理
            _ => Brushes.White
        };

        // 冻结对象以提升性能。系统预定义的Brushes已经是冻结的，但这是一个好习惯。
        if (elementBrush.CanFreeze) elementBrush.Freeze();
        ElementColor = elementBrush;

        var percentage = playerTotal > 0 ? skillData.TotalDamage / playerTotal : 0;
        PercentageText = $"{percentage:P1}";

        if (skillData.Type == "伤害")
        {
            TotalValueText = MainViewModel.FormatNumber(skillData.TotalDamage);
            var damageBrush = Brushes.Yellow;
            if (damageBrush.CanFreeze) damageBrush.Freeze();
            TypeColor = damageBrush;
        }
        else // 治疗
        {
            TotalValueText = MainViewModel.FormatNumber(skillData.TotalDamage);
            var healingBrush = Brushes.LimeGreen;
            if (healingBrush.CanFreeze) healingBrush.Freeze();
            TypeColor = healingBrush;
        }
    }

    public string DisplayName { get; }
    public string ElementType { get; }
    public string TotalValueText { get; }
    public string PercentageText { get; }
    public Brush TypeColor { get; }
    public Brush ElementColor { get; }
}