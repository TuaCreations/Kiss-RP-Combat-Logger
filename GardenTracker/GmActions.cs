using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace GardenTracker;

/// <summary>GM-side game logic. Everything here reads the latest snapshot and writes through GardenApi.</summary>
public sealed class GmActions
{
    private readonly Plugin p;
    public GmActions(Plugin plugin) => p = plugin;

    private GardenApi Api => p.Api;
    private GardenState? State => p.Sync.Current;

    public PartyMember? FindMember(string name) =>
        State?.Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // ---------- rolls ----------

    public void ResolveAttack(RollEntry roll, Enemy enemy, int bonus)
    {
        p.Run(async () =>
        {
            var member = FindMember(roll.Roller);
            if (member != null)
                await Api.UpsertMember(new { id = member.Id, momentum = member.Momentum + roll.Roll });

            if (enemy.DodgeMax > 0 && roll.Roll <= enemy.DodgeMax)
            {
                await Api.ResolveRoll(roll.Id, "miss", enemy.Name, 0, $"{enemy.Name} dodges");
                return;
            }

            var dmg = Math.Max(0, Rules.Amount(roll.Roll) + bonus);
            var hp = Math.Max(0, enemy.Hp - dmg);
            await Api.UpsertEnemy(new { id = enemy.Id, hp });
            var note = Rules.IsCrit(roll.Roll) ? "CRIT - roll again" : Rules.Label(roll.Roll);
            if (hp == 0) note += $" - {enemy.Name} falls!";
            await Api.ResolveRoll(roll.Id, "attack", enemy.Name, dmg, note);
        });
    }

    public void ResolveHeal(RollEntry roll, PartyMember target, int bonus)
    {
        p.Run(async () =>
        {
            var healer = FindMember(roll.Roller);
            if (healer != null && healer.Id != target.Id)
                await Api.UpsertMember(new { id = healer.Id, momentum = healer.Momentum + roll.Roll });

            var heal = Math.Max(0, Rules.Amount(roll.Roll) + bonus);
            var hp = Math.Min(target.HpMax, target.Hp + heal);
            var selfMomentum = healer != null && healer.Id == target.Id ? target.Momentum + roll.Roll : target.Momentum;
            await Api.UpsertMember(new { id = target.Id, hp, momentum = selfMomentum });
            await Api.ResolveRoll(roll.Id, "heal", target.Name, heal, Rules.IsCrit(roll.Roll) ? "CRIT - roll again" : "");
        });
    }

    public void ResolveSimple(RollEntry roll, string outcome, string note = "")
    {
        var n = note;
        if (outcome == "save" && roll.Roll >= 10) n = "Natural 10 - Counter Attack!";
        if (roll.Roll == 1 && outcome is "save" or "check") n = "Natural 1 - automatic failure";
        p.Run(() => Api.ResolveRoll(roll.Id, outcome, "", 0, n));
    }

    public void ResolveEnemyRoll(RollEntry roll)
    {
        p.Run(async () =>
        {
            var s = State?.Session;
            if (s != null) await Api.SetSession(new { enemy_momentum = s.EnemyMomentum + roll.Roll });
            await Api.ResolveRoll(roll.Id, "enemy", "Enemy Momentum", roll.Roll, "");
        });
    }

    // ---------- party ----------

    public void AddMember(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (FindMember(name) != null) { p.Status = $"{name} is already in the party."; return; }
        var sort = (State?.Members.Count ?? 0) + 1;
        p.Run(() => Api.UpsertMember(new { name = name.Trim(), sort }));
    }

    public void AddPartyList()
    {
        foreach (var m in Plugin.PartyList)
        {
            var name = m.Name.TextValue;
            if (!string.IsNullOrWhiteSpace(name)) AddMember(name);
        }
    }

    public void AdjustMemberHp(PartyMember m, int delta) =>
        p.Run(() => Api.UpsertMember(new { id = m.Id, hp = Math.Clamp(m.Hp + delta, 0, m.HpMax) }));

    public void UsePotion(PartyMember m)
    {
        if (m.Potions <= 0) { p.Status = $"{m.Name} has no potions left."; return; }
        p.Run(() => Api.UpsertMember(new { id = m.Id, potions = m.Potions - 1, hp = Math.Min(m.HpMax, m.Hp + Rules.PotionHeal) }));
    }

    public void UseInterception(PartyMember m)
    {
        if (m.Interception <= 0) { p.Status = $"{m.Name} has already used Interception."; return; }
        p.Run(() => Api.UpsertMember(new { id = m.Id, interception = m.Interception - 1 }));
    }

    public void UnleashMomentum(PartyMember m)
    {
        if (m.Momentum < m.MomentumMax) { p.Status = $"{m.Name} needs {m.MomentumMax} Momentum."; return; }
        p.Run(() => Api.UpsertMember(new { id = m.Id, momentum = m.Momentum - m.MomentumMax }));
        p.Status = $"{m.Name} unleashes Momentum: {Rules.MomentumUnleashDamage} DMG or {Rules.MomentumUnleashHeal} healing, + 2 rolls. Apply it with the HP buttons.";
    }

