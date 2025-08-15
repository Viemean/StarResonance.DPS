using System.Text.Json.Serialization;

namespace StarResonance.DPS.Models;

public record TotalCount
{
    [JsonInclude]
    [JsonPropertyName("critical")]
    public int Critical { get; init; }

    [JsonInclude]
    [JsonPropertyName("lucky")]
    public int Lucky { get; init; }

    [JsonInclude]
    [JsonPropertyName("total")]
    public int Total { get; init; }
}

public record TotalStats
{
    [JsonInclude]
    [JsonPropertyName("total")]
    public double Total { get; init; }
}

public record UserData
{
    [JsonInclude]
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonInclude]
    [JsonPropertyName("profession")]
    public string Profession { get; init; } = "";

    [JsonInclude]
    [JsonPropertyName("total_damage")]
    public TotalStats TotalDamage { get; init; } = new();

    [JsonInclude]
    [JsonPropertyName("total_dps")]
    public double TotalDps { get; init; }

    [JsonInclude]
    [JsonPropertyName("total_healing")]
    public TotalStats TotalHealing { get; init; } = new();

    [JsonInclude]
    [JsonPropertyName("total_hps")]
    public double TotalHps { get; init; }

    [JsonInclude]
    [JsonPropertyName("taken_damage")]
    public double TakenDamage { get; init; }

    [JsonInclude]
    [JsonPropertyName("fightPoint")]
    public int FightPoint { get; init; }

    [JsonInclude]
    [JsonPropertyName("total_count")]
    public TotalCount TotalCount { get; init; } = new();
}

public record ApiResponse
{
    [JsonInclude]
    [JsonPropertyName("user")]
    public Dictionary<string, UserData> User { get; init; } = new();
}

public record SkillDamageBreakdown
{
    [JsonInclude]
    [JsonPropertyName("normal")]
    public double Normal { get; init; }

    [JsonInclude]
    [JsonPropertyName("critical")]
    public double Critical { get; init; }

    [JsonInclude]
    [JsonPropertyName("lucky")]
    public double Lucky { get; init; }

    [JsonInclude]
    [JsonPropertyName("crit_lucky")]
    public double CritLucky { get; init; }

    [JsonInclude]
    [JsonPropertyName("hpLessen")]
    public double HpLessen { get; init; }

    [JsonInclude]
    [JsonPropertyName("total")]
    public double Total { get; init; }
}

public record SkillCountBreakdown
{
    [JsonInclude]
    [JsonPropertyName("normal")]
    public int Normal { get; init; }

    [JsonInclude]
    [JsonPropertyName("critical")]
    public int Critical { get; init; }

    [JsonInclude]
    [JsonPropertyName("lucky")]
    public int Lucky { get; init; }

    [JsonInclude]
    [JsonPropertyName("total")]
    public int Total { get; init; }
}

public record SkillData
{
    [JsonInclude]
    [JsonPropertyName("displayName")]
    public object DisplayName { get; init; } = "";

    [JsonInclude]
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonInclude]
    [JsonPropertyName("elementype")]
    public string ElementType { get; init; } = "";

    [JsonInclude]
    [JsonPropertyName("totalDamage")]
    public double TotalDamage { get; init; }

    [JsonInclude]
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    [JsonInclude]
    [JsonPropertyName("critCount")]
    public int CritCount { get; init; }

    [JsonInclude]
    [JsonPropertyName("luckyCount")]
    public int LuckyCount { get; init; }

    [JsonInclude]
    [JsonPropertyName("critRate")]
    public double CritRate { get; init; }

    [JsonInclude]
    [JsonPropertyName("luckyRate")]
    public double LuckyRate { get; init; }

    [JsonInclude]
    [JsonPropertyName("damageBreakdown")]
    public SkillDamageBreakdown DamageBreakdown { get; init; } = new();

    [JsonInclude]
    [JsonPropertyName("countBreakdown")]
    public SkillCountBreakdown CountBreakdown { get; init; } = new();
}

public record SkillApiResponseData
{
    [JsonInclude]
    [JsonPropertyName("uid")]
    public long Uid { get; init; }

    [JsonInclude]
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonInclude]
    [JsonPropertyName("profession")]
    public string Profession { get; init; } = "";

    [JsonInclude]
    [JsonPropertyName("skills")]
    public Dictionary<string, SkillData> Skills { get; init; } = new();
}

public record SkillApiResponse
{
    [JsonInclude]
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonInclude]
    [JsonPropertyName("data")]
    public SkillApiResponseData? Data { get; init; }
}