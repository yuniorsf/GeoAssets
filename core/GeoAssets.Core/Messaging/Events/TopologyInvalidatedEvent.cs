namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Integration event signaling that cached topology neighborhoods for the listed features/edges are stale.
/// </summary>
public sealed record TopologyInvalidatedEvent(
    string CatalogInstanceId,
    Guid OrganizationId,
    string? ScopeId,
    IReadOnlyList<string> AffectedFeatureIds,
    IReadOnlyList<string> AffectedEdgeKinds,
    Rfc7946BoundingBox? AffectedBounds,
    string Reason,
    DateTimeOffset InvalidatedAtUtc,
    string? CorrelationId = null)
{
    public static TopologyInvalidatedEvent Create(
        string catalogInstanceId,
        Guid organizationId,
        IReadOnlyList<string> affectedFeatureIds,
        IReadOnlyList<string> affectedEdgeKinds,
        string reason,
        TimeProvider timeProvider,
        string? scopeId = null,
        Rfc7946BoundingBox? affectedBounds = null,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogInstanceId);
        ArgumentNullException.ThrowIfNull(affectedFeatureIds);
        ArgumentNullException.ThrowIfNull(affectedEdgeKinds);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new TopologyInvalidatedEvent(
            CatalogInstanceId: catalogInstanceId,
            OrganizationId: organizationId,
            ScopeId: scopeId,
            AffectedFeatureIds: affectedFeatureIds,
            AffectedEdgeKinds: affectedEdgeKinds,
            AffectedBounds: affectedBounds,
            Reason: reason,
            InvalidatedAtUtc: timeProvider.GetUtcNow(),
            CorrelationId: correlationId);
    }
}
