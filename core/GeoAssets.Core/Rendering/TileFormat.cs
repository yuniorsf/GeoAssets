namespace GeoAssets.Core.Rendering;

/// <summary>The encoding of a served tile (XD01-431) — part of its URL (<c>.mvt</c>/<c>.png</c>) and of its <see cref="TileCacheKey"/>.</summary>
public enum TileFormat
{
    /// <summary>A Mapbox Vector Tile (<see cref="TileFormats.MapboxVectorTileContentType"/>), from an <see cref="IVectorTileSource"/>.</summary>
    Mvt = 0,

    /// <summary>A PNG image (<see cref="TileFormats.PngContentType"/>), from an <see cref="IRasterTileSource"/>.</summary>
    Png = 1,
}
