using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// Keeps this replica subscribed to the shared tile versions (XD01-458): one <see cref="ITileVersionStore.ListenAsync"/>
/// at a time, handing every change to <see cref="TileVersionSync.ApplyAsync"/> and each subscription to
/// <see cref="TileVersionSync.OnSubscribedAsync"/>; a lost subscription is logged, counted
/// (<c>geoassets.tiles.versions.reconnects</c>) and retried after <see cref="TileVersionSyncOptions.ReconnectDelay"/>,
/// doubling up to <see cref="TileVersionSyncOptions.MaxReconnectDelay"/> until one succeeds. Alongside, every
/// <see cref="TileVersionSyncOptions.RecoveryInterval"/>, it retries <see cref="TileVersionSync.RecoverAsync"/>. Every delay
/// is the <see cref="TimeProvider"/>'s; stopping the host cancels both loops and closes the subscription.
/// </summary>
public sealed partial class TileVersionListener : BackgroundService
{
    private readonly ITileVersionStore _store;
    private readonly TileVersionSync _sync;
    private readonly TileVersionSyncOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TileVersionListener> _logger;

    public TileVersionListener(
        ITileVersionStore store, TileVersionSync sync, TileVersionSyncOptions options, TimeProvider timeProvider, ILogger<TileVersionListener> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _sync = sync;
        _options = options.Validated();
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recovery = RecoverAsync(stoppingToken);
        await ListenAsync(stoppingToken).ConfigureAwait(false);
        await recovery.ConfigureAwait(false);
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        var delay = _options.ReconnectDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _store.ListenAsync(
                    async ct =>
                    {
                        await _sync.OnSubscribedAsync(ct).ConfigureAwait(false);
                        delay = _options.ReconnectDelay;
                    },
                    _sync.ApplyAsync,
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                TileMetrics.Reconnected();
                LogSubscriptionLost(_logger, ex, delay.TotalMilliseconds);
            }

            if (!await DelayAsync(delay, stoppingToken).ConfigureAwait(false)) return;
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxReconnectDelay.Ticks));
        }
    }

    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        while (await DelayAsync(_options.RecoveryInterval, stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _sync.RecoverAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogRecoveryFailed(_logger, ex);
            }
        }
    }

    /// <summary>Waits <paramref name="delay"/>; <c>false</c> once the host is stopping.</summary>
    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The subscription to the shared tile versions was lost; subscribing again in {DelayMilliseconds} ms (the changes announced meanwhile are treated as unknown: the tile cache is emptied and the versions re-read)")]
    private static partial void LogSubscriptionLost(ILogger logger, Exception ex, double delayMilliseconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Flushing every replica for a refused tile version change failed; retrying")]
    private static partial void LogRecoveryFailed(ILogger logger, Exception ex);
}
