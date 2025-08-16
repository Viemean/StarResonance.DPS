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

        var elementBrush = skillData.ElementType switch
        {
            { } s when s.Contains('火') => Brushes.OrangeRed,
            { } s when s.Contains('冰') => Brushes.DeepSkyBlue,
            { } s when s.Contains('雷') => Brushes.Yellow,
            { } s when s.Contains('森') => Brushes.LimeGreen,
            { } s when s.Contains('风') => Brushes.Turquoise,
            { } s when s.Contains('光') => Brushes.Gold,
            { } s when s.Contains('暗') => Brushes.MediumPurple,
            "⚔️" => Brushes.WhiteSmoke,
            _ => Brushes.White
        };

        if (elementBrush is { CanFreeze: true } brush) brush.Freeze();

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
        else
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