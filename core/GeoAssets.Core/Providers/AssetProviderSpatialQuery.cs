using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Providers;

/// <summary>
/// Default <see cref="IAssetSpatialQuery"/> adapter over any <see cref="IAssetProvider"/>: it scans
/// <see cref="IAssetProvider.GetAll"/> in memory. Correct for every provider, and the right choice
/// for client-side or single-tenant stores; a server host backed by a real database should register
/// a pushdown implementation instead.
///
/// <para>The scan itself is synchronous, so cancellation is observed before it starts and again
/// after it finishes — a cancelled call never returns a result.</para>
///
/// <para>With <see cref="AssetSpatialFilter.OrganizationId"/> unset this adapter returns whatever
/// the wrapped provider exposes; it has no tenant context of its own.</para>
/// </summary>
public sealed class AssetProviderSpatialQuery : IAssetSpatialQuery
{
    private readonly IAssetProvider _provider;

    public AssetProviderSpatialQuery(IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    public Task<IReadOnlyList<GeoFeature>> FindCoveredByAsync(
        GeoPolygon area, AssetSpatialFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(filter.MaxResults);

        return Scan(f => f.Geometry is { } geometry && geometry.CoveredBy(area), filter, ct);
    }

    public Task<IReadOnlyList<GeoFeature>> FindByAttributeValuesAsync(
        string attributeKey, IReadOnlyCollection<string> values, AssetSpatialFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attributeKey);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(filter.MaxResults);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeKey);

        if (ct.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<GeoFeature>>(ct);

        if (values.Count == 0)
            return Task.FromResult<IReadOnlyList<GeoFeature>>([]);

        var wanted = new HashSet<string>(values, StringComparer.Ordinal);

        return string.Equals(attributeKey, AssetSpatialQueryKeys.FeatureId, StringComparison.Ordinal)
            ? Scan(f => wanted.Contains(f.Id), filter, ct)
            : Scan(
                f => f.Properties.CustomAttributes.TryGetValue(attributeKey, out var value) && wanted.Contains(value),
                filter,
                ct);
    }

    private Task<IReadOnlyList<GeoFeature>> Scan(
        Func<GeoFeature, bool> matches, AssetSpatialFilter filter, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<GeoFeature>>(ct);

        var typeIds = filter.AssetTypeIds is { Count: > 0 } ids
            ? new HashSet<string>(ids, StringComparer.Ordinal)
            : null;
        var organizationId = filter.OrganizationId;

        IReadOnlyList<GeoFeature> result =
        [
            .. _provider.GetAll()
                .Where(f => organizationId is null || f.Properties.OrganizationId == organizationId.Value)
                .Where(f => typeIds is null || typeIds.Contains(f.Properties.AssetTypeId))
                .Where(matches)
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .Take(filter.MaxResults),
        ];

        return ct.IsCancellationRequested
            ? Task.FromCanceled<IReadOnlyList<GeoFeature>>(ct)
            : Task.FromResult(result);
    }
}