    public void ResetMember(PartyMember m) =>
        p.Run(() => Api.UpsertMember(new { id = m.Id, hp = m.HpMax, shield = 0, potions = m.PotionsMax, interception = 1, effects = "", momentum = 0 }));

    // ---------- enemies ----------

    public void AdjustEnemyHp(Enemy e, int delta) =>
        p.Run(() => Api.UpsertEnemy(new { id = e.Id, hp = Math.Clamp(e.Hp + delta, 0, e.HpMax) }));

    // ---------- enemy action macros ----------

    /// <summary>Living party members an action can target.</summary>
    private System.Collections.Generic.List<PartyMember> Living() =>
        State?.Members.Where(m => m.Hp > 0).ToList() ?? new();

    /// <summary>Works out who an action targets. "choose" uses the GM's picks.</summary>
    public System.Collections.Generic.List<PartyMember> PickTargets(EnemyAction a, System.Collections.Generic.IReadOnlyCollection<Guid> chosen)
    {
        var living = Living();
        var count = Math.Max(1, a.Count);
        return a.Targets switch
        {
            "all" => living,
            "random" => living.OrderBy(_ => Random.Shared.Next()).Take(count).ToList(),
            "highest_momentum" => living.OrderByDescending(m => m.Momentum).ThenBy(_ => Random.Shared.Next()).Take(count).ToList(),
            "lowest_hp" => living.OrderBy(m => m.Hp).ThenBy(_ => Random.Shared.Next()).Take(count).ToList(),
            _ => living.Where(m => chosen.Contains(m.Id)).ToList(),
        };
    }

    /// <summary>
    /// Fires an enemy ability: calls for a save from each target (the server resolves each save
    /// when that player's next /random arrives) and rolls the enemy's d10 into Enemy Momentum.
    /// </summary>
    public void ExecuteAction(Enemy? enemy, EnemyAction a, System.Collections.Generic.List<PartyMember> targets, bool isMomentumAction = false)
    {
        if (targets.Count == 0) { p.Status = $"{a.Name}: no targets."; return; }
        var saves = targets.Select(t => new
        {
            target = t.Name,
            enemy_id = enemy?.Id.ToString() ?? "",
            enemy_name = enemy?.Name ?? "Enemy",
            action_name = a.Name,
            fail_max = a.FailMax,
            damage = a.Damage,
            status = a.Status,
            heal_enemy = a.HealSelf,
            reset_momentum = a.ResetMomentum,
        }).ToList();

        var session = State?.Session;
        var rollEnemyDie = p.Configuration.PluginRollsEnemyDice && !isMomentumAction;
        var enemyRoll = Random.Shared.Next(1, 11);

        p.Run(async () =>
        {
            await Api.CreateSaves(saves);
            if (session == null) return;
            if (isMomentumAction)
            {
                await Api.SetSession(new { enemy_momentum = Math.Max(0, session.EnemyMomentum - session.EnemyMomentumMax) });
            }
            else if (rollEnemyDie)
            {
                await Api.SetSession(new { enemy_momentum = session.EnemyMomentum + enemyRoll });
                var id = await Api.LogRoll(enemy?.Name ?? "Enemy", enemyRoll);
                if (id > 0) await Api.ResolveRoll(id, "enemy", a.Name, enemyRoll, "");
            }
        });

        var names = string.Join(", ", targets.Select(t => t.Name));
        p.Status = $"{(enemy?.Name ?? "Enemy")} uses {a.Name} on {names}. Saves called: /random 10.";
        if (rollEnemyDie && session != null)
        {
            p.Status += $" Enemy rolled {enemyRoll} (+Momentum).";
            if (session.EnemyMomentum + enemyRoll >= session.EnemyMomentumMax && session.MomentumAction != null)
                p.Status += $" ENEMY MOMENTUM FULL: {session.MomentumAction.Name} is ready.";
        }
    }

    public void CancelPendingSaves()
    {
        var pending = State?.Saves.Where(s => s.Outcome == "pending").ToList() ?? new();
        p.Run(async () => { foreach (var s in pending) await Api.CancelSave(s.Id); });
    }

    // ---------- markers ----------

    public void PlaceMarker(Enemy? enemy, Vector3 pos, Marker? existing, string label = "", string color = "red", float radius = 3f)
    {
        var territory = (int)Plugin.ClientState.TerritoryType;
        if (existing != null)
            p.Run(() => Api.UpsertMarker(new { id = existing.Id, territory, x = pos.X, y = pos.Y, z = pos.Z }));
        else
            p.Run(() => Api.UpsertMarker(new
            {
                enemy_id = enemy?.Id.ToString() ?? "",
                label = enemy?.Name ?? label,
                territory, x = pos.X, y = pos.Y, z = pos.Z, radius, color,
            }));
    }

    public static Vector3? MyPosition => Plugin.ObjectTable.LocalPlayer?.Position;
    public static Vector3? TargetPosition => Plugin.TargetManager.Target?.Position;
}
