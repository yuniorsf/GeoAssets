namespace GeoAssets.Core.Rendering;

/// <summary>
/// Read port for map rendering (XD01-372, ADR-001 Phase 4): the features a raster map service (WMS) draws and identifies,
/// through the provider abstraction and the request's tenant scoping — the caller's organization's features plus the
/// shared ones. <see cref="AssetProviderRenderQuery"/> answers it over any <see cref="Interfaces.IAssetProvider"/>; a
/// provider may register a pushdown that projects only what drawing needs.
/// </summary>
public interface IAssetRenderQuery
{
    /// <summary>The features with a geometry intersecting <paramref name="bbox"/>, colored by <see cref="RenderStyle"/>.</summary>
    Task<IReadOnlyList<RenderableFeature>> GetRenderableAsync(BoundingBox bbox, RenderQueryOptions options, CancellationToken ct);

    /// <summary>At most <paramref name="maxCount"/> features with a geometry intersecting <paramref name="bbox"/>, by ordinal id.</summary>
    Task<IReadOnlyList<FeatureInfo>> GetFeatureInfoAsync(BoundingBox bbox, int maxCount, CancellationToken ct);
}
