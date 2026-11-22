using System;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GardenTracker.Windows;

namespace GardenTracker;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string Command = "/garden";
    private const string GmCommand = "/gardengm";

    public Configuration Configuration { get; }
    public GardenApi Api { get; }
    public StateSync Sync { get; }
    public GmActions Gm { get; }
    public RollWatcher Rolls { get; }

    public readonly WindowSystem WindowSystem = new("GardenTracker");
    public TrackerWindow TrackerWindow { get; }
    public GmWindow GmWindow { get; }
    private readonly MarkerOverlay overlay;

    /// <summary>True once the GM key has been verified against the server this session.</summary>
    public bool IsGm { get; set; }

    /// <summary>Last status / error line shown in the windows.</summary>
    public string Status { get; set; } = "";

    public static string? LocalPlayerName => ObjectTable.LocalPlayer?.Name.TextValue;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Api = new GardenApi(Configuration);
        Sync = new StateSync(Api, Configuration);
        Gm = new GmActions(this);
        Rolls = new RollWatcher(this);
        Rolls.RollSeen += OnRollSeen;

        TrackerWindow = new TrackerWindow(this);
        GmWindow = new GmWindow(this);
        WindowSystem.AddWindow(TrackerWindow);
        WindowSystem.AddWindow(GmWindow);
        overlay = new MarkerOverlay(this);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the K.I.S.S. RP Combat Logger. /garden join <CODE> joins a session.",
        });
        CommandManager.AddHandler(GmCommand, new CommandInfo((_, _) => GmWindow.Toggle())
        {
            HelpMessage = "Open the K.I.S.S. RP Combat Logger GM controls.",
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += TrackerWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += GmWindow.Toggle;

        // A remembered GM key is re-verified on load, never trusted blindly.
        if (!string.IsNullOrEmpty(Configuration.GmKey) && !string.IsNullOrEmpty(Configuration.SessionCode))
            Run(async () => IsGm = await Api.CheckKey());
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0].Equals("join", StringComparison.OrdinalIgnoreCase))
        {
            JoinSession(parts[1]);
            TrackerWindow.IsOpen = true;
            return;
        }
        TrackerWindow.Toggle();
    }

    public void JoinSession(string code)
    {
        Configuration.SessionCode = code.Trim().ToUpperInvariant();
        IsGm = false;
        Configuration.Save();
        Sync.Reset();
        ChatGui.Print($"[Garden] Joined session {Configuration.SessionCode}.");
    }

    private void OnRollSeen(string name, int roll)
    {
        if (!IsGm || !Configuration.LogRollsAutomatically) return;

        // Ignore bystanders: only characters on the session's party list are logged.
        if (Configuration.OnlyRosterRolls)
        {
            var roster = Sync.Current?.Members;
            if (roster == null || !roster.Exists(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Debug($"Ignored roll from {name} (not on the party list)");
                return;
            }
        }

        // The server matches the roll to any save this player owes and resolves it automatically.
        Run(() => Api.LogRoll(name, roll));
    }

    /// <summary>Fire-and-forget a server call, surface errors in the UI, and refresh state afterwards.</summary>
    public void Run(Func<Task> action)
    {
        Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                Log.Warning(ex, "K.I.S.S. RP Combat Logger server call failed");
            }
            finally
            {
                Sync.RefreshSoon();
            }
        });
    }

    private void DrawUi()
    {
        WindowSystem.Draw();
        overlay.Draw();
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= TrackerWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= GmWindow.Toggle;
        CommandManager.RemoveHandler(Command);
        CommandManager.RemoveHandler(GmCommand);
        WindowSystem.RemoveAllWindows();
        Rolls.RollSeen -= OnRollSeen;
        Rolls.Dispose();
        Sync.Dispose();
        Api.Dispose();
    }
}
