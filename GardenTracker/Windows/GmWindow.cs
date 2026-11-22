using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GardenTracker.Windows;

/// <summary>GM controls: session setup, resolving rolls, editing the party and enemies, placing markers.</summary>
public sealed class GmWindow : Window
{
    private readonly Plugin p;

    // session setup
    private string newCode = "";
    private string newName = "";
    private string keyInput = "";

    // roll resolution
    private int bonus;
    private Guid attackTarget;
    private Guid healTarget;

    // misc inputs
    private string addName = "";
    private int presetPlayers = 10;

    // enemy actions
    private readonly System.Collections.Generic.HashSet<Guid> chosenTargets = new();

    // scenario import
    private string scenarioFile = "";
    private Scenario? scenarioPreview;
    private System.Collections.Generic.List<string> scenarioProblems = new();
    private string scenarioSource = "";
    private string freeLabel = "";

    private readonly System.Collections.Generic.Dictionary<Guid, float> radiusEdits = new();

    private static readonly string[] Colors ={ "red", "violet", "gold", "green", "blue", "white" };

    public GmWindow(Plugin plugin) : base("K.I.S.S. RP Combat Logger - GM##GardenTrackerGm")
    {
        p = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        keyInput = p.Configuration.GmKey;
    }

    public override void Draw()
    {
        if (!string.IsNullOrEmpty(p.Status))
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(p.Status.StartsWith("Error") ? Ui.Red : Ui.Gold, p.Status);
            ImGui.PopTextWrapPos();
            ImGui.SameLine();
            if (ImGui.SmallButton("x##clearstatus")) p.Status = "";
        }

        if (!ImGui.BeginTabBar("gmtabs")) return;
        if (ImGui.BeginTabItem("Session")) { DrawSession(); ImGui.EndTabItem(); }

