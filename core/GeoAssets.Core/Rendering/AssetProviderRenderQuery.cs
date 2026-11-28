using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// The generic <see cref="IAssetRenderQuery"/> (XD01-372) over an <see cref="IAssetProvider"/> — the request's catalog,
/// already scoped to the caller's organization: <see cref="IAssetProvider.GetInBoundsAsync"/> (a pushdown where the
/// provider has one) styled by <see cref="RenderStyle"/>, narrowed by <see cref="RenderQueryOptions"/> — asset type, the
/// zoom's type ranges (the catalog's asset types) and the first <see cref="RenderQueryOptions.MaxCount"/> by ordinal id.
/// </summary>
public sealed class AssetProviderRenderQuery(IAssetProvider catalog) : IAssetRenderQuery
{
    public async Task<IReadOnlyList<RenderableFeature>> GetRenderableAsync(BoundingBox bbox, RenderQueryOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();
        if (options.MaxCount <= 0) return [];

        var features = await catalog.GetInBoundsAsync(bbox.MinLon, bbox.MinLat, bbox.MaxLon, bbox.MaxLat, ct).ConfigureAwait(false);
        var hidden = options.Zoom is { } zoom ? AssetTypeZoomRange.TypeIdsHiddenAt(catalog.GetAssetTypes(), zoom) : null;
        var style = RenderStyle.Of(catalog);
        return
        [
            .. features
                .Where(f => f.Geometry is not null)
                .Where(f => options.AssetTypeId is null
                    || string.Equals(f.Properties.AssetTypeId, options.AssetTypeId, StringComparison.Ordinal))
                .Where(f => hidden is null || !hidden.Contains(f.Properties.AssetTypeId))
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .Take(options.MaxCount)
                .Select(f => new RenderableFeature(f.Id, f.Properties.Name, f.Properties.AssetTypeId, f.Geometry!, style.ColorOf(f))),
        ];
    }

    public async Task<IReadOnlyList<FeatureInfo>> GetFeatureInfoAsync(BoundingBox bbox, int maxCount, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (maxCount <= 0) return [];

        var features = await catalog.GetInBoundsAsync(bbox.MinLon, bbox.MinLat, bbox.MaxLon, bbox.MaxLat, ct).ConfigureAwait(false);
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
