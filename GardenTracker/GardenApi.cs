using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GardenTracker;

public sealed class GardenApiException(string message) : Exception(message);

/// <summary>Thin client for the garden_* Postgres functions exposed through Supabase's REST API.</summary>
public sealed class GardenApi : IDisposable
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Configuration config;

    public GardenApi(Configuration config) => this.config = config;

    private async Task<string> Rpc(string function, object args, CancellationToken ct = default)
    {
        var url = $"{config.SupabaseUrl.TrimEnd('/')}/rest/v1/rpc/{function}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("apikey", config.SupabaseKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(JsonSerializer.Serialize(args, Json), Encoding.UTF8, "application/json");

        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            string msg = body;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var m)) msg = m.GetString() ?? body;
            }
            catch { /* not JSON */ }
            throw new GardenApiException(msg);
        }
        return body;
    }

    // ---- everyone ----
    public async Task<GardenState?> GetState(string code, CancellationToken ct = default)
    {
        var body = await Rpc("garden_get_state", new { p_code = code }, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body) || body == "null") return null;
        return JsonSerializer.Deserialize<GardenState>(body, Json);
    }

    // ---- GM ----
    private string Code => config.SessionCode;
    private string Key => config.GmKey;

    public Task CreateSession(string code, string name, string gmKey) =>
        Rpc("garden_create_session", new { p_code = code, p_name = name, p_gm_key = gmKey });

    public async Task<bool> CheckKey()
    {
        try { await Rpc("garden_check_key", new { p_code = Code, p_key = Key }).ConfigureAwait(false); return true; }
        catch (GardenApiException) { return false; }
    }

    public Task SetSession(object patch) => Rpc("garden_set_session", new { p_code = Code, p_key = Key, p = patch });

    public Task UpsertMember(object row) => Rpc("garden_upsert_member", new { p_code = Code, p_key = Key, p = row });
    public Task DeleteMember(Guid id) => Rpc("garden_delete_member", new { p_code = Code, p_key = Key, p_id = id });

    public Task UpsertEnemy(object row) => Rpc("garden_upsert_enemy", new { p_code = Code, p_key = Key, p = row });
    public Task DeleteEnemy(Guid id) => Rpc("garden_delete_enemy", new { p_code = Code, p_key = Key, p_id = id });

    public async Task<long> LogRoll(string roller, int roll)
    {
        var body = await Rpc("garden_log_roll", new { p_code = Code, p_key = Key, p_roller = roller, p_roll = roll }).ConfigureAwait(false);
        return long.TryParse(body.Trim(), out var id) ? id : 0;
    }

    public Task ResolveRoll(long id, string outcome, string target, int amount, string note) =>
        Rpc("garden_resolve_roll", new { p_code = Code, p_key = Key, p_id = id, p_outcome = outcome, p_target = target, p_amount = amount, p_note = note });

    public Task ClearRolls() => Rpc("garden_clear_rolls", new { p_code = Code, p_key = Key });

    /// <summary>Player-side: pick your own attack or heal target. Needs only the session code.</summary>
    public Task SetIntent(string playerName, Guid? enemy, Guid? heal) =>
        Rpc("garden_set_intent", new { p_code = Code, p_name = playerName, p_enemy = enemy, p_heal = heal });

    public Task StartBank(Guid member, string kind, string label, Guid? enemy, Guid? heal, int amount, int rolls) =>
        Rpc("garden_start_bank", new { p_code = Code, p_key = Key, p_member = member, p_kind = kind, p_label = label, p_enemy = enemy, p_heal = heal, p_amount = amount, p_rolls = rolls });

    public Task CancelBank(Guid member) => Rpc("garden_cancel_bank", new { p_code = Code, p_key = Key, p_member = member });

    public Task CreateSaves(object saves) => Rpc("garden_create_saves", new { p_code = Code, p_key = Key, p = saves });
    public Task CancelSave(long id) => Rpc("garden_cancel_save", new { p_code = Code, p_key = Key, p_id = id });
    public Task ClearEncounter() => Rpc("garden_clear_encounter", new { p_code = Code, p_key = Key });

    public Task UpsertMarker(object row) => Rpc("garden_upsert_marker", new { p_code = Code, p_key = Key, p = row });
    public Task DeleteMarker(Guid id) => Rpc("garden_delete_marker", new { p_code = Code, p_key = Key, p_id = id });

    public void Dispose() => http.Dispose();
}
