using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// An <see cref="IAssetSpatialQuery"/> over a fixed list of features that answers the attribute lookup the
/// <c>identifier-list</c> strategy runs (the reserved key <c>id</c> matches the feature id), honouring the filter's
/// organization and asset types — enough to drive a selection without a spatial store. Area queries are not supported.
/// </summary>
public sealed class CatalogSpatialQuery(IReadOnlyCollection<GeoFeature> features) : IAssetSpatialQuery
{
    public Task<IReadOnlyList<GeoFeature>> FindCoveredByAsync(GeoPolygon area, AssetSpatialFilter filter, CancellationToken ct) =>
        throw new NotSupportedException("CatalogSpatialQuery answers identifier lookups only.");

    public Task<IReadOnlyList<GeoFeature>> FindByAttributeValuesAsync(
        string attributeKey, IReadOnlyCollection<string> values, AssetSpatialFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(filter);
        ct.ThrowIfCancellationRequested();

        var wanted = new HashSet<string>(values, StringComparer.Ordinal);
        IReadOnlyList<GeoFeature> matches =
        [
            .. features
                .Where(f => filter.OrganizationId is null || f.Properties.OrganizationId == filter.OrganizationId)
                .Where(f => filter.AssetTypeIds is null || filter.AssetTypeIds.Contains(f.Properties.AssetTypeId, StringComparer.Ordinal))
                .Where(f => string.Equals(attributeKey, AssetSpatialQueryKeys.FeatureId, StringComparison.Ordinal)
                    ? wanted.Contains(f.Id)
                    : f.Properties.CustomAttributes.TryGetValue(attributeKey, out var value) && wanted.Contains(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty))
                .Take(filter.MaxResults),
        ];
        return Task.FromResult(matches);
    }
}
