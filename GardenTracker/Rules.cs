namespace GardenTracker;

/// <summary>The Garden Hunt K.I.S.S. combat table, as written on the sheet.</summary>
public static class Rules
{
    public const int MomentumUnleashDamage = 10; // Momentum D: 10 DMG + 2 rolls
    public const int MomentumUnleashHeal = 5;    // Momentum H: 5 healing + 2 rolls
    public const int PotionHeal = 5;

    /// <summary>Damage or healing for a natural d10 roll (before modifiers).</summary>
    public static int Amount(int roll) => roll switch
    {
        <= 1 => 1,          // glancing blow
        <= 3 => 2,          // 2-3 hit
        <= 5 => 3,          // 4-5 hit
        <= 7 => 4,          // 6-7 hit
        <= 9 => 5,          // 8-9 hit
        _ => 5,             // 10 crit: 5 + another roll
    };

    public static string Label(int roll) => roll switch
    {
        <= 1 => "Glancing blow",
        <= 9 => "Hit",
        _ => "CRIT - roll again",
    };

    public static bool IsCrit(int roll) => roll >= 10;
}