        var s = p.Sync.Current;
        if (p.IsGm && s != null)
        {
            var pending = s.Rolls.Count(r => r.Outcome == "pending");
            if (ImGui.BeginTabItem(pending > 0 ? $"Rolls ({pending})###rolls" : "Rolls###rolls")) { DrawRolls(s); ImGui.EndTabItem(); }
            var owed = s.Saves.Count(x => x.Outcome == "pending");
            if (ImGui.BeginTabItem(owed > 0 ? $"Enemy Actions ({owed})###actions" : "Enemy Actions###actions")) { DrawActions(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Party")) { DrawParty(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Enemies")) { DrawEnemies(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Markers")) { DrawMarkers(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Scenario")) { DrawScenario(s); ImGui.EndTabItem(); }
        }
        ImGui.EndTabBar();
    }

    // ------------------------------------------------------------------ Session

    private void DrawSession()
    {
        var cfg = p.Configuration;
        Ui.Header("UNLOCK GM CONTROLS");
        ImGui.TextUnformatted($"Session: {(string.IsNullOrEmpty(cfg.SessionCode) ? "(none - join one in the main window)" : cfg.SessionCode)}");
        ImGui.SetNextItemWidth(180);
        ImGui.InputText("GM key##key", ref keyInput, 64, ImGuiInputTextFlags.Password);
        ImGui.SameLine();
        if (ImGui.Button("Unlock"))
        {
            cfg.GmKey = keyInput;
            p.Run(async () =>
            {
                p.IsGm = await p.Api.CheckKey();
                p.Status = p.IsGm ? $"GM controls unlocked for {cfg.SessionCode}." : "Error: wrong GM key or session.";
                if (!cfg.RememberGmKey && !p.IsGm) cfg.GmKey = "";
                cfg.Save();
            });
        }
        ImGui.SameLine();
        ImGui.TextColored(p.IsGm ? Ui.Green : Ui.Grey, p.IsGm ? "Unlocked" : "Locked");

        var remember = cfg.RememberGmKey;
        if (ImGui.Checkbox("Remember key on this PC", ref remember)) { cfg.RememberGmKey = remember; if (!remember) cfg.GmKey = ""; cfg.Save(); }

        Ui.Header("CREATE A NEW SESSION");
        ImGui.SetNextItemWidth(140);
        ImGui.InputTextWithHint("##newcode", "CODE (A-Z, 0-9, -)", ref newCode, 32);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##newname", "Event name", ref newName, 64);
        ImGui.TextColored(Ui.Grey, "Uses the GM key above (6+ characters). Share only the CODE with players.");
        if (ImGui.Button("Create session"))
        {
            var code = newCode.Trim().ToUpperInvariant();
            var name = newName.Trim();
            var key = keyInput;
            p.Run(async () =>
            {
                await p.Api.CreateSession(code, name, key);
                cfg.SessionCode = code;
                cfg.GmKey = key;
                cfg.Save();
                p.Sync.Reset();
                p.IsGm = true;
                p.Status = $"Session {code} created. Players join with /garden join {code}";
            });
        }

        Ui.Header("OPTIONS");
        var auto = cfg.LogRollsAutomatically;
        if (ImGui.Checkbox("Log /random rolls from chat automatically", ref auto)) { cfg.LogRollsAutomatically = auto; cfg.Save(); }
        var roster = cfg.OnlyRosterRolls;
        if (ImGui.Checkbox("Only log rolls from players on the Party list", ref roster)) { cfg.OnlyRosterRolls = roster; cfg.Save(); }
        Ui.Tooltip("Bystanders in the zone using /random are ignored. Add players in the Party tab.");
        var enemyDice = cfg.PluginRollsEnemyDice;
        if (ImGui.Checkbox("Enemy actions roll their own d10 for Enemy Momentum", ref enemyDice)) { cfg.PluginRollsEnemyDice = enemyDice; cfg.Save(); }
        Ui.Tooltip("Off: roll /random 10 yourself and resolve it with the Enemy button on the Rolls tab.");
        var d10 = cfg.OnlyD10Rolls;
        if (ImGui.Checkbox("Only count d10 rolls (/random 10)", ref d10)) { cfg.OnlyD10Rolls = d10; cfg.Save(); }
        var hp = cfg.ShowOverlayHp;
        if (ImGui.Checkbox("Show HP on world markers", ref hp)) { cfg.ShowOverlayHp = hp; cfg.Save(); }
        var lines = cfg.RollLogLines;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Roll log lines", ref lines)) { cfg.RollLogLines = Math.Clamp(lines, 3, 50); cfg.Save(); }

        var s = p.Sync.Current;
        if (p.IsGm && s != null)
        {
            Ui.Header("ENCOUNTER");
            ImGui.TextUnformatted("Passive / notes shown to players:");
            if (Ui.EditMultiline("passive", s.Session.Passive, out var passive, new Vector2(-1, 60)))
                p.Run(() => p.Api.SetSession(new { passive }));

            ImGui.TextUnformatted("Enemy Momentum");
            ImGui.SameLine();
            if (Ui.EditInt("em", s.Session.EnemyMomentum, out var em)) p.Run(() => p.Api.SetSession(new { enemy_momentum = Math.Max(0, em) }));
            ImGui.SameLine();
            ImGui.TextUnformatted("/");
            ImGui.SameLine();
            if (Ui.EditInt("emmax", s.Session.EnemyMomentumMax, out var emMax)) p.Run(() => p.Api.SetSession(new { enemy_momentum_max = Math.Max(1, emMax) }));
            ImGui.SameLine();
            if (ImGui.Button("Unleash (spend max)"))
                p.Run(() => p.Api.SetSession(new { enemy_momentum = Math.Max(0, s.Session.EnemyMomentum - s.Session.EnemyMomentumMax) }));
        }
    }

    // ------------------------------------------------------------------ Rolls

    private void DrawRolls(GardenState s)
    {
        var live = s.Enemies.Where(e => e.Hp > 0 || e.HpMax == 0).ToList();
        if (live.Count > 0 && !live.Any(e => e.Id == attackTarget)) attackTarget = live[0].Id;
        if (s.Members.Count > 0 && !s.Members.Any(m => m.Id == healTarget)) healTarget = s.Members[0].Id;

        ImGui.TextUnformatted("Attack target:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(170);
        var atk = s.Enemies.FirstOrDefault(e => e.Id == attackTarget);
        if (ImGui.BeginCombo("##atk", atk?.Name ?? "-"))
        {
            foreach (var e in s.Enemies)
                if (ImGui.Selectable($"{e.Name} ({e.Hp}/{e.HpMax})##{e.Id}", e.Id == attackTarget)) attackTarget = e.Id;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Heal target:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        var heal = s.Members.FirstOrDefault(m => m.Id == healTarget);
        if (ImGui.BeginCombo("##heal", heal?.Name ?? "-"))
        {
            foreach (var m in s.Members)
                if (ImGui.Selectable($"{m.Name} ({m.Hp}/{m.HpMax})##{m.Id}", m.Id == healTarget)) healTarget = m.Id;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70);
        ImGui.InputInt("Bonus", ref bonus);
        Ui.Tooltip("Extra damage/healing from modifiers (Cold Hearth weakness, buffs). Resets after each roll.");

        ImGui.Separator();
        var pending = s.Rolls.Where(r => r.Outcome == "pending").OrderBy(r => r.Id).ToList();
        if (pending.Count == 0) ImGui.TextColored(Ui.Grey, "No rolls waiting. Rolls appear here when players use /random 10 near you.");

        foreach (var r in pending)
        {
            ImGui.PushID((int)r.Id);
            ImGui.TextColored(r.Roll >= 10 ? Ui.Gold : r.Roll == 1 ? Ui.Red : Ui.Green, $"{r.Roll,2}");
            ImGui.SameLine();
            var known = p.Gm.FindMember(r.Roller) != null;
            ImGui.TextColored(known ? Ui.Gold : Ui.Grey, r.Roller);
            if (!known) Ui.Tooltip("Not on the party list - Momentum won't be tracked. Add them in the Party tab.");
            ImGui.SameLine(220);

            if (ImGui.SmallButton("Attack") && atk != null) { p.Gm.ResolveAttack(r, atk, bonus); bonus = 0; }
            Ui.Tooltip(atk == null ? "Pick an attack target first" : $"Hit {atk.Name}: {Rules.Amount(r.Roll) + bonus} DMG unless it dodges");
            ImGui.SameLine();
            if (ImGui.SmallButton("Heal") && heal != null) { p.Gm.ResolveHeal(r, heal, bonus); bonus = 0; }
            Ui.Tooltip(heal == null ? "Pick a heal target first" : $"Heal {heal.Name} for {Rules.Amount(r.Roll) + bonus}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Save")) p.Gm.ResolveSimple(r, "save");
            ImGui.SameLine();
            if (ImGui.SmallButton("Check")) p.Gm.ResolveSimple(r, "check");
            ImGui.SameLine();
            if (ImGui.SmallButton("Enemy")) p.Gm.ResolveEnemyRoll(r);
            Ui.Tooltip("A GM roll for an enemy action: adds to Enemy Momentum");
            ImGui.SameLine();
            if (ImGui.SmallButton("Ignore")) p.Gm.ResolveSimple(r, "ignored");
            ImGui.PopID();
        }

        Ui.Header("RECENT");
        foreach (var r in s.Rolls.Where(r => r.Outcome != "pending").Take(10))
            ImGui.TextColored(Ui.Grey, $"{r.Roll,2}  {TrackerWindow.Describe(r)}");

        ImGui.Spacing();
        if (ImGui.Button("Log a manual roll for me"))
        {
            var me = Plugin.LocalPlayerName ?? "GM";
            var roll = Random.Shared.Next(1, 11);
            p.Run(() => p.Api.LogRoll(me, roll));
        }
        Ui.Tooltip("For testing, or when a roll was missed by chat");
        ImGui.SameLine();
        if (ImGui.Button("Clear roll log") && ImGui.GetIO().KeyCtrl) p.Run(() => p.Api.ClearRolls());
        Ui.Tooltip("Hold Ctrl and click to clear every roll in this session");
    }

    // ------------------------------------------------------------------ Party

    private void DrawParty(GardenState s)
    {
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##addname", "Character name", ref addName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add")) { p.Gm.AddMember(addName); addName = ""; }
        ImGui.SameLine();
        if (ImGui.Button("Add target")) { var t = Plugin.TargetManager.Target?.Name.TextValue; if (t != null) p.Gm.AddMember(t); }
        ImGui.SameLine();
        if (ImGui.Button("Add my party")) p.Gm.AddPartyList();
        ImGui.SameLine();
        if (ImGui.Button("Add me")) { var me = Plugin.LocalPlayerName; if (me != null) p.Gm.AddMember(me); }

        ImGui.Separator();
        if (!ImGui.BeginTable("gmparty", 8, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit, new Vector2(-1, -1)))
            return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("HP");
        ImGui.TableSetupColumn("Shield");
        ImGui.TableSetupColumn("Potions");
        ImGui.TableSetupColumn("Int.");
        ImGui.TableSetupColumn("Momentum");
        ImGui.TableSetupColumn("Effects");
        ImGui.TableSetupColumn("");
        ImGui.TableHeadersRow();

        foreach (var m in s.Members)
        {
            var id = m.Id.ToString();
            ImGui.PushID(id);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(m.Hp <= 0 ? Ui.Grey : Ui.Gold, m.Name);

            ImGui.TableNextColumn();
            if (ImGui.SmallButton("-")) p.Gm.AdjustMemberHp(m, -1);
            ImGui.SameLine();
            if (Ui.EditInt($"{id}hp", m.Hp, out var hp, 36)) p.Run(() => p.Api.UpsertMember(new { id = m.Id, hp = Math.Clamp(hp, 0, m.HpMax) }));
            ImGui.SameLine();
            ImGui.TextUnformatted($"/{m.HpMax}");
            ImGui.SameLine();
            if (ImGui.SmallButton("+")) p.Gm.AdjustMemberHp(m, +1);

            ImGui.TableNextColumn();
            if (Ui.EditInt($"{id}sh", m.Shield, out var sh, 36)) p.Run(() => p.Api.UpsertMember(new { id = m.Id, shield = Math.Max(0, sh) }));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{m.Potions}/{m.PotionsMax}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Use")) p.Gm.UsePotion(m);
            Ui.Tooltip($"Spend a potion: +{Rules.PotionHeal} HP");

            ImGui.TableNextColumn();
            if (m.Interception > 0) { if (ImGui.SmallButton("Use##int")) p.Gm.UseInterception(m); }
            else ImGui.TextColored(Ui.Grey, "Used");

            ImGui.TableNextColumn();
            if (Ui.EditInt($"{id}mo", m.Momentum, out var mo, 36)) p.Run(() => p.Api.UpsertMember(new { id = m.Id, momentum = Math.Max(0, mo) }));
            ImGui.SameLine();
            if (m.Momentum >= m.MomentumMax) { if (ImGui.SmallButton("Unleash")) p.Gm.UnleashMomentum(m); }
            else ImGui.TextColored(Ui.Grey, $"/{m.MomentumMax}");

            ImGui.TableNextColumn();
            if (Ui.EditText($"{id}fx", m.Effects, out var fx, 120)) p.Run(() => p.Api.UpsertMember(new { id = m.Id, effects = fx }));

            ImGui.TableNextColumn();
            if (ImGui.SmallButton("Reset")) p.Gm.ResetMember(m);
            Ui.Tooltip("Full HP, potions and Interception; clear Momentum and effects");
            ImGui.SameLine();
            if (ImGui.SmallButton("X") && ImGui.GetIO().KeyCtrl) p.Run(() => p.Api.DeleteMember(m.Id));
            Ui.Tooltip("Ctrl+click to remove from the party");

            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    // ------------------------------------------------------------------ Enemies

    private void DrawEnemies(GardenState s)
    {
        if (ImGui.Button("Add blank enemy")) p.Run(() => p.Api.UpsertEnemy(new { name = "New enemy", hp = 10, hp_max = 10, sort = s.Enemies.Count + 1 }));

        ImGui.Separator();
        ImGui.BeginChild("enemies", new Vector2(-1, -1));
        foreach (var e in s.Enemies)
        {
            var id = e.Id.ToString();
            ImGui.PushID(id);

            var vis = e.Visible;
            if (ImGui.Checkbox("##vis", ref vis)) p.Run(() => p.Api.UpsertEnemy(new { id = e.Id, visible = vis }));
            Ui.Tooltip("Visible to players");
            ImGui.SameLine();
            if (Ui.EditText($"{id}name", e.Name, out var name, 180)) p.Run(() => p.Api.UpsertEnemy(new { id = e.Id, name }));
            ImGui.SameLine();
            ImGui.TextUnformatted("HP");
            ImGui.SameLine();
            if (ImGui.SmallButton("-5")) p.Gm.AdjustEnemyHp(e, -5);
            ImGui.SameLine();
            if (ImGui.SmallButton("-1")) p.Gm.AdjustEnemyHp(e, -1);
            ImGui.SameLine();
            if (Ui.EditInt($"{id}hp", e.Hp, out var hp, 44)) p.Run(() => p.Api.UpsertEnemy(new { id = e.Id, hp = Math.Max(0, hp) }));
            ImGui.SameLine();
            ImGui.TextUnformatted("/");
            ImGui.SameLine();
            if (Ui.EditInt($"{id}max", e.HpMax, out var max, 44)) p.Run(() => p.Api.UpsertEnemy(new { id = e.Id, hp_max = Math.Max(0, max) }));
            ImGui.SameLine();
            if (ImGui.SmallButton("+1")) p.Gm.AdjustEnemyHp(e, +1);
            ImGui.SameLine();
            if (ImGui.SmallButton("+5")) p.Gm.AdjustEnemyHp(e, +5);

            ImGui.TextUnformatted("   Dodges on 1-");
            ImGui.SameLine();
            if (Ui.EditInt($"{id}dodge", e.DodgeMax, out var dodge, 36)) p.Run(() => p.Api.UpsertEnemy(new { id = e.Id, dodge_max = Math.Clamp(dodge, 0, 9) }));
            Ui.Tooltip("0 = never dodges");
            ImGui.SameLine();
            ImGui.TextUnformatted("Effects");
            ImGui.SameLine();
            if (Ui.EditText($"{id}fx", e.Effects, out var fx, 160)) p.Run(() => p.Api.UpsertEnemy(new { id = e.Id, effects = fx }));
            ImGui.SameLine();
            if (ImGui.SmallButton("Delete") && ImGui.GetIO().KeyCtrl) p.Run(() => p.Api.DeleteEnemy(e.Id));
            Ui.Tooltip("Ctrl+click to delete (also removes its marker)");

            if (ImGui.TreeNode("Abilities"))
            {
                if (Ui.EditMultiline($"{id}ab", e.Abilities, out var ab, new Vector2(-1, 90)))
                    p.Run(() => p.Api.UpsertEnemy(new { id = e.Id, abilities = ab }));
                ImGui.TreePop();
            }
            ImGui.Separator();
            ImGui.PopID();
        }
        ImGui.EndChild();
    }

    // ------------------------------------------------------------------ Markers

    private void DrawMarkers(GardenState s)
    {
        var territory = (int)Plugin.ClientState.TerritoryType;
        var me = GmActions.MyPosition;
        var target = GmActions.TargetPosition;
        ImGui.TextColored(Ui.Grey, "Markers are drawn in the world for everyone in this zone running the plugin. Stand where the enemy is, or target an object, then place.");

        Ui.Header("ENEMY MARKERS");
        foreach (var e in s.Enemies)
        {
            ImGui.PushID(e.Id.ToString());
            var marker = s.Markers.FirstOrDefault(m => m.EnemyId == e.Id);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(e.Name);
            ImGui.SameLine(200);
            if (ImGui.SmallButton("At me") && me.HasValue) p.Gm.PlaceMarker(e, me.Value, marker);
            ImGui.SameLine();
            if (ImGui.SmallButton("At target") && target.HasValue) p.Gm.PlaceMarker(e, target.Value, marker);
            Ui.Tooltip("Target a player, NPC or object standing in for the enemy");
            if (marker != null)
            {
                ImGui.SameLine();
                DrawMarkerControls(marker, territory);
            }
            ImGui.PopID();
        }

        Ui.Header("OTHER MARKERS (objectives, rally points)");
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##freelabel", "Label, e.g. The Anchor", ref freeLabel, 64);
        ImGui.SameLine();
        if (ImGui.Button("Place at me") && me.HasValue && !string.IsNullOrWhiteSpace(freeLabel))
        {
            p.Gm.PlaceMarker(null, me.Value, null, freeLabel.Trim(), "gold", 2f);
            freeLabel = "";
        }
        foreach (var m in s.Markers.Where(m => m.EnemyId == null))
        {
            ImGui.PushID(m.Id.ToString());
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(m.Label);
            ImGui.SameLine(200);
            if (ImGui.SmallButton("Move to me") && me.HasValue) p.Gm.PlaceMarker(null, me.Value, m);
            ImGui.SameLine();
            DrawMarkerControls(m, territory);
            ImGui.PopID();
        }
    }

    private void DrawMarkerControls(Marker m, int territory)
    {
        if (!radiusEdits.TryGetValue(m.Id, out var radius)) radius = m.Radius;
        ImGui.SetNextItemWidth(90);
        ImGui.SliderFloat("##r", ref radius, 0.5f, 15f, "r %.1f");
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            var r = radius;
            p.Run(() => p.Api.UpsertMarker(new { id = m.Id, radius = r }));
        }
        if (ImGui.IsItemActive()) radiusEdits[m.Id] = radius; else radiusEdits.Remove(m.Id);
        Ui.Tooltip("Ring radius in yalms");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.BeginCombo("##c", m.Color))
        {
            foreach (var c in Colors)
                if (ImGui.Selectable(c, c == m.Color)) p.Run(() => p.Api.UpsertMarker(new { id = m.Id, color = c }));
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Remove")) p.Run(() => p.Api.DeleteMarker(m.Id));
        if (m.Territory != territory) { ImGui.SameLine(); ImGui.TextColored(Ui.Grey, "(other zone)"); }
    }

    // ------------------------------------------------------------------ Enemy actions

    private void DrawActions(GardenState s)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextColored(Ui.Grey, "Click an ability to call for saves. Each target's next /random 10 resolves their save automatically: a fail applies damage (shield first) and the status, and a natural 10 counters.");
        ImGui.PopTextWrapPos();

        // Target picker for "choose" abilities
        Ui.Header("CHOSEN TARGETS (for 'choose' abilities)");
        var living = s.Members.Where(m => m.Hp > 0).ToList();
        chosenTargets.RemoveWhere(id => !living.Any(m => m.Id == id));
        var col = 0;
        foreach (var m in living)
        {
            var on = chosenTargets.Contains(m.Id);
            if (ImGui.Checkbox($"{m.Name}##pick{m.Id}", ref on)) { if (on) chosenTargets.Add(m.Id); else chosenTargets.Remove(m.Id); }
            if (++col % 4 != 0) ImGui.SameLine(col % 4 * 150f);
        }
        if (col % 4 != 0) ImGui.NewLine();
        if (ImGui.SmallButton("Pick my target")) { var t = Plugin.TargetManager.Target?.Name.TextValue; var m = living.FirstOrDefault(x => x.Name == t); if (m != null) chosenTargets.Add(m.Id); }
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear picks")) chosenTargets.Clear();

        // Ability buttons per enemy
        foreach (var e in s.Enemies.Where(e => e.Actions.Count > 0))
        {
            Ui.Header(e.Visible ? e.Name : $"{e.Name} (hidden)");
            if (e.HpMax > 0 && e.Hp <= 0) { ImGui.TextColored(Ui.Grey, "Defeated."); continue; }
            foreach (var a in e.Actions)
            {
                ImGui.PushID($"{e.Id}{a.Name}");
                var needsPicks = a.Targets == "choose";
                var picks = needsPicks ? chosenTargets.Count : 0;
                var label = a.Phase > 0 ? $"[P{a.Phase}] {a.Name}" : a.Name;
                if (ImGui.Button(label))
                {
                    if (needsPicks && picks == 0) p.Status = $"{a.Name}: tick who it targets first.";
                    else
                    {
                        var targets = p.Gm.PickTargets(a, chosenTargets.ToList());
                        p.Gm.ExecuteAction(e, a, targets);
                        if (needsPicks) chosenTargets.Clear();
                    }
                }
                ImGui.SameLine();
                ImGui.TextColored(Ui.Grey, a.Summary() + (string.IsNullOrWhiteSpace(a.Note) ? "" : $" - {a.Note}"));
                if (needsPicks && picks > 0 && picks != a.Count) { ImGui.SameLine(); ImGui.TextColored(Ui.Gold, $"({picks} picked, ability says {a.Count})"); }
                ImGui.PopID();
            }
        }

        // Momentum action
        var ma = s.Session.MomentumAction;
        if (ma != null)
        {
            Ui.Header("ENEMY MOMENTUM");
            var full = s.Session.EnemyMomentum >= s.Session.EnemyMomentumMax;
            Ui.Bar((float)s.Session.EnemyMomentum / Math.Max(1, s.Session.EnemyMomentumMax), $"{s.Session.EnemyMomentum} / {s.Session.EnemyMomentumMax}", full ? Ui.Gold : Ui.Violet, 260, 16);
            ImGui.SameLine();
            if (!full) ImGui.BeginDisabled();
            if (ImGui.Button($"Unleash {ma.Name}"))
            {
                var source = s.Enemies.FirstOrDefault(x => x.Visible && x.Hp > 0);
                p.Gm.ExecuteAction(source, ma, p.Gm.PickTargets(ma, chosenTargets.ToList()), isMomentumAction: true);
            }
            if (!full) ImGui.EndDisabled();
            ImGui.TextColored(Ui.Grey, ma.Summary() + (string.IsNullOrWhiteSpace(ma.Note) ? "" : $" - {ma.Note}"));
        }

        // Outstanding and recent saves
        Ui.Header("SAVES");
        var pending = s.Saves.Where(x => x.Outcome == "pending").OrderBy(x => x.Id).ToList();
        if (pending.Count == 0) ImGui.TextColored(Ui.Grey, "No saves waiting.");
        foreach (var sv in pending)
        {
            ImGui.PushID((int)sv.Id);
            ImGui.TextColored(Ui.Gold, sv.Target);
            ImGui.SameLine();
            ImGui.TextUnformatted($"vs {sv.ActionName} (fail 1-{Math.Max(1, sv.FailMax)}, {sv.Damage} DMG{(sv.Status != "" ? " + " + sv.Status : "")})");
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel")) p.Run(() => p.Api.CancelSave(sv.Id));
            Ui.Tooltip("Use for Interception or a GM call; no damage is applied");
            ImGui.PopID();
        }
        if (pending.Count > 1 && ImGui.SmallButton("Cancel all waiting saves") && ImGui.GetIO().KeyCtrl) p.Gm.CancelPendingSaves();
        if (pending.Count > 1) Ui.Tooltip("Ctrl+click");

        foreach (var sv in s.Saves.Where(x => x.Outcome != "pending").Take(8))
        {
            var (c, word) = sv.Outcome switch
            {
                "failed" => (Ui.Red, $"failed ({sv.Roll}) - took {sv.Applied}"),
                "countered" => (Ui.Gold, $"natural 10 - COUNTER"),
                "cancelled" => (Ui.Grey, "cancelled"),
                _ => (Ui.Green, $"saved ({sv.Roll})"),
            };
            ImGui.TextColored(c, $"{sv.Target} vs {sv.ActionName}: {word}");
        }
    }

    // ------------------------------------------------------------------ Scenario

    private void DrawScenario(GardenState s)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(string.IsNullOrEmpty(s.Session.ScenarioName) ? "No scenario loaded." : $"Loaded: {s.Session.ScenarioName}");
        ImGui.TextColored(Ui.Grey, "Scenarios are kiss-scenario/1 JSON files (see SCENARIO_FORMAT.md). Loading one replaces the enemies, markers and waiting saves. The party and roll log stay.");
        ImGui.PopTextWrapPos();

        Ui.Header("1. PICK A SCENARIO");
        foreach (var name in ScenarioLoader.BuiltInNames())
        {
            if (ImGui.Button($"Built-in: {name}")) Preview(ScenarioLoader.ReadBuiltIn(name), $"built-in {name}");
            ImGui.SameLine();
        }
        if (ImGui.Button("Paste from clipboard")) Preview(ImGui.GetClipboardText() ?? "", "clipboard");
        Ui.Tooltip("Copy the whole JSON Claude gives you, then click this");

        var folder = Plugin.PluginInterface.GetPluginConfigDirectory();
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##scfile", "file name in the scenarios folder", ref scenarioFile, 128);
        ImGui.SameLine();
        if (ImGui.Button("Load file"))
        {
            var path = System.IO.Path.IsPathRooted(scenarioFile) ? scenarioFile : System.IO.Path.Combine(folder, scenarioFile);
            try { Preview(System.IO.File.ReadAllText(path), System.IO.Path.GetFileName(path)); }
            catch (Exception ex) { scenarioPreview = null; scenarioProblems = new() { ex.Message }; }
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Open folder"))
        {
            System.IO.Directory.CreateDirectory(folder);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); } catch { /* ignore */ }
        }
        Ui.Tooltip(folder);

        if (scenarioProblems.Count > 0)
        {
            Ui.Header("PROBLEMS");
            foreach (var pr in scenarioProblems) ImGui.TextColored(Ui.Red, pr);
        }

        var sc = scenarioPreview;
        if (sc == null) return;

        Ui.Header($"2. PREVIEW: {sc.Name} ({scenarioSource})");
        if (!string.IsNullOrWhiteSpace(sc.Summary)) { ImGui.PushTextWrapPos(0); ImGui.TextUnformatted(sc.Summary); ImGui.PopTextWrapPos(); }
        ImGui.TextUnformatted("Players present");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("##players", ref presetPlayers);
        presetPlayers = Math.Clamp(presetPlayers, 1, 40);
        ImGui.SameLine();
        if (ImGui.SmallButton("Use party size")) presetPlayers = Math.Max(1, s.Members.Count);

        if (ImGui.BeginTable("scprev", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Enemy");
            ImGui.TableSetupColumn("HP");
            ImGui.TableSetupColumn("Dodge");
            ImGui.TableSetupColumn("Start");
            ImGui.TableSetupColumn("Abilities");
            ImGui.TableHeadersRow();
            foreach (var e in sc.Enemies)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(e.Name);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(e.HpFor(presetPlayers).ToString());
                ImGui.TableNextColumn(); ImGui.TextUnformatted(e.DodgeMax > 0 ? $"1-{e.DodgeMax}" : "-");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(e.Hidden ? "hidden" : "visible");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(string.Join(", ", e.Actions.Select(a => a.Name)));
            }
            ImGui.EndTable();
        }
        if (sc.MomentumAction != null) ImGui.TextColored(Ui.Violet, $"Momentum action: {sc.MomentumAction.Name} - {sc.MomentumAction.Summary()}");

        ImGui.Spacing();
        if (scenarioProblems.Count > 0) ImGui.BeginDisabled();
        if (ImGui.Button($"Load into {s.Session.Code}") && ImGui.GetIO().KeyCtrl)
        {
            var players = presetPlayers;
            p.Run(async () =>
            {
                await ScenarioLoader.Load(p.Api, sc, players);
                p.Status = $"Loaded {sc.Name} for {players} players.";
            });
        }
        if (scenarioProblems.Count > 0) ImGui.EndDisabled();
        Ui.Tooltip("Ctrl+click. Replaces the current enemies, markers and waiting saves.");
    }

    private void Preview(string json, string source)
    {
        var (sc, problems) = ScenarioLoader.Parse(json);
        scenarioPreview = sc;
        scenarioProblems = problems;
        scenarioSource = source;
    }
}
