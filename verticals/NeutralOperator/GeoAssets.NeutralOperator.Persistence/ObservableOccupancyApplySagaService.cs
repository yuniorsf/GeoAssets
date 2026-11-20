using System.Diagnostics;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Logging;

namespace GeoAssets.NeutralOperator.Persistence;

/// <summary>
/// Spans, metrics and logs around <see cref="IOccupancyApplySagaService"/> (XD01-383), in the adapter project because the
/// application layer references no telemetry library — the arrangement of <see cref="ObservableOccupancyCommitService"/>.
///
/// <para>Spans: <c>Saga.AssetMutationApplied</c>, <c>Saga.AssetMutationRejected</c>, <c>Saga.AssetMutationRetry</c>, each a
/// child of the consume (or request) span it runs in — so the approval, the kernel's apply and the reply are one trace — and
/// tagged with the batch, order and organization and the outcome. Metrics: <c>geoassets.saga.apply.outcomes</c> by outcome
/// and, for a reply that ended a saga, <c>geoassets.saga.apply.duration</c>; for a revert whose compensation left poles
/// untouched (XD01-392), <c>geoassets.saga.apply.compensation_conflicts</c> by order type and a warning naming them; never a
/// batch, order or organization id as a tag. An applied batch whose saga had already reverted is logged as an error for an
/// operator to reconcile.</para>
/// </summary>
public sealed partial class ObservableOccupancyApplySagaService : IOccupancyApplySagaService
{
    private readonly IOccupancyApplySagaService _inner;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly GeoAssetsMeter _metrics;
    private readonly ILogger<ObservableOccupancyApplySagaService> _logger;

    public ObservableOccupancyApplySagaService(
        IOccupancyApplySagaService inner,
        GeoAssetsActivitySource tracer,
        GeoAssetsMeter metrics,
        ILogger<ObservableOccupancyApplySagaService> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _tracer = tracer;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>The <c>outcome</c> tag of an <see cref="OccupancyApplySagaOutcome"/>.</summary>
    public static string OutcomeTag(OccupancyApplySagaOutcome outcome) => outcome switch
    {
        OccupancyApplySagaOutcome.Committed => "committed",
        OccupancyApplySagaOutcome.Reverted => "reverted",
        OccupancyApplySagaOutcome.Duplicate => "duplicate",
        OccupancyApplySagaOutcome.AppliedAfterRevert => "applied-after-revert",
        _ => "ignored",
    };

    public Task<OccupancyApplySagaResult> HandleAppliedAsync(AssetBatchAppliedEvent applied, IntegrationMessageContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(applied);

        return ObserveAsync(
            "AssetMutationApplied", applied.BatchId, applied.SourceOrderId, applied.OrganizationId,
            () => _inner.HandleAppliedAsync(applied, context, ct));
    }

    public Task<OccupancyApplySagaResult> HandleRejectedAsync(AssetMutationBatchRejectedEvent rejected, IntegrationMessageContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rejected);

        return ObserveAsync(
            "AssetMutationRejected", rejected.BatchId, rejected.SourceOrderId, rejected.OrganizationId,
            async () =>
            {
                var result = await _inner.HandleRejectedAsync(rejected, context, ct).ConfigureAwait(false);
                if (result.Outcome == OccupancyApplySagaOutcome.Reverted)
                {
                    LogReverted(_logger, rejected.BatchId, rejected.SourceOrderId, rejected.ReasonCode);
                    if (result.Saga is { UnrestoredFeatureIds.Count: > 0 } saga)
                    {
                        Activity.Current?.SetTag("saga.unrestored_count", saga.UnrestoredFeatureIds.Count);
                        _metrics.RecordSagaApplyCompensationConflict(saga.OrderTypeId);
                        LogCompensationConflict(_logger, saga.BatchId, saga.OrderId, string.Join(", ", saga.UnrestoredFeatureIds));
                    }
                }

                return result;
            });
    }

    public async Task<OccupancyApplySagaView> RetryAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        using var span = _tracer.StartSagaActivity("AssetMutationRetry", orderId: orderId, organizationId: principal.OrganizationId);
        try
        {
            var view = await _inner.RetryAsync(orderId, principal, ct).ConfigureAwait(false);
            span?.SetTag("catalog.batch.id", view.BatchId.ToString("D"));
            span?.SetTag("saga.retry_count", view.RetryCount);
            LogRetried(_logger, view.BatchId, orderId, view.RetryCount);
            return view;
        }
        catch (Exception ex)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
    }

    private async Task<OccupancyApplySagaResult> ObserveAsync(
        string step, Guid batchId, string? orderId, Guid organizationId, Func<Task<OccupancyApplySagaResult>> handle)
    {
        using var span = _tracer.StartSagaActivity(step, batchId, orderId, organizationId.ToString("D"));
        try
        {
            var result = await handle().ConfigureAwait(false);
            var outcome = OutcomeTag(result.Outcome);
            span?.SetTag("saga.outcome", outcome);

            double? duration = result is { Outcome: OccupancyApplySagaOutcome.Committed or OccupancyApplySagaOutcome.Reverted, Saga: { CompletedAt: { } end } saga }
                ? (end - saga.RequestedAt).TotalSeconds
                : null;
            _metrics.RecordSagaApplyOutcome(outcome, duration);

            if (result.Outcome == OccupancyApplySagaOutcome.AppliedAfterRevert)
                LogAppliedAfterRevert(_logger, batchId, orderId);
            return result;
        }
        catch (Exception ex)
        {
            span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Approval saga of batch {BatchId} (order {OrderId}) reverted: the asset catalog rejected it ({ReasonCode}).")]
    private static partial void LogReverted(ILogger logger, Guid batchId, string? orderId, string reasonCode);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Approval saga of batch {BatchId} (order {OrderId}) reverted without restoring pole(s) {UnrestoredFeatureIds}: a later change superseded the approval there, so their occupancy was kept.")]
    private static partial void LogCompensationConflict(ILogger logger, Guid batchId, string orderId, string unrestoredFeatureIds);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Approval saga of batch {BatchId} (order {OrderId}) staged again by an operator (retry {RetryCount}).")]
    private static partial void LogRetried(ILogger logger, Guid batchId, string orderId, int retryCount);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The asset catalog applied batch {BatchId} (order {OrderId}) after its approval saga had reverted: the catalog holds occupancy the vertical rolled back. Reconcile by hand.")]
    private static partial void LogAppliedAfterRevert(ILogger logger, Guid batchId, string? orderId);
}
