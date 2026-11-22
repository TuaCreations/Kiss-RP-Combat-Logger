using System;
using Dalamud.Configuration;

namespace GardenTracker;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Backend. The publishable key is safe to ship: every write is checked against the session's GM key.
    public string SupabaseUrl { get; set; } = "https://klilologdlkauhhkwjqa.supabase.co";
    public string SupabaseKey { get; set; } = "sb_publishable_mPlPyTeg0ueLvgrf6btczw_iVIcD1Cc";

    // Session
    public string SessionCode { get; set; } = "";
    public string GmKey { get; set; } = "";          // stored only on the GM's own PC
    public bool RememberGmKey { get; set; } = true;

    // Behaviour
    public int PollMilliseconds { get; set; } = 1000;
    public bool LogRollsAutomatically { get; set; } = true; // GM only
    public bool OnlyD10Rolls { get; set; } = true;          // ignore /random results above 10
    public bool OnlyRosterRolls { get; set; } = true;       // ignore rolls from players not on the session's party list
    public bool PluginRollsEnemyDice { get; set; } = true;  // enemy actions roll their own d10 for Enemy Momentum
    public bool ShowOverlay { get; set; } = true;
    public bool ShowOverlayHp { get; set; } = true;
    public int RollLogLines { get; set; } = 15;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
