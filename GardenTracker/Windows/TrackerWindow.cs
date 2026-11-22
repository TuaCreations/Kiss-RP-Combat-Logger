using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GardenTracker.Windows;

/// <summary>The window every player sees: enemies, Enemy Momentum, the party, and the roll log.</summary>
public sealed class TrackerWindow : Window
{
    private readonly Plugin p;
    private string joinCode = "";

    public TrackerWindow(Plugin plugin) : base("K.I.S.S. RP Combat Logger##GardenTrackerMain")
    {
        p = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        joinCode = p.Configuration.SessionCode;
    }

    public override void Draw()
    {
        DrawSessionBar();
        var s = p.Sync.Current;
        if (s == null) return;

        DrawSavesCalled(s);
        DrawEnemies(s);
        DrawParty(s);
        DrawRollLog(s);
        DrawStatuses(s);
    }

    private void DrawSessionBar()
    {
        ImGui.SetNextItemWidth(140);
        ImGui.InputTextWithHint("##join", "Session code", ref joinCode, 32);
        ImGui.SameLine();
        if (ImGui.Button("Join")) p.JoinSession(joinCode);
        ImGui.SameLine();

        var s = p.Sync.Current;
        if (p.Sync.LastError != null)
            ImGui.TextColored(Ui.Red, p.Sync.LastError);
        else if (s != null)
        {
            ImGui.TextColored(Ui.Green, "LIVE");
            ImGui.SameLine();
            ImGui.TextUnformatted(string.IsNullOrEmpty(s.Session.Name) ? s.Session.Code : $"{s.Session.Name} ({s.Session.Code})");
            if (!string.IsNullOrEmpty(s.Session.ScenarioName)) { ImGui.SameLine(); ImGui.TextColored(Ui.Grey, $"- {s.Session.ScenarioName}"); }
            if (p.IsGm) { ImGui.SameLine(); ImGui.TextColored(Ui.Gold, "[GM]"); }
        }
        else if (string.IsNullOrEmpty(p.Configuration.SessionCode))
            ImGui.TextColored(Ui.Grey, "Enter the code your GM gave you.");
        else
            ImGui.TextColored(Ui.Grey, "Connecting...");

        ImGui.SameLine(ImGui.GetWindowWidth() - 70);
        var overlay = p.Configuration.ShowOverlay;
        if (ImGui.Checkbox("##overlay", ref overlay)) { p.Configuration.ShowOverlay = overlay; p.Configuration.Save(); }
        Ui.Tooltip("Show enemy markers in the world");
        ImGui.SameLine();
        if (ImGui.SmallButton("GM")) p.GmWindow.Toggle();
    }

