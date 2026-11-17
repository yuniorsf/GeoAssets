using System.Diagnostics;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;

namespace GeoAssets.Server;

/// <summary>
/// Span and metrics around <see cref="IAssetMutationBatchApplier.ApplyAsync"/> (XD01-337). The
/// span is <c>Catalog.MutationBatchApply</c>, tagged with the batch, its catalog instance, its
/// organization and how many mutations it carries, and — when applied — how many items were
/// applied. Every attempt is counted and timed by outcome; no metric carries a batch,
/// organization or feature id. A failure records the exception's type only and is rethrown, so
/// whoever delivered the batch retries it.
/// </summary>
public sealed class ObservableAssetMutationBatchApplier : IAssetMutationBatchApplier
{
    public const string OutcomeOk = "ok";
    public const string OutcomeRejected = "rejected";
    public const string OutcomeCancelled = "cancelled";
    public const string OutcomeError = "error";

    private readonly IAssetMutationBatchApplier _inner;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly GeoAssetsMeter _metrics;
    private readonly TimeProvider _timeProvider;

    public ObservableAssetMutationBatchApplier(
        IAssetMutationBatchApplier inner, GeoAssetsActivitySource tracer, GeoAssetsMeter metrics, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _inner = inner;
        _tracer = tracer;
        _metrics = metrics;
        _timeProvider = timeProvider;
    }

    public async Task<AssetBatchAppliedEvent> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var started = _timeProvider.GetTimestamp();
        using var span = _tracer.StartAssetMutationBatchActivity(batch.BatchId, batch.CatalogInstanceId, batch.OrganizationId, batch.Mutations.Count);

        try
        {
            var applied = await _inner.ApplyAsync(batch, ct).ConfigureAwait(false);

            span?.AddTag("catalog.batch.applied_items", applied.AppliedItems.Count);
            _metrics.RecordAssetMutationBatch(_timeProvider.GetElapsedTime(started).TotalMilliseconds, OutcomeOk);
            return applied;
        }
        catch (Exception ex)
        {
            var outcome = Outcome(ex);
            span?.AddTag("catalog.batch.outcome", outcome);
            span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            _metrics.RecordAssetMutationBatch(_timeProvider.GetElapsedTime(started).TotalMilliseconds, outcome);
            throw;
        }
    }

    /// <summary>
    /// <see cref="OutcomeRejected"/> for what retrying cannot fix by itself — a bad argument, a
    /// feature that is not in the organization's catalog, an invalid or unsupported mutation.
    /// </summary>
    internal static string Outcome(Exception ex) => ex switch
    {
        OperationCanceledException => OutcomeCancelled,
        ArgumentException or KeyNotFoundException or NotSupportedException
            or Core.Services.GeoFeatureAttributeValidationException
            or Core.Services.GeoFeatureGeometryMismatchException => OutcomeRejected,
        _ => OutcomeError,
    };
}
