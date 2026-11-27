namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// What the kernel's tile style (<c>/tiles/style.json</c>, XD01-434) publishes for one map, as
/// <see cref="IMapTileLayerInterop.ShowTileLayersAsync"/> answers it (XD01-436), camelCase over JS interop.
/// </summary>
/// <param name="Version">The tiles' <c>{v}</c> the map draws (<c>metadata."geoassets:version"</c>); null when unpublished.</param>
/// <param name="RasterMinZoom">The first tile zoom of the style's raster tier (its raster layer's <c>minzoom</c>); null when
/// the style has no raster tier — the default LOD policy, vector at every zoom.</param>
/// <param name="RasterMaxZoom">The last tile zoom of the raster tier (the raster layer's exclusive <c>maxzoom</c> − 1),
/// inclusive; null when the style has no raster tier.</param>
/// <param name="Zoom">The map's zoom when it answered, in Leaflet's 256 px scale (the scale of
/// <see cref="Core.Rendering.ViewportQuery.Zoom"/>).</param>
public sealed record MapTileStyleInfo(string? Version, int? RasterMinZoom, int? RasterMaxZoom, int Zoom)
{
    /// <summary>Whether the style publishes a raster tier at all.</summary>
    public bool HasRasterTier => RasterMinZoom is not null && RasterMaxZoom is not null;

    /// <summary>
    /// Whether a Leaflet map at <paramref name="zoom"/> (256 px tiles, so its zoom is the tile zoom the style's tiers are
    /// given in) draws the raster tiles rather than discrete features — the interim Leaflet split (XD01-436). Always false
    /// without a raster tier, which keeps the vector features at every zoom, as before.
    /// </summary>
    public bool IsRasterZoom(int zoom) =>
        RasterMinZoom is { } min && RasterMaxZoom is { } max && zoom >= min && zoom <= max;
}
