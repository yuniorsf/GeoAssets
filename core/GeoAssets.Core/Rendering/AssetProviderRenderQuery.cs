using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// The generic <see cref="IAssetRenderQuery"/> (XD01-372) over an <see cref="IAssetProvider"/> — the request's catalog,
/// already scoped to the caller's organization: <see cref="IAssetProvider.GetInBoundsAsync"/> (a pushdown where the
/// provider has one) styled by <see cref="RenderStyle"/>.
/// </summary>
public sealed class AssetProviderRenderQuery(IAssetProvider catalog) : IAssetRenderQuery
{
    public async Task<IReadOnlyList<RenderableFeature>> GetRenderableAsync(BoundingBox bbox, RenderQueryOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();

        var features = await catalog.GetInBoundsAsync(bbox.MinLon, bbox.MinLat, bbox.MaxLon, bbox.MaxLat).ConfigureAwait(false);
        var style = RenderStyle.Of(catalog);
        return
        [
            .. features
                .Where(f => f.Geometry is not null)
                .Where(f => options.AssetTypeId is null
                    || string.Equals(f.Properties.AssetTypeId, options.AssetTypeId, StringComparison.Ordinal))
                .Select(f => new RenderableFeature(f.Id, f.Properties.Name, f.Properties.AssetTypeId, f.Geometry!, style.ColorOf(f))),
        ];
    }

    public async Task<IReadOnlyList<FeatureInfo>> GetFeatureInfoAsync(BoundingBox bbox, int maxCount, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (maxCount <= 0) return [];

        var features = await catalog.GetInBoundsAsync(bbox.MinLon, bbox.MinLat, bbox.MaxLon, bbox.MaxLat).ConfigureAwait(false);
        return
        [
            .. features
                .Where(f => f.Geometry is not null)
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .Take(maxCount)
                .Select(f => new FeatureInfo(f.Id, f.Properties.Name, f.Properties.AssetTypeId, f.Properties.Description)),
        ];
    }
}
