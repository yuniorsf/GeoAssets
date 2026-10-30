namespace GeoAssets.Core.Messaging.Events;

/// <summary>
/// Integration event notifying downstream subscribers that an individual feature in a catalog shard changed.
/// </summary>
public sealed record FeatureCatalogMutatedEvent(
    string CatalogInstanceId,
    Guid OrganizationId,
    string? ScopeId,
    string FeatureId,
    AssetMutationKind MutationKind,
    string Name,
    string AssetTypeId,
    string LayerId,
    AssetGeometrySnapshot? Geometry,
    IReadOnlyDictionary<string, string> CustomAttributes,
    Rfc7946BoundingBox? AffectedBounds,
    byte[] RowVersion,
    DateTimeOffset MutatedAtUtc,
    string? CorrelationId = null)
{
    public static FeatureCatalogMutatedEvent Create(
        string catalogInstanceId,
        Guid organizationId,
        string featureId,
        AssetMutationKind mutationKind,
        string name,
        string assetTypeId,
        string layerId,
        IReadOnlyDictionary<string, string> customAttributes,
        byte[] rowVersion,
        TimeProvider timeProvider,
        string? scopeId = null,
        AssetGeometrySnapshot? geometry = null,
        Rfc7946BoundingBox? affectedBounds = null,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetTypeId);
        ArgumentNullException.ThrowIfNull(layerId);
        ArgumentNullException.ThrowIfNull(customAttributes);
        ArgumentNullException.ThrowIfNull(rowVersion);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new FeatureCatalogMutatedEvent(
            CatalogInstanceId: catalogInstanceId,
            OrganizationId: organizationId,
            ScopeId: scopeId,
            FeatureId: featureId,
            MutationKind: mutationKind,
            Name: name,
            AssetTypeId: assetTypeId,
            LayerId: layerId,
            Geometry: geometry,
            CustomAttributes: customAttributes,
            AffectedBounds: affectedBounds,
            RowVersion: rowVersion,
            MutatedAtUtc: timeProvider.GetUtcNow(),
            CorrelationId: correlationId);
    }
}
