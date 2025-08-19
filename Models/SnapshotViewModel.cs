using System.Text.Json.Serialization;

namespace StarResonance.DPS.Models;

public record PlayerSnapshot
{
    [JsonInclude] public UserData UserData { get; init; } = new();
    [JsonInclude] public SkillApiResponseData? SkillData { get; init; }

    // --- 百分比属性 ---
    [JsonInclude] public double DamagePercent { get; init; }
    [JsonInclude] public double HealingPercent { get; init; }
}

public record SnapshotData
{
    [JsonInclude] public int ElapsedSeconds { get; init; }
    [JsonInclude] public List<PlayerSnapshot> Players { get; init; } = [];
}