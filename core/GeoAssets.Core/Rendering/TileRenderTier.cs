namespace GeoAssets.Core.Rendering;

/// <summary>
/// How a layer is drawn at a zoom (ADR-002 D6, XD01-431) — the 3-tier hybrid LOD, coarsest first, as
/// <see cref="ILodPolicy.Decide"/> answers it; the editable GeoJSON overlay is drawn on top of <see cref="Vector"/> by
/// the client and is not a tier.
/// </summary>
public enum TileRenderTier
{
    /// <summary>Not drawn at this zoom (outside the layer's <see cref="LodThresholds.MinZoom"/>/<see cref="LodThresholds.MaxZoom"/>).</summary>
    Hidden = 0,

    /// <summary>Aggregated: features merged into grid/cluster summaries (vector tiles of aggregates).</summary>
    Aggregate = 1,

    /// <summary>Rendered server-side into raster image tiles (<see cref="IRasterTileSource"/>).</summary>
    Raster = 2,

    /// <summary>Discrete features in vector tiles (<see cref="IVectorTileSource"/>).</summary>
    Vector = 3,
}
