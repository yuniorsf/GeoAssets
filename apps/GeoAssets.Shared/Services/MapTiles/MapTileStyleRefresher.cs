using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services.MapTiles;

/// <summary>
/// When a <see cref="MapRenderMode.MapLibre"/> map re-reads the kernel's <c>/tiles/style.json</c> (XD01-435): the tiles'
/// <c>{v}</c> changes with every committed write (XD01-434), and only <c>style.json</c> says which one is current. Each
/// attached map is refreshed every <see cref="MapLibreOptions.StyleRefreshInterval"/> (others' writes) and
/// <see cref="MapLibreOptions.WriteRefreshDelay"/> after this client's last write (<see cref="NotifyWrite"/>, debounced:
/// a burst of writes is one re-read, after the server committed them). One refresh per map runs at a time; one asked for
/// meanwhile runs once right after it. A failing refresh is logged, never thrown — the next one tries again. What a
/// refresh does (the conditional re-read and the swap of the sources' tile URLs) is <c>geoassets.maplibre.js</c>'s.
/// </summary>
public sealed partial class MapTileStyleRefresher : IDisposable
{
    private readonly Func<string, CancellationToken, Task> _refresh;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _writeDelay;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, MapEntry> _maps = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _disposed = new();
    private bool _isDisposed;

    private sealed class MapEntry
    {
        public ITimer? Periodic { get; set; }
        public ITimer? AfterWrite { get; set; }
        public bool Running { get; set; }
        public bool Pending { get; set; }
    }

    /// <param name="refresh">Re-reads the style of the map with the given div id.</param>
    /// <param name="options">The intervals; <see cref="MapLibreOptions.EnsureValid"/> must hold.</param>
    /// <param name="timeProvider">The clock the timers run on.</param>
    /// <param name="logger">Where a failing refresh is reported.</param>
    public MapTileStyleRefresher(
        Func<string, CancellationToken, Task> refresh, MapLibreOptions options, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        MapLibreOptions.EnsureValid(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _refresh = refresh;
        _interval = options.StyleRefreshInterval;
        _writeDelay = options.WriteRefreshDelay;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>The div ids of the maps refreshed now.</summary>
    public IReadOnlyCollection<string> AttachedMaps
    {
        get { lock (_gate) return [.. _maps.Keys]; }
    }

    /// <summary>Starts refreshing the map <paramref name="divId"/> (a no-op when it already is).</summary>
    public void Attach(string divId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(divId);
        lock (_gate)
        {
            if (_isDisposed || _maps.ContainsKey(divId)) return;
            var entry = new MapEntry();
            if (_interval > TimeSpan.Zero)
                entry.Periodic = _timeProvider.CreateTimer(OnTimer, divId, _interval, _interval);
            _maps[divId] = entry;
        }
    }

    /// <summary>Stops refreshing the map <paramref name="divId"/>; a refresh already running finishes.</summary>
    public void Detach(string divId)
    {
        ArgumentNullException.ThrowIfNull(divId);
        lock (_gate)
        {
            if (!_maps.Remove(divId, out var entry)) return;
            StopTimers(entry);
        }
    }

    /// <summary>This client wrote: every attached map refreshes <see cref="MapLibreOptions.WriteRefreshDelay"/> after the
    /// last such call.</summary>
    public void NotifyWrite()
    {
        lock (_gate)
        {
            foreach (var (divId, entry) in _maps)
            {
                if (entry.AfterWrite is { } timer)
                    timer.Change(_writeDelay, Timeout.InfiniteTimeSpan);
                else
                    entry.AfterWrite = _timeProvider.CreateTimer(OnTimer, divId, _writeDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            foreach (var entry in _maps.Values) StopTimers(entry);
            _maps.Clear();
        }
        _disposed.Cancel();
        _disposed.Dispose();
    }

    private void OnTimer(object? state) => _ = RefreshAsync((string)state!);

    private async Task RefreshAsync(string divId)
    {
        MapEntry? entry;
        CancellationToken cancellationToken;
        lock (_gate)
        {
            if (_isDisposed || !_maps.TryGetValue(divId, out entry)) return;
            if (entry.Running)
            {
                entry.Pending = true;
                return;
            }
            entry.Running = true;
            cancellationToken = _disposed.Token;
        }

        try
        {
            bool again;
            do
            {
                try
                {
                    await _refresh(divId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogRefreshFailed(_logger, divId, ex);
                }

                lock (_gate)
                {
                    again = entry.Pending && !cancellationToken.IsCancellationRequested
                        && _maps.TryGetValue(divId, out var current) && ReferenceEquals(current, entry);
                    entry.Pending = false;
                }
            }
            while (again);
        }
        finally
        {
            lock (_gate) entry.Running = false;
        }
    }

    private static void StopTimers(MapEntry entry)
    {
        entry.Periodic?.Dispose();
        entry.AfterWrite?.Dispose();
        entry.Periodic = null;
        entry.AfterWrite = null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Map {DivId}: re-reading the tile style failed; the next refresh tries again")]
    private static partial void LogRefreshFailed(ILogger logger, string divId, Exception ex);
}
