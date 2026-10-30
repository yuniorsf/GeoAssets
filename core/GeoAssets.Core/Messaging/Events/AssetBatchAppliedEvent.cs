namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Integration event emitted after a requested batch of asset mutations has been committed to a catalog shard.
/// </summary>
public sealed record AssetBatchAppliedEvent(
    Guid BatchId,
    string CatalogInstanceId,
    Guid OrganizationId,
    string RequestedByService,
    string AppliedByUserId,
    string? SourceOrderId,
    string? SourceOrderTypeId,
    string? ScopeId,
    IReadOnlyList<AssetBatchAppliedItem> AppliedItems,
    Rfc7946BoundingBox? AffectedBounds,
    DateTimeOffset AppliedAtUtc,
    string? CorrelationId = null)
{
    public static AssetBatchAppliedEvent Create(
        Guid batchId,
        string catalogInstanceId,
        Guid organizationId,
        string requestedByService,
        string appliedByUserId,
        IReadOnlyList<AssetBatchAppliedItem> appliedItems,
        TimeProvider timeProvider,
        string? sourceOrderId = null,
        string? sourceOrderTypeId = null,
        string? scopeId = null,
        Rfc7946BoundingBox? affectedBounds = null,
        string? correlationId = null)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("Batch ID cannot be Guid.Empty.", nameof(batchId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(catalogInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedByService);
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedByUserId);
        ArgumentNullException.ThrowIfNull(appliedItems);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new AssetBatchAppliedEvent(
            BatchId: batchId,
            CatalogInstanceId: catalogInstanceId,
            OrganizationId: organizationId,
            RequestedByService: requestedByService,
            AppliedByUserId: appliedByUserId,
            SourceOrderId: sourceOrderId,
            SourceOrderTypeId: sourceOrderTypeId,
            ScopeId: scopeId,
            AppliedItems: appliedItems,
            AffectedBounds: affectedBounds,
            AppliedAtUtc: timeProvider.GetUtcNow(),
            CorrelationId: correlationId);
    }
}
