using System;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace GardenTracker;

/// <summary>
/// Watches chat for /random results and, on the GM's client only, logs them to the session.
///
/// English client lines look like:
///   "Random! You roll a 7."
///   "Random! Tua Ito rolls a 7."
///   "Random! (1-10) Tua Ito rolls a 7."   (some clients show the range)
/// Rolls never have a chat sender, so a player typing a fake "Random! ..." line into /say is ignored.
/// </summary>
public sealed partial class RollWatcher : IDisposable
{
    private readonly Plugin plugin;

    [GeneratedRegex(@"Random!\s*(?:\((?<min>\d+)-(?<max>\d+)\)\s*)?(?<name>.+?)\s+rolls?\s+an?\s+(?<roll>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RollRegex();

    public event Action<string, int>? RollSeen;

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
            var sender = chat.Sender;
            var message = chat.Message;
            if (!string.IsNullOrWhiteSpace(sender.TextValue)) return; // typed by a player, not a system roll

            var text = message.TextValue;
            if (!text.Contains("Random!", StringComparison.OrdinalIgnoreCase)) return;

            var m = RollRegex().Match(text);
            if (!m.Success) return;
            if (!int.TryParse(m.Groups["roll"].Value, out var roll)) return;

            if (m.Groups["max"].Success && int.TryParse(m.Groups["max"].Value, out var max) && max != 10 && plugin.Configuration.OnlyD10Rolls)
                return;
            if (plugin.Configuration.OnlyD10Rolls && (roll < 1 || roll > 10)) return;

            var name = ResolveName(message, m.Groups["name"].Value.Trim());
            if (string.IsNullOrEmpty(name)) return;

            RollSeen?.Invoke(name, roll);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to parse roll message");
        }
    }

    private static string ResolveName(SeString message, string fromText)
    {
        // Other players: the name carries a PlayerPayload (clean name without the world icon/name).
        var player = message.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if (player != null && !string.IsNullOrWhiteSpace(player.PlayerName)) return player.PlayerName;

        // You: "Random! You roll a 7."
        if (fromText.Equals("You", StringComparison.OrdinalIgnoreCase))
            return Plugin.LocalPlayerName ?? "You";

        return fromText;
    }

    public void Dispose() => Plugin.ChatGui.ChatMessage -= OnChatMessage;
}