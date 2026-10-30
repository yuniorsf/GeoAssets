namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Integration command/event requesting an atomic batch of asset catalog mutations on a target catalog shard.
/// </summary>
public sealed record AssetMutationBatchRequestedEvent(
    Guid BatchId,
    string CatalogInstanceId,
    Guid OrganizationId,
    string RequestedByService,
    string PerformedByUserId,
    string? SourceOrderId,
    string? SourceOrderTypeId,
    string? ScopeId,
    IReadOnlyList<AssetFeatureMutationItem> Mutations,
    DateTimeOffset RequestedAtUtc,
    string? CorrelationId = null)
{
    public static AssetMutationBatchRequestedEvent Create(
        string catalogInstanceId,
        Guid organizationId,
        string requestedByService,
        string performedByUserId,
        IReadOnlyList<AssetFeatureMutationItem> mutations,
        TimeProvider timeProvider,
        Guid? batchId = null,
        string? sourceOrderId = null,
        string? sourceOrderTypeId = null,
        string? scopeId = null,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedByService);
        ArgumentException.ThrowIfNullOrWhiteSpace(performedByUserId);
        ArgumentNullException.ThrowIfNull(mutations);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var resolvedBatchId = batchId ?? Guid.NewGuid();
        if (resolvedBatchId == Guid.Empty)
        {
            throw new ArgumentException("Batch ID cannot be Guid.Empty.", nameof(batchId));
        }

        return new AssetMutationBatchRequestedEvent(
            BatchId: resolvedBatchId,
            CatalogInstanceId: catalogInstanceId,
            OrganizationId: organizationId,
            RequestedByService: requestedByService,
            PerformedByUserId: performedByUserId,
            SourceOrderId: sourceOrderId,
            SourceOrderTypeId: sourceOrderTypeId,
            ScopeId: scopeId,
            Mutations: mutations,
            RequestedAtUtc: timeProvider.GetUtcNow(),
            CorrelationId: correlationId);
    }
}