    private void DrawEnemies(GardenState s)
    {
        var visible = s.Enemies.Where(e => e.Visible).ToList();
        Ui.Header("ENEMIES");
        if (visible.Count == 0) ImGui.TextColored(Ui.Grey, "No enemies on the field.");

        foreach (var e in visible)
        {
            ImGui.PushID(e.Id.ToString());
            var down = e.Hp <= 0;
            ImGui.TextColored(down ? Ui.Grey : Ui.Gold, down ? $"{e.Name} (defeated)" : e.Name);
            if (e.DodgeMax > 0)
            {
                ImGui.SameLine();
                ImGui.TextColored(Ui.Grey, e.DodgeMax == 1 ? "dodges on 1" : $"dodges on 1-{e.DodgeMax}");
            }
            if (!string.IsNullOrWhiteSpace(e.Effects))
            {
                ImGui.SameLine();
                ImGui.TextColored(Ui.Violet, $"[{e.Effects}]");
            }
            Ui.Bar(e.HpMax > 0 ? (float)e.Hp / e.HpMax : 0, $"{e.Hp} / {e.HpMax}", Ui.HpColor(e.Hp, e.HpMax));
            if (!string.IsNullOrWhiteSpace(e.Abilities) && ImGui.TreeNode("Abilities"))
            {
                ImGui.PushTextWrapPos(0);
                ImGui.TextUnformatted(e.Abilities);
                ImGui.PopTextWrapPos();
                ImGui.TreePop();
            }
            ImGui.PopID();
        }

        var em = s.Session;
        ImGui.Spacing();
        ImGui.TextUnformatted("Enemy Momentum");
        Ui.Bar(em.EnemyMomentumMax > 0 ? (float)em.EnemyMomentum / em.EnemyMomentumMax : 0, $"{em.EnemyMomentum} / {em.EnemyMomentumMax}", Ui.Violet, -1, 14);
        if (!string.IsNullOrWhiteSpace(em.Passive))
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(Ui.Grey, em.Passive);
            ImGui.PopTextWrapPos();
        }
    }

    private void DrawParty(GardenState s)
    {
        Ui.Header("PARTY");
        if (s.Members.Count == 0) { ImGui.TextColored(Ui.Grey, "No players added yet."); return; }

        var me = Plugin.LocalPlayerName;
        if (!ImGui.BeginTable("party", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("HP", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Shld", ImGuiTableColumnFlags.WidthFixed, 34);
        ImGui.TableSetupColumn("Pot", ImGuiTableColumnFlags.WidthFixed, 34);
        ImGui.TableSetupColumn("Int", ImGuiTableColumnFlags.WidthFixed, 28);
        ImGui.TableSetupColumn("Momentum", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableHeadersRow();

        foreach (var m in s.Members)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var isMe = me != null && m.Name.Equals(me, StringComparison.OrdinalIgnoreCase);
            if (m.Hp <= 0) ImGui.TextColored(Ui.Grey, $"{m.Name} (Hollowed)");
            else if (isMe) ImGui.TextColored(Ui.Gold, m.Name);
            else ImGui.TextUnformatted(m.Name);
            if (!string.IsNullOrWhiteSpace(m.Effects))
                ImGui.TextColored(Ui.Violet, m.Effects);

            ImGui.TableNextColumn();
            Ui.Bar(m.HpMax > 0 ? (float)m.Hp / m.HpMax : 0, $"{m.Hp}/{m.HpMax}", Ui.HpColor(m.Hp, m.HpMax), -1, 16);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(m.Shield > 0 ? m.Shield.ToString() : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{m.Potions}/{m.PotionsMax}");
            ImGui.TableNextColumn();
            ImGui.TextColored(m.Interception > 0 ? Ui.Green : Ui.Grey, m.Interception > 0 ? "Yes" : "Used");

            ImGui.TableNextColumn();
            var ready = m.Momentum >= m.MomentumMax;
            Ui.Bar(m.MomentumMax > 0 ? (float)m.Momentum / m.MomentumMax : 0, ready ? $"{m.Momentum} READY" : $"{m.Momentum}/{m.MomentumMax}", ready ? Ui.Gold : Ui.Violet, -1, 16);
        }
        ImGui.EndTable();
    }

    private void DrawRollLog(GardenState s)
    {
        Ui.Header("ROLLS");
        var lines = Math.Clamp(p.Configuration.RollLogLines, 3, 50);
        foreach (var r in s.Rolls.Take(lines))
        {
            var time = r.CreatedAt.ToLocalTime().ToString("HH:mm");
            ImGui.TextColored(Ui.Grey, time);
            ImGui.SameLine();
            ImGui.TextColored(r.Roll >= 10 ? Ui.Gold : r.Roll == 1 ? Ui.Red : Ui.Green, $"{r.Roll,2}");
            ImGui.SameLine();
            ImGui.TextUnformatted(Describe(r));
        }
        if (s.Rolls.Count == 0) ImGui.TextColored(Ui.Grey, "No rolls yet. Use /random 10.");
    }

    private static void DrawSavesCalled(GardenState s)
    {
        var pending = s.Saves.Where(x => x.Outcome == "pending").OrderBy(x => x.Id).ToList();
        if (pending.Count == 0) return;

        var me = Plugin.LocalPlayerName;
        Ui.Header("SAVES CALLED - roll /random 10");
        foreach (var sv in pending)
        {
            var mine = me != null && sv.Target.Equals(me, StringComparison.OrdinalIgnoreCase);
            var line = $"{sv.Target}: {sv.ActionName} - fail on 1-{Math.Max(1, sv.FailMax)} ({sv.Damage} DMG{(sv.Status != "" ? " + " + sv.Status : "")})";
            if (mine) ImGui.TextColored(Ui.Gold, ">> YOU: " + line);
            else ImGui.TextUnformatted(line);
        }
    }

    private static void DrawStatuses(GardenState s)
    {
        if (s.Session.Statuses.Count == 0) return;
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("Status effects")) return;
        foreach (var st in s.Session.Statuses)
        {
            ImGui.TextColored(Ui.Violet, st.Name);
            ImGui.SameLine();
            ImGui.TextUnformatted($"{st.Effect}. Clears: {st.Clears}");
        }
    }

    public static string Describe(RollEntry r) => r.Outcome switch
    {
        "attack" => $"{r.Roller} hits {r.Target} for {r.Amount}{Suffix(r)}",
        "miss" => $"{r.Roller} attacks {r.Target}: dodged!",
        "heal" => $"{r.Roller} heals {r.Target} for {r.Amount}{Suffix(r)}",
        "save" => string.IsNullOrEmpty(r.Target) ? $"{r.Roller} saves{Suffix(r)}" : $"{r.Roller} vs {r.Target}{Suffix(r)}",
        "check" => $"{r.Roller} makes a check{Suffix(r)}",
        "enemy" => $"{r.Roller} uses {r.Target} (+{r.Amount} Enemy Momentum)",
        "ignored" => $"{r.Roller} (not counted)",
        _ => $"{r.Roller} rolls... (GM resolving)",
    };

    private static string Suffix(RollEntry r) => string.IsNullOrWhiteSpace(r.Note) ? "" : $" - {r.Note}";
}
