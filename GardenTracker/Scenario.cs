using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace GardenTracker;

/// <summary>A kiss-scenario/1 file. See SCENARIO_FORMAT.md.</summary>
public sealed class Scenario
{
    public const string FormatId = "kiss-scenario/1";

    public string Format { get; set; } = "";
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
    public int EnemyMomentumMax { get; set; } = 25;
    public string Passive { get; set; } = "";
    public List<StatusDef> Statuses { get; set; } = new();
    public EnemyAction? MomentumAction { get; set; }
    public List<ScenarioEnemy> Enemies { get; set; } = new();
}

public sealed class ScenarioEnemy
{
    public string Name { get; set; } = "";
    public int? Hp { get; set; }               // fixed HP, or...
    public int? HpPerPlayer { get; set; }      // ...HP that scales with the headcount
    public int DodgeMax { get; set; }
    public string Effects { get; set; } = "";
    public bool Hidden { get; set; }
    public int Phase { get; set; } = 1;
    public string Abilities { get; set; } = "";
    public List<EnemyAction> Actions { get; set; } = new();

    public int HpFor(int players) => Hp ?? Math.Max(1, (HpPerPlayer ?? 10) * Math.Max(1, players));
}

public static class ScenarioLoader
{
    private static readonly string[] TargetModes = { "choose", "random", "all", "highest_momentum", "lowest_hp" };

    /// <summary>Parses and validates. Returns the scenario plus a list of problems (empty = OK).</summary>
    public static (Scenario? Scenario, List<string> Problems) Parse(string json)
    {
        var problems = new List<string>();
        Scenario? sc;
        try
        {
            sc = JsonSerializer.Deserialize<Scenario>(json, new JsonSerializerOptions(GardenApi.Json)
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            problems.Add($"Not valid JSON: {ex.Message}");
            return (null, problems);
        }

        if (sc == null) { problems.Add("File is empty."); return (null, problems); }
        if (sc.Format != Scenario.FormatId) problems.Add($"\"format\" must be \"{Scenario.FormatId}\".");
        if (string.IsNullOrWhiteSpace(sc.Name)) problems.Add("Scenario needs a \"name\".");
        if (sc.Enemies.Count == 0) problems.Add("Scenario needs at least one enemy.");

        foreach (var e in sc.Enemies)
        {
            if (string.IsNullOrWhiteSpace(e.Name)) problems.Add("An enemy is missing its \"name\".");
            if (e.Hp == null && e.HpPerPlayer == null) problems.Add($"{e.Name}: give \"hp\" or \"hp_per_player\".");
            foreach (var a in e.Actions) CheckAction(a, $"{e.Name} / {a.Name}", problems);
        }
        if (sc.MomentumAction != null) CheckAction(sc.MomentumAction, $"momentum_action / {sc.MomentumAction.Name}", problems);
        return (sc, problems);
    }

    private static void CheckAction(EnemyAction a, string where, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(a.Name)) problems.Add($"{where}: action is missing \"name\".");
        if (!TargetModes.Contains(a.Targets)) problems.Add($"{where}: \"targets\" must be one of {string.Join(", ", TargetModes)}.");
        if (a.FailMax is < 0 or > 9) problems.Add($"{where}: \"fail_max\" must be 0-9.");
        if (a.Damage < 0) problems.Add($"{where}: \"damage\" can't be negative.");
    }

    public static string ReadBuiltIn(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                  ?? throw new FileNotFoundException($"Built-in scenario {fileName} not found.");
        using var stream = asm.GetManifestResourceStream(res)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IEnumerable<string> BuiltInNames() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && n.Contains(".Scenarios."))
            .Select(n => n[(n.IndexOf(".Scenarios.", StringComparison.Ordinal) + 11)..]);

    /// <summary>Replaces the session's enemies, markers and saves with this scenario. Party and roll log stay.</summary>
    public static async Task Load(GardenApi api, Scenario sc, int players)
    {
        await api.ClearEncounter();
        var sort = 0;
        foreach (var e in sc.Enemies)
        {
            var hp = e.HpFor(players);
            var text = string.IsNullOrWhiteSpace(e.Abilities)
                ? string.Join("\n", e.Actions.Select(a => $"{a.Name}: {a.Summary()}{(string.IsNullOrWhiteSpace(a.Note) ? "" : $" ({a.Note})")}"))
                : e.Abilities;
            await api.UpsertEnemy(new
            {
                name = e.Name, hp, hp_max = hp, dodge_max = e.DodgeMax, effects = e.Effects,
                abilities = text, visible = !e.Hidden, sort = ++sort, actions = e.Actions,
            });
        }
        await api.SetSession(new
        {
            scenario_name = sc.Name,
            enemy_momentum = 0,
            enemy_momentum_max = sc.EnemyMomentumMax,
            passive = sc.Passive,
            statuses = sc.Statuses,
            momentum_action = sc.MomentumAction,
        });
    }
}
