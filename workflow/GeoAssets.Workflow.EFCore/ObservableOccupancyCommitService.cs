using System.Diagnostics;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// Span and metrics around <see cref="IOccupancyCommitService.ApproveAndCommitAsync"/>
/// (XD01-318), in the adapter project because the workflow core references no telemetry library
/// — the arrangement of <see cref="ObservableSharedUseDispatchService"/>.
///
/// The span is <c>ServiceOrder.OccupancyCommit</c>, tagged with the order, the caller's
/// organization and, when the approval completed, the order type and how many sets and changes it
/// committed. Metrics carry no organization, order, pole or occupant id: committed changes are
/// counted per kind, conflicts are counted, and the duration is tagged with the outcome. A
/// failure records the exception's type only.
/// </summary>
public sealed class ObservableOccupancyCommitService : IOccupancyCommitService
{
    public const string OutcomeOk = "ok";
    public const string OutcomeConflict = "conflict";
    public const string OutcomeRejected = "rejected";
    public const string OutcomeCancelled = "cancelled";
    public const string OutcomeError = "error";

    private readonly IOccupancyCommitService _inner;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly GeoAssetsMeter _metrics;
    private readonly TimeProvider _timeProvider;

    public ObservableOccupancyCommitService(
        IOccupancyCommitService inner,
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

    public async Task<OccupancyCommitResult> ApproveAndCommitAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var started = _timeProvider.GetTimestamp();
        using var span = _tracer.StartOccupancyCommitActivity(orderId, principal.OrganizationId);

        try
        {
            var result = await _inner.ApproveAndCommitAsync(orderId, principal, ct).ConfigureAwait(false);

            GeoAssetsActivitySource.SetOccupancyCommitCounts(span, result.OrderTypeId, result.ChangeSetsCommitted, result.ChangesCommitted);
            _metrics.RecordOccupancyCommitDuration(_timeProvider.GetElapsedTime(started).TotalMilliseconds, OutcomeOk);
            foreach (var (kind, count) in result.ChangesByKind)
                _metrics.RecordOccupancyChangesCommitted(count, kind.ToString());
            return result;
        }
        catch (Exception ex)
        {
            var outcome = Outcome(ex);
            span?.AddTag("occupancy.outcome", outcome);
            span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            _metrics.RecordOccupancyCommitDuration(_timeProvider.GetElapsedTime(started).TotalMilliseconds, outcome);
            if (string.Equals(outcome, OutcomeConflict, StringComparison.Ordinal))
                _metrics.RecordOccupancyCommitConflict();
            throw;
        }
    }

    internal static string Outcome(Exception ex) => ex switch
    {
        OperationCanceledException => OutcomeCancelled,
        OccupancyConflictException or ServiceOrderConcurrencyException => OutcomeConflict,
        ArgumentException or KeyNotFoundException or OrderActionDeniedException
            or InvalidServiceOrderTransitionException => OutcomeRejected,
        _ => OutcomeError,
    };
}
