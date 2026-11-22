using System;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace GardenTracker;

/// <summary>
/// Watches chat for dice results and raises RollSeen(name, roll, source).
///
/// Sources (each can be switched on/off by the GM for the session):
///   random   - /random system lines: "Random! You roll a 7 (out of 10)." / "Random! Tua Ito rolls a 7."
///   party, alliance, fc, ls, cwls - /dice lines posted into those chat channels by a player:
///              sender = the player, text like "Random! (1-10) 7"
/// A /random line never has a sender, so a fake typed into /say can't pass as one. /dice lines do have a sender,
/// so the session's "require_icon" option only accepts them when they carry the game's dice icon.
/// </summary>
public sealed partial class RollWatcher : IDisposable
{
    private readonly Plugin plugin;

    // /random: "Random! [(1-10)] Name rolls a 7 [(out of 10)]." or "You roll a 7"
    [GeneratedRegex(@"Random!\s*(?:\((?<min>\d+)-(?<max>\d+)\)\s*)?(?<name>.+?)\s+rolls?\s+an?\s*\D{0,3}?(?<roll>\d+)(?:\s*\(out of (?<outof>\d+)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex RandomRegex();

    // /dice in a channel: "{Random!} (1-10) [dice icon]8". "Random!" is an auto-translate phrase, so it can be
    // wrapped in invisible bracket characters; anything that isn't a digit or "(" is skipped between the parts.
    [GeneratedRegex(@"Random!?[^\d(]*(?:\((?<min>\d+)-(?<max>\d+)\))?[^\d]*(?<roll>\d+)\D*$", RegexOptions.IgnoreCase)]
    private static partial Regex DiceRegex();

    [GeneratedRegex(@"[^\p{L}\p{M}' \-]")]
    private static partial Regex NotNameChars();

    public event Action<string, int, string>? RollSeen;

    public RollWatcher(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.ChatGui.ChatMessage += OnChatMessage;
    }

    // Dalamud API 15: chat events pass one IHandleableChatMessage instead of separate ref parameters.
    private void OnChatMessage(IHandleableChatMessage chat)
    {
        try
        {
            var message = chat.Message;
            var text = message.TextValue;
            if (!text.Contains("Random", StringComparison.OrdinalIgnoreCase)) return;

            var senderText = chat.Sender.TextValue;
            if (string.IsNullOrWhiteSpace(senderText))
                HandleRandom(message, text);
            else
                HandleDice(chat.LogKind, chat.Sender, message, text);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to parse roll message");
        }
    }

    private void HandleRandom(SeString message, string text)
    {
        var m = RandomRegex().Match(text);
        if (!m.Success) { Debug($"ignored (unrecognised /random line): {text}"); return; }
        if (!int.TryParse(m.Groups["roll"].Value, out var roll)) return;

        var max = m.Groups["outof"].Success ? m.Groups["outof"].Value : m.Groups["max"].Success ? m.Groups["max"].Value : null;
        if (!PassesD10(roll, max, text)) return;

        var fromText = m.Groups["name"].Value.Trim();
        var player = message.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        string name;
        if (player != null && !string.IsNullOrWhiteSpace(player.PlayerName)) name = player.PlayerName;
        else if (fromText.Equals("You", StringComparison.OrdinalIgnoreCase)) name = Plugin.LocalPlayerName ?? "You";
        else name = CleanName(fromText);

        Raise(name, roll, "random", text);
    }

    private void HandleDice(XivChatType kind, SeString sender, SeString message, string text)
    {
        var source = SourceOf(kind);
        if (source == null) { Debug($"ignored ({kind} isn't a /dice channel): {text}"); return; }

        var m = DiceRegex().Match(text);
        if (!m.Success) { Debug($"ignored (unrecognised /dice line in {source}): {text}"); return; }
        if (!int.TryParse(m.Groups["roll"].Value, out var roll)) return;
        if (!PassesD10(roll, m.Groups["max"].Success ? m.Groups["max"].Value : null, text)) return;

        var requireIcon = plugin.Sync.Current?.Session.SourceOn("require_icon") ?? true;
        if (requireIcon && !message.Payloads.Any(p => p is IconPayload))
        {
            Debug($"ignored (no dice icon - typed by hand?): {text}");
            return;
        }

        // Sender looks like "(②Cecilia Black ✿Balmung)": prefer the clean PlayerPayload name, then match the
        // party list by name, so slot numbers and world names never get in the way.
        var player = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        var name = player != null && !string.IsNullOrWhiteSpace(player.PlayerName) ? player.PlayerName : CleanName(sender.TextValue);
        var rawSender = sender.TextValue;
        var rosterMatch = plugin.Sync.Current?.Members
            .Where(m => rawSender.Contains(m.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Name.Length)
            .FirstOrDefault();
        if (rosterMatch != null) name = rosterMatch.Name;
        else
        {
            var me = Plugin.LocalPlayerName;
            if (me != null && rawSender.Contains(me, StringComparison.OrdinalIgnoreCase)) name = me;
        }

        Raise(name, roll, source, text);
    }

    private bool PassesD10(int roll, string? max, string text)
    {
        if (!plugin.Configuration.OnlyD10Rolls) return true;
        if (max != null && max != "10") { Debug($"ignored (not a d10): {text}"); return false; }
        if (roll < 1 || roll > 10) { Debug($"ignored (not a d10): {text}"); return false; }
        return true;
    }

    private void Raise(string name, int roll, string source, string text)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Debug($"{source}: {name} rolled {roll}");
        RollSeen?.Invoke(name, roll, source);
    }

    private void Debug(string line)
    {
        if (plugin.Configuration.DebugRolls) Plugin.ChatGui.Print($"[Garden] {line}");
    }

    /// <summary>Strips party-slot glyphs, world icons and the like from a sender's display name.</summary>
    private static string CleanName(string raw) => NotNameChars().Replace(raw, "").Trim();

    public static string? SourceOf(XivChatType kind) => kind switch
    {
        XivChatType.Party or XivChatType.CrossParty => "party",
        XivChatType.Alliance => "alliance",
        XivChatType.FreeCompany => "fc",
        XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4
            or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8 => "ls",
        XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3 or XivChatType.CrossLinkShell4
            or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6 or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8 => "cwls",
        _ => null,
    };

    public void Dispose() => Plugin.ChatGui.ChatMessage -= OnChatMessage;
}
