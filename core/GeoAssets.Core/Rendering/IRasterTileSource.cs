namespace GeoAssets.Core.Rendering;

/// <summary>
/// Engine-agnostic read port for raster tiles (ADR-002 D1/D3/D6, XD01-431) — implemented by the SkiaSharp renderer, a
/// COG reader; a shared contract in GeoAssets.Core, so a plugin's implementation and the host's endpoint meet in the
/// default load context. A source answers in the caller's ambient tenant scope (as <see cref="IAssetRenderQuery"/>
/// does); whoever caches its tiles keys them by that scope (<see cref="TileCacheKey"/>).
/// </summary>
public interface IRasterTileSource
{
    /// <summary>
    /// The 256 × 256 pixel image of <see cref="TileRequest.Address"/>'s EPSG:3857 envelope with the
    /// <see cref="TileLayerSet.LayerIds"/> drawn bottom first, in an image media type (e.g.
    /// <see cref="TileFormats.PngContentType"/>); <see cref="TilePayload.IsEmpty"/> when nothing is drawn there. Never
    /// <c>null</c>.
    /// </summary>
    Task<TilePayload> GetRasterTileAsync(TileRequest request, CancellationToken ct);
}
