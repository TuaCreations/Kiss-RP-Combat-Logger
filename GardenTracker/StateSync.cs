using System;
using System.Threading;
using System.Threading.Tasks;

namespace GardenTracker;

/// <summary>
/// Polls garden_get_state on a background task and publishes an immutable snapshot for the UI.
/// The UI never mutates the snapshot; GM edits go to the server and the next poll brings them back.
/// </summary>
public sealed class StateSync : IDisposable
{
    private readonly GardenApi api;
    private readonly Configuration config;
    private readonly CancellationTokenSource cts = new();
    private volatile bool refreshNow;

    public GardenState? Current { get; private set; }
    public string? LastError { get; private set; }
    public DateTime LastSuccess { get; private set; }
    public bool Connected => Current != null && LastError == null;

    /// <summary>Raised on the background thread after every successful poll.</summary>
    public event Action<GardenState>? Updated;

    public StateSync(GardenApi api, Configuration config)
    {
        this.api = api;
        this.config = config;
        Task.Run(() => Loop(cts.Token));
    }

    public void RefreshSoon() => refreshNow = true;

    public void Reset()
    {
        Current = null;
        LastError = null;
        refreshNow = true;
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            refreshNow = false;
            var code = config.SessionCode;
            if (!string.IsNullOrWhiteSpace(code))
            {
                try
                {
                    var state = await api.GetState(code, ct).ConfigureAwait(false);
                    if (state == null)
                    {
                        Current = null;
                        LastError = $"No session called {code.ToUpperInvariant()}.";
                    }
                    else
                    {
                        Current = state;
                        LastError = null;
                        LastSuccess = DateTime.UtcNow;
                        Updated?.Invoke(state);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }

            // Sleep in small steps so RefreshSoon() feels instant after a GM action.
            var waited = 0;
            var interval = Math.Clamp(config.PollMilliseconds, 500, 10000);
            while (waited < interval && !refreshNow && !ct.IsCancellationRequested)
            {
                try { await Task.Delay(100, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                waited += 100;
            }
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
