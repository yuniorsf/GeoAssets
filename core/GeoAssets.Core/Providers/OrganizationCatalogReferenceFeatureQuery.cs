using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Core.Providers;

/// <summary>
/// The generic <see cref="IReferenceFeatureQuery"/> (XD01-330) over <see cref="IOrganizationCatalogFactory"/>: the named
/// organization's catalog (<see cref="IAssetProvider.GetInBoundsAsync"/>), kept to the features that organization owns —
/// its catalog also holds the shared ones — and projected to <see cref="ReferenceFeature"/>, the asset type's name taken
/// from that same catalog. What a host answers with when no provider registers a pushdown.
/// </summary>
public sealed class OrganizationCatalogReferenceFeatureQuery(IOrganizationCatalogFactory catalogs) : IReferenceFeatureQuery
{
    public async Task<IReadOnlyList<ReferenceFeature>> GetInBoundsAsync(
        Guid organizationId, BoundingBox bbox, ReferenceFeatureFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(filter.MaxResults);
        ct.ThrowIfCancellationRequested();

        var catalog = await catalogs.GetForOrganizationAsync(organizationId, ct).ConfigureAwait(false);
        var features = await catalog.GetInBoundsAsync(bbox.MinLon, bbox.MinLat, bbox.MaxLon, bbox.MaxLat).ConfigureAwait(false);
        var scopes = filter.ScopeIds is null ? null : new HashSet<string>(filter.ScopeIds, StringComparer.Ordinal);
        var typeNames = catalog.GetAssetTypes()
            .Where(t => t.OrganizationId == organizationId || t.OrganizationId == Guid.Empty)
            .ToDictionary(t => t.Id.ToString(), t => t.Name, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. features
                .Where(f => f.Geometry is not null && f.Properties.OrganizationId == organizationId)
                .Where(f => string.IsNullOrEmpty(filter.AssetTypeId)
                    || string.Equals(f.Properties.AssetTypeId, filter.AssetTypeId, StringComparison.Ordinal))
                .Where(f => scopes is null
                    || (((IScopeableResource)f).ScopeId is { } scopeId && scopes.Contains(scopeId)))
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .Take(filter.MaxResults)
                .Select(f => new ReferenceFeature(
                    f.Id,
                    f.Geometry!,
                    f.Properties.Name,
                    f.Properties.AssetTypeId,
                    typeNames.GetValueOrDefault(f.Properties.AssetTypeId, string.Empty))),
        ];
    }
}
