using System.Text.Json;
using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services;

/// <summary>Default <see cref="IProjectAutosaveService"/> — see that interface for the full
/// contract (XD01-143). Same <see cref="PeriodicTimer"/>-driven-by-<see cref="TimeProvider"/>
/// shape as <c>SessionTimeoutService</c>.</summary>
public sealed class ProjectAutosaveService : IProjectAutosaveService, IAsyncDisposable
{
    private const string StorageKey = "geoassets.project-autosave";
    private const int DefaultIntervalMinutes = 10;

    private readonly IProjectSessionService _session;
    private readonly IStorageService _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProjectAutosaveService> _logger;

    private readonly CancellationTokenSource _cts = new();
    private PeriodicTimer? _timer;
    private Task? _runTask;

    public bool Enabled { get; private set; } = true;
    public int IntervalMinutes { get; private set; } = DefaultIntervalMinutes;
    public event EventHandler<Exception>? AutosaveFailed;
    public event EventHandler<DateTimeOffset>? AutosaveSucceeded;

    public ProjectAutosaveService(
        IProjectSessionService session, IStorageService storage, TimeProvider timeProvider,
        ILogger<ProjectAutosaveService> logger)
    {
        _session = session;
        _storage = storage;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        var stored = await _storage.GetStringAsync(StorageKey, ct);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            var settings = JsonSerializer.Deserialize<PersistedSettings>(stored);
            if (settings is not null)
            {
                Enabled = settings.Enabled;
                IntervalMinutes = settings.IntervalMinutes;
            }
        }

        _timer = new PeriodicTimer(TimeSpan.FromMinutes(IntervalMinutes), _timeProvider);
        _runTask = RunAsync(_cts.Token);
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        Enabled = enabled;
        return PersistAsync(ct);
    }

    public Task SetIntervalMinutesAsync(int intervalMinutes, CancellationToken ct = default)
    {
        if (intervalMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes), "Interval must be positive.");

        IntervalMinutes = intervalMinutes;
        if (_timer is not null)
            _timer.Period = TimeSpan.FromMinutes(intervalMinutes);

        return PersistAsync(ct);
    }

    private Task PersistAsync(CancellationToken ct) =>
        _storage.SetStringAsync(StorageKey, JsonSerializer.Serialize(new PersistedSettings(Enabled, IntervalMinutes)), ct);

    private async Task RunAsync(CancellationToken ct)
    {
        while (_timer is not null && await _timer.WaitForNextTickAsync(ct))
        {
            if (!Enabled) continue;
            if (_session.Current is null) continue;
            if (!_session.IsDirty) continue;

            try
            {
                await _session.SaveAsync(ct);
                AutosaveSucceeded?.Invoke(this, _timeProvider.GetUtcNow());
            }
            catch (Exception ex)
            {
                // Never a silent no-op — see the interface's own doc comment.
                _logger.LogWarning(ex, "Autosave failed");
                AutosaveFailed?.Invoke(this, ex);
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
        _timer?.Dispose();
    }

    private sealed record PersistedSettings(bool Enabled, int IntervalMinutes);
}
