using GeoAssets.IpBilling.GrantSync;
using Microsoft.Extensions.Options;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Keeps the município grant-sync outbox (<c>MunicipioGrantSyncRequests</c>, XD01-395) from growing without bound (XD01-400).
/// Soon after start and then every <see cref="MunicipioGrantSyncOptions.PruneInterval"/> it deletes, through
/// <see cref="IMunicipioGrantSyncStore.PruneAsync"/> in batches of <see cref="MunicipioGrantSyncOptions.PruneBatchSize"/>,
/// the requests completed more than <see cref="MunicipioGrantSyncOptions.CompletedRetention"/> ago and — only when
/// <see cref="MunicipioGrantSyncOptions.FailedRetention"/> is set — those failed more than that ago. A município's latest
/// request, which its <c>grantSyncState</c> is derived from, and unfinished requests are never deleted. Each run counts what
/// it deleted on <c>geoassets.ipbilling.grant_sync_pruned</c>; a failed run is logged and the next interval tries again.
/// Runs only while <see cref="MunicipioGrantSyncOptions.Enabled"/>, like the dispatcher. One scope per run.
/// </summary>
internal sealed partial class MunicipioGrantSyncPruner : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly MunicipioGrantSyncOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly MunicipioGrantSyncMetrics _metrics;
    private readonly ILogger<MunicipioGrantSyncPruner> _logger;

    public MunicipioGrantSyncPruner(
        IServiceScopeFactory scopes,
        IOptions<MunicipioGrantSyncOptions> options,
        TimeProvider timeProvider,
        MunicipioGrantSyncMetrics metrics,
        ILogger<MunicipioGrantSyncPruner> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _scopes = scopes;
        _options = options.Value;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>One run: prunes batch after batch until one comes back short; returns how many requests it deleted.</summary>
    internal async Task<int> PruneOnceAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var completedBefore = now - _options.CompletedRetention;
        DateTimeOffset? failedBefore = _options.FailedRetention is { } failedRetention ? now - failedRetention : null;

        var scope = _scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IMunicipioGrantSyncStore>();

            var total = 0;
            int deleted;
            do
            {
                ct.ThrowIfCancellationRequested();
                deleted = await store
                    .PruneAsync(completedBefore, failedBefore, _options.PruneBatchSize, ct)
                    .ConfigureAwait(false);
                total += deleted;
            }
            while (deleted >= _options.PruneBatchSize);

            _metrics.RecordPruned(total);
            if (total > 0) LogPruned(_logger, total);
            else LogNothingToPrune(_logger);

            return total;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogRunFailed(_logger, _options.PruneInterval.TotalMinutes, ex);
            }

            try
            {
                await Task.Delay(_options.PruneInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pruned {Count} finished município grant sync requests.")]
    private static partial void LogPruned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No finished município grant sync request was due for pruning.")]
    private static partial void LogNothingToPrune(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pruning the município grant sync requests failed; trying again in {RetryDelayMinutes:F0} min.")]
    private static partial void LogRunFailed(ILogger logger, double retryDelayMinutes, Exception exception);
}
