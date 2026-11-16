using System.Diagnostics;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// Decorates an <see cref="IOrderTargetSelectionService"/> with the
/// <c>ServiceOrder.TargetSelection.Preview</c> / <c>.Apply</c> spans and the
/// <c>geoassets.orders.targets.*</c> metrics (XD01-308). It lives here, beside
/// <see cref="ObservableServiceOrderRepository"/>, because <c>GeoAssets.Workflow</c> does not
/// reference the observability project.
///
/// What is recorded is deliberately limited to counts and low-cardinality names. The
/// organization id goes on the span only; the selection's own values (fence coordinates,
/// identifiers) go nowhere. The <c>strategy</c> tag is the registered strategy's own id, or
/// <see cref="UnknownStrategy"/> — never the caller's raw string, which would let a client mint
/// metric series.
/// </summary>
public sealed class ObservableOrderTargetSelectionService : IOrderTargetSelectionService
{
    public const string PreviewMode = "preview";
    public const string ApplyMode = "apply";
    public const string UnknownStrategy = "unknown";

    public const string OutcomeOk = "ok";
    public const string OutcomeRejected = "rejected";
    public const string OutcomeTooLarge = "too_large";
    public const string OutcomeCancelled = "cancelled";
    public const string OutcomeError = "error";

    private readonly IOrderTargetSelectionService _inner;
    private readonly FeatureSelectionRegistry _strategies;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly GeoAssetsMeter _metrics;
    private readonly TimeProvider _timeProvider;

    public ObservableOrderTargetSelectionService(
        IOrderTargetSelectionService inner,
        FeatureSelectionRegistry strategies,
        GeoAssetsActivitySource tracer,
        GeoAssetsMeter metrics,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(metrics);

        _inner = inner;
        _strategies = strategies;
        _tracer = tracer;
        _metrics = metrics;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TargetSelectionPreview> PreviewAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        var strategy = StrategyTag(request.StrategyId);
        var started = _timeProvider.GetTimestamp();
        using var span = _tracer.StartTargetSelectionActivity("Preview", request.OrderId, strategy, principal.OrganizationId);

        try
        {
            var preview = await _inner.PreviewAsync(request, principal, ct).ConfigureAwait(false);

            RecordCompleted(span, strategy, PreviewMode, started, preview.OrderTypeId, new TargetSelectionCounts(
                preview.Matched, preview.Eligible, preview.AlreadyInOrder, preview.TruncatedByLimit, preview.WarningCount, preview.BlockedCount));
            return preview;
        }
        catch (Exception ex)
        {
            RecordFailed(span, strategy, PreviewMode, started, ex);
            throw;
        }
    }

    public async Task<TargetSelectionResult> ApplyAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        var strategy = StrategyTag(request.StrategyId);
        var started = _timeProvider.GetTimestamp();
        using var span = _tracer.StartTargetSelectionActivity("Apply", request.OrderId, strategy, principal.OrganizationId);

        try
        {
            var result = await _inner.ApplyAsync(request, principal, ct).ConfigureAwait(false);

            RecordCompleted(span, strategy, ApplyMode, started, result.OrderTypeId, new TargetSelectionCounts(
                result.Matched, result.Eligible, result.AlreadyInOrder, result.TruncatedByLimit, result.WarningCount, result.BlockedCount));

            // Conflicts are counted where targets are actually written, not on every preview.
            _metrics.RecordTargetConflicts(result.WarningCount, "warning", result.OrderTypeId);
            _metrics.RecordTargetConflicts(result.BlockedCount, "blocked", result.OrderTypeId);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailed(span, strategy, ApplyMode, started, ex);
            throw;
        }
    }

    private void RecordCompleted(
        Activity? span, string strategy, string mode, long started, string orderTypeId, TargetSelectionCounts counts)
    {
        GeoAssetsActivitySource.SetTargetSelectionCounts(span, orderTypeId, counts);

        _metrics.RecordTargetSelectionDuration(_timeProvider.GetElapsedTime(started).TotalMilliseconds, strategy, mode, OutcomeOk);
        _metrics.RecordTargetSelectionMatched(counts.Matched, strategy);
        if (counts.Truncated > 0)
            _metrics.RecordTargetSelectionTruncated(strategy);
    }

    private void RecordFailed(Activity? span, string strategy, string mode, long started, Exception ex)
    {
        var outcome = Outcome(ex);
        span?.AddTag("selection.outcome", outcome);

        // The exception type is enough: messages of parameter errors can echo client input.
        span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

        _metrics.RecordTargetSelectionDuration(_timeProvider.GetElapsedTime(started).TotalMilliseconds, strategy, mode, outcome);
        if (ex is SelectionLimitExceededException)
            _metrics.RecordTargetSelectionTruncated(strategy);
    }

    /// <summary>
    /// Sorts a failure into: the caller gave up; the selection was past the host ceiling; the
    /// request was refused (bad input, unknown order or strategy, denied, limit, conflict); or
    /// something broke.
    /// </summary>
    internal static string Outcome(Exception ex) => ex switch
    {
        OperationCanceledException => OutcomeCancelled,
        SelectionTooLargeException => OutcomeTooLarge,
        ArgumentException or KeyNotFoundException or InvalidCastException or FormatException
            or OrderActionDeniedException or GeoAssets.Core.Services.InvalidSelectionFenceException
            or SelectionLimitExceededException
            or Orders.InvalidServiceOrderTransitionException or Orders.ServiceOrderConcurrencyException => OutcomeRejected,
        InvalidOperationException => OutcomeRejected,
        _ => OutcomeError,
    };

    private string StrategyTag(string? strategyId) =>
        _strategies.GetAvailableStrategies()
            .Select(m => m.StrategyId)
            .FirstOrDefault(id => id.Equals(strategyId, StringComparison.OrdinalIgnoreCase))
        ?? UnknownStrategy;
}
