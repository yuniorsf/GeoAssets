using Microsoft.Extensions.Options;

namespace GeoAssets.Web.Services.Session;

/// <summary>
/// Tracks user inactivity and fires events when the warning threshold or
/// full timeout is reached.
///
/// Lifecycle:
///   1. <see cref="Start"/> is called by <c>SessionActivityTracker</c> after first render.
///   2. JS activity listeners call <see cref="RecordActivity"/> via JS interop.
///   3. When inactivity exceeds the warning threshold, <see cref="OnStateChanged"/> fires
///      every second so the overlay can update its countdown.
///   4. When inactivity exceeds the full timeout, <see cref="OnTimeout"/> fires once.
/// </summary>
public sealed class SessionTimeoutService : IAsyncDisposable
{
    private readonly SessionConfig _config;

    private DateTime              _lastActivity = DateTime.UtcNow;
    private CancellationTokenSource _cts        = new();
    private Task?                 _runTask;

    /// <summary>Fired every second while in the warning window (and once on reset).</summary>
    public event Action? OnStateChanged;

    /// <summary>Fired once when the inactivity timeout is reached.</summary>
    public event Action? OnTimeout;

    public bool IsInWarning  { get; private set; }
    public int  SecondsLeft  { get; private set; } = int.MaxValue;

    public SessionTimeoutService(IOptions<SessionConfig> config)
        => _config = config.Value;

    /// <summary>Starts the background inactivity monitoring loop.</summary>
    public void Start()
    {
        if (_runTask is not null) return;
        _lastActivity = DateTime.UtcNow;
        _runTask = RunAsync(_cts.Token);
    }

    /// <summary>Called on any user interaction — resets the inactivity timer.</summary>
    public void RecordActivity()
    {
        _lastActivity = DateTime.UtcNow;

        if (IsInWarning)
        {
            IsInWarning = false;
            SecondsLeft = _config.InactivityTimeoutMinutes * 60;
            OnStateChanged?.Invoke(); // dismiss the overlay
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(ct))
        {
            var elapsed         = (DateTime.UtcNow - _lastActivity).TotalSeconds;
            var timeoutSeconds  = _config.InactivityTimeoutMinutes * 60;
            var remaining       = timeoutSeconds - (int)elapsed;

            SecondsLeft = Math.Max(0, remaining);

            if (remaining <= 0)
            {
                IsInWarning = false;
                OnStateChanged?.Invoke();
                OnTimeout?.Invoke();
                return;
            }

            if (remaining <= _config.WarningBeforeTimeoutSeconds)
            {
                IsInWarning = true;
                OnStateChanged?.Invoke();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        if (_runTask is not null)
        {
            try   { await _runTask; }
            catch (OperationCanceledException) { /* expected */ }
        }

        _cts.Dispose();
    }
}
