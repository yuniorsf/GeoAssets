namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Integration event emitted when the kernel refuses an <see cref="AssetMutationBatchRequestedEvent"/> for a business
/// reason (XD01-383, <see cref="IntegrationEventTypes.AssetMutationBatchRejectedV1"/>): nothing of the batch was applied,
/// and redelivering it would fail the same way. The requester compensates — the approval saga reverts what it staged for
/// the batch. Transport or database failures are never reported this way; the batch is redelivered instead.
/// </summary>
/// <param name="BatchId">The rejected batch.</param>
/// <param name="CatalogInstanceId">The catalog instance the batch was addressed to.</param>
/// <param name="OrganizationId">The batch's organization.</param>
/// <param name="RequestedByService">The service that requested the batch.</param>
/// <param name="SourceOrderId">The order the batch came from, when it named one.</param>
/// <param name="SourceOrderTypeId">That order's type, when it named one.</param>
/// <param name="ScopeId">The batch's scope, when it named one.</param>
/// <param name="ReasonCode">A stable, low-cardinality code from <see cref="AssetMutationBatchRejectionCodes"/>.</param>
/// <param name="Reason">A human-readable reason.</param>
/// <param name="ConflictingFeatureIds">The features the batch could not be applied to, when the kernel knows them; may be empty.</param>
/// <param name="RejectedAtUtc">When the kernel refused the batch.</param>
/// <param name="CorrelationId">The batch's correlation id.</param>
/// <param name="SourceVerticalId">The vertical that requested the batch, so the reply routes back.</param>
public sealed record AssetMutationBatchRejectedEvent(
    Guid BatchId,
    string CatalogInstanceId,
    Guid OrganizationId,
    string RequestedByService,
    string? SourceOrderId,
    string? SourceOrderTypeId,
    string? ScopeId,
    string ReasonCode,
    string Reason,
    IReadOnlyList<string> ConflictingFeatureIds,
    DateTimeOffset RejectedAtUtc,
    string? CorrelationId = null,
    string? SourceVerticalId = null)
{
    /// <summary>The rejection of <paramref name="batch"/>, at <paramref name="timeProvider"/>'s now, routed back the way the batch came.</summary>
    public static AssetMutationBatchRejectedEvent For(
        AssetMutationBatchRequestedEvent batch,
        string reasonCode,
        string reason,
        IReadOnlyList<string> conflictingFeatureIds,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(conflictingFeatureIds);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new AssetMutationBatchRejectedEvent(
            BatchId: batch.BatchId,
            CatalogInstanceId: batch.CatalogInstanceId,
            OrganizationId: batch.OrganizationId,
            RequestedByService: batch.RequestedByService,
            SourceOrderId: batch.SourceOrderId,
            SourceOrderTypeId: batch.SourceOrderTypeId,
            ScopeId: batch.ScopeId,
            ReasonCode: reasonCode,
            Reason: reason,
            ConflictingFeatureIds: conflictingFeatureIds,
            RejectedAtUtc: timeProvider.GetUtcNow(),
            CorrelationId: batch.CorrelationId,
            SourceVerticalId: batch.SourceVerticalId);
    }
}
