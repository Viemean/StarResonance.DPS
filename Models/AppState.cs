using System.ComponentModel;

namespace StarResonance.DPS.Models;

public class AppState
{
    public int ElapsedSeconds { get; init; }
    public bool IsFightActive { get; init; }
    public double WindowOpacity { get; init; } = 0.85;
    public double FontOpacity { get; init; } = 1.0;
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
    
    /// 主窗口的位置和大小
    public double WindowTop { get; init; }
    public double WindowLeft { get; init; }
    public double WindowHeight { get; init; }
    public double WindowWidth { get; init; }
}