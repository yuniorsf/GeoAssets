namespace GeoAssets.Core.Rendering;

/// <summary>
/// Engine-agnostic read port for vector tiles (ADR-002 D1/D3/D6, XD01-431) — implemented by the PostGIS
/// <c>ST_AsMVT</c> source, a PMTiles archive, an offline pack; a shared contract in GeoAssets.Core, so a plugin's
/// implementation and the host's endpoint meet in the default load context. A source answers in the caller's ambient
/// tenant scope (as <see cref="IAssetRenderQuery"/> does — the caller's organization's features plus the shared ones);
/// whoever caches its tiles keys them by that scope (<see cref="TileCacheKey"/>).
/// </summary>
public interface IVectorTileSource
{
    /// <summary>
    /// The Mapbox Vector Tile at <see cref="TileRequest.Address"/> with one MVT layer per
    /// <see cref="TileLayerSet.LayerIds"/> entry that has features there, in
    /// <see cref="TileFormats.MapboxVectorTileContentType"/>; <see cref="TilePayload.IsEmpty"/> when none has. Never
    /// <c>null</c>.
    /// </summary>
    Task<TilePayload> GetVectorTileAsync(TileRequest request, CancellationToken ct);
}
