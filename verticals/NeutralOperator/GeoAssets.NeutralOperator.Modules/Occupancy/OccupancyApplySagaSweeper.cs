using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using Microsoft.Extensions.Options;

namespace GeoAssets.NeutralOperator.Modules.Occupancy;

/// <summary>
/// The approval saga's timeout (XD01-383, ADR-001 risks 3.3): every <see cref="OccupancyApplySagaOptions.SweepInterval"/>
/// it flags the sagas still pending <see cref="OccupancyApplySagaOptions.ApplyTimeout"/> after their batch was last staged —
/// once each: their state stays pending (no silent revert; the kernel may still answer, an operator may retry) — counts them
/// on <c>geoassets.saga.apply.timeouts</c>, logs a structured warning per saga, and measures the backlog for the
/// <c>geoassets.saga.apply.pending</c> and <c>…oldest_pending_age</c> gauges the alert rules watch
/// (<c>deploy/otel/alerts/saga-apply-alerts.yaml</c>). Reads across organizations through
/// <see cref="IOccupancyApplySagaMonitor"/>; one scope per sweep. A failing sweep is logged and the next one runs on time.
/// </summary>
public sealed partial class OccupancyApplySagaSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly OccupancyApplySagaOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly GeoAssetsMeter _metrics;
    private readonly ILogger<OccupancyApplySagaSweeper> _logger;

    public OccupancyApplySagaSweeper(
        IServiceScopeFactory scopes,
        IOptions<OccupancyApplySagaOptions> options,
        TimeProvider timeProvider,
        GeoAssetsActivitySource tracer,
        GeoAssetsMeter metrics,
        ILogger<OccupancyApplySagaSweeper> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _scopes = scopes;
        _options = options.Value;
        _timeProvider = timeProvider;
        _tracer = tracer;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>One sweep: flags, counts and logs the timed-out sagas, measures the backlog; returns how many were flagged.</summary>
    public async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        using var span = _tracer.StartSagaActivity("TimeoutSweep");

        var scope = _scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var monitor = scope.ServiceProvider.GetRequiredService<IOccupancyApplySagaMonitor>();
            var now = _timeProvider.GetUtcNow();
            var staleBefore = now - _options.ApplyTimeout;
            var batchSize = Math.Max(1, _options.SweepBatchSize);

            var flagged = 0;
            for (var page = 0; page < Math.Max(1, _options.MaxPagesPerSweep); page++)
            {
                var timedOut = await monitor.MarkTimedOutAsync(staleBefore, now, batchSize, ct).ConfigureAwait(false);
                foreach (var saga in timedOut)
                {
                    LogTimedOut(
                        _logger, saga.BatchId, saga.OrderId, saga.OrganizationId,
                        (now - saga.LastRequestedAt).TotalSeconds, saga.RetryCount, _options.ApplyTimeout.TotalSeconds);
                }

                flagged += timedOut.Count;
                if (timedOut.Count < batchSize) break;
            }

            _metrics.RecordSagaApplyTimeouts(flagged);

            var (pending, oldest) = await monitor.GetBacklogAsync(ct).ConfigureAwait(false);
            var oldestAge = oldest is { } at ? Math.Max(0d, (now - at).TotalSeconds) : 0d;
            _metrics.SetSagaApplyBacklog(pending, oldestAge);

            span?.SetTag("saga.timed_out", flagged);
            span?.SetTag("saga.pending", pending);
            return flagged;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableSweeper) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogSweepFailed(_logger, ex);
            }

            try
            {
                await Task.Delay(_options.SweepInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Approval saga of batch {BatchId} (order {OrderId}, organization {OrganizationId}) has waited {AgeSeconds:F0} s for the asset catalog's reply (retries: {RetryCount}; timeout {TimeoutSeconds:F0} s). It stays pending: check the kernel's asset-mutation consumer, or retry it.")]
    private static partial void LogTimedOut(
        ILogger logger, Guid batchId, string orderId, Guid organizationId, double ageSeconds, int retryCount, double timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "The approval saga timeout sweep failed; the next one runs on schedule.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
