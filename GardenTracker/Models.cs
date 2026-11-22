using System;
using System.Collections.Generic;

namespace GardenTracker;

// Shapes returned by public.garden_get_state (snake_case JSON, mapped by GardenApi's serializer options).

public sealed class GardenState
{
    public SessionInfo Session { get; set; } = new();
    public List<PartyMember> Members { get; set; } = new();
    public List<Enemy> Enemies { get; set; } = new();
    public List<RollEntry> Rolls { get; set; } = new();
    public List<Marker> Markers { get; set; } = new();
    public List<SaveEntry> Saves { get; set; } = new();
}

/// <summary>One enemy ability macro. Also the "actions" entries of a kiss-scenario file.</summary>
public sealed class EnemyAction
{
    public string Name { get; set; } = "";
    /// <summary>choose | random | all | highest_momentum | lowest_hp</summary>
    public string Targets { get; set; } = "choose";
    public int Count { get; set; } = 1;
    /// <summary>Save fails on a roll at or below this number (a natural 1 always fails).</summary>
    public int FailMax { get; set; }
    public int Damage { get; set; }
    public string Status { get; set; } = "";
    public int HealSelf { get; set; }
    public bool ResetMomentum { get; set; }
    public int Phase { get; set; }
    public string Note { get; set; } = "";

    public string Summary()
    {
        var who = Targets switch
        {
            "all" => "all players",
            "random" => Count == 1 ? "1 random player" : $"{Count} random players",
            "highest_momentum" => "highest Momentum",
            "lowest_hp" => "lowest HP",
            _ => Count == 1 ? "1 chosen player" : $"{Count} chosen players",
        };
        var fail = FailMax <= 1 ? "fail on 1" : $"fail 1-{FailMax}";
        var hit = Damage > 0 ? $"{Damage} DMG" : "no DMG";
        if (!string.IsNullOrWhiteSpace(Status)) hit += $" + {Status}";
        if (ResetMomentum) hit += ", Momentum to 0";
        if (HealSelf > 0) hit += $", heals {HealSelf}";
        return $"{who}: {fail} -> {hit}";
    }
}

public sealed class StatusDef
{
    public string Name { get; set; } = "";
    public string Effect { get; set; } = "";
    public string Clears { get; set; } = "";
}

public sealed class SaveEntry
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? EnemyId { get; set; }
    public string EnemyName { get; set; } = "";
    public string ActionName { get; set; } = "";
    public string Target { get; set; } = "";
    public int FailMax { get; set; }
    public int Damage { get; set; }
    public string Status { get; set; } = "";
    public string Outcome { get; set; } = "pending"; // pending | failed | passed | countered | cancelled
    public int? Roll { get; set; }
    public int Applied { get; set; }
}

public sealed class SessionInfo
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int EnemyMomentum { get; set; }
    public int EnemyMomentumMax { get; set; } = 25;
    public string Passive { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public string ScenarioName { get; set; } = "";
    public EnemyAction? MomentumAction { get; set; }
    public List<StatusDef> Statuses { get; set; } = new();
    public bool ShowEnemyHp { get; set; } = true;
}

public sealed class PartyMember
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Hp { get; set; } = 20;
    public int HpMax { get; set; } = 20;
    public int Shield { get; set; }
    public int Potions { get; set; } = 3;
    public int PotionsMax { get; set; } = 3;
    public int Interception { get; set; } = 1;
    public string Effects { get; set; } = "";
    public int Momentum { get; set; }
    public int MomentumMax { get; set; } = 25;
    public int Sort { get; set; }

    // What the player picked in their own window
    public Guid? TargetEnemyId { get; set; }
    public Guid? HealTargetId { get; set; }

    // Crit / Momentum bank: the next BankRolls rolls are added up and applied as one lump sum
    public string BankKind { get; set; } = "";   // "" | attack | heal
    public string BankLabel { get; set; } = "";
    public int BankAmount { get; set; }
    public int BankRolls { get; set; }
    public Guid? BankEnemyId { get; set; }
    public Guid? BankHealId { get; set; }
    public bool Banking => BankRolls > 0;
}

public sealed class Enemy
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Hp { get; set; }
    public int HpMax { get; set; }
    public int DodgeMax { get; set; }       // attack rolls at or below this miss (0 = never dodges)
    public string Effects { get; set; } = "";
    public string Abilities { get; set; } = "";
    public bool Visible { get; set; } = true;
    public int Sort { get; set; }
    public List<EnemyAction> Actions { get; set; } = new();
}

public sealed class RollEntry
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Roller { get; set; } = "";
    public int Roll { get; set; }
    public string Outcome { get; set; } = "pending"; // pending | attack | miss | heal | save | check | ignored
    public string Target { get; set; } = "";
    public int Amount { get; set; }
    public string Note { get; set; } = "";
}

public sealed class Marker
{
    public Guid Id { get; set; }
    public Guid? EnemyId { get; set; }
    public string Label { get; set; } = "";
    public int Territory { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 3f;
    public string Color { get; set; } = "red";
}
