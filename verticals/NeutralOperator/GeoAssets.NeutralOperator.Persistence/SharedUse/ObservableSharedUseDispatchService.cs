using System.Diagnostics;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.NeutralOperator.Persistence.SharedUse;

/// <summary>
/// Span and metrics around <see cref="ISharedUseDispatchService.DispatchAsync"/> (XD01-316), in
/// the adapter project because the workflow core references no telemetry library — the same
/// arrangement as <see cref="GeoAssets.Workflow.Persistence.ObservableOrderTargetSelectionService"/>.
///
/// The span is <c>ServiceOrder.FanOutDispatch</c>, tagged with the order, the caller's
/// organization and, when the dispatch completed, the order type and what it did; the child
/// spans <see cref="ActivitySharedUseFanOutTracer"/> starts nest under it. Metrics carry no
/// organization, order, pole or occupant id. A failure records the exception's type only.
/// </summary>
public sealed class ObservableSharedUseDispatchService : ISharedUseDispatchService
{
    public const string OutcomeOk = "ok";
    public const string OutcomeBlocked = "blocked";
    public const string OutcomeRejected = "rejected";
    public const string OutcomeCancelled = "cancelled";
    public const string OutcomeError = "error";

    private readonly ISharedUseDispatchService _inner;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly GeoAssetsMeter _metrics;
    private readonly TimeProvider _timeProvider;

    public ObservableSharedUseDispatchService(
        ISharedUseDispatchService inner,
        GeoAssetsActivitySource tracer,
        GeoAssetsMeter metrics,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(metrics);

        _inner = inner;
        _tracer = tracer;
        _metrics = metrics;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SharedUseDispatchResult> DispatchAsync(
        string orderId, byte[] expectedRowVersion, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var started = _timeProvider.GetTimestamp();
        using var span = _tracer.StartFanOutDispatchActivity(orderId, principal.OrganizationId);

        try
        {
            var result = await _inner.DispatchAsync(orderId, expectedRowVersion, principal, ct).ConfigureAwait(false);

            GeoAssetsActivitySource.SetFanOutCounts(span, result.OrderTypeId, result.ChildOrdersCreated, result.UnnotifiablePoleCount);
            _metrics.RecordFanOutDuration(_timeProvider.GetElapsedTime(started).TotalMilliseconds, OutcomeOk);
            _metrics.RecordFanOutChildrenSpawned(result.ChildOrdersCreated, result.OrderTypeId);
            return result;
        }
        catch (Exception ex)
        {
            var outcome = Outcome(ex);
            span?.AddTag("fanout.outcome", outcome);
            span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            _metrics.RecordFanOutDuration(_timeProvider.GetElapsedTime(started).TotalMilliseconds, outcome);
            throw;
        }
    }

    internal static string Outcome(Exception ex) => ex switch
    {
        OperationCanceledException => OutcomeCancelled,
        SharedUseDispatchBlockedException => OutcomeBlocked,
        ArgumentException or KeyNotFoundException or OrderActionDeniedException or OrderTypeHasNoFanOutException
            or InvalidServiceOrderTransitionException or ServiceOrderConcurrencyException => OutcomeRejected,
        _ => OutcomeError,
    };
}
