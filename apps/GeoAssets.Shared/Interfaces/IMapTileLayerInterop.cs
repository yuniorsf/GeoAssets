namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// The kernel's tile layers on a map (XD01-436) — <c>/tiles/style.json</c> (XD01-434), which
/// <see cref="Services.Rendering.HybridLodMapLayerRenderer"/> draws a kernel provider entry from. Kept apart from
/// <see cref="IMapInterop"/>, as <see cref="IMapSelectionInterop"/> is, so its doubles stay untouched.
/// <list type="bullet">
///   <item><see cref="Services.MapRenderMode.MapLibre"/>: the style's vector and raster layers, which
///   <c>geoassets.maplibre.js</c> loads when the map registers its handlers.</item>
///   <item>The Leaflet modes, with <see cref="Services.MapInteropOptions.LeafletTileSplit"/>: one XYZ layer of the style's
///   raster tiles over the zooms of its raster tier (the interim split), each tile fetched with the bearer token
///   <c>MapContainer.GetTileAccessFromJs</c> answers and shown as an object URL — the token is never in a URL.</item>
/// </list>
/// </summary>
public interface IMapTileLayerInterop
{
    /// <summary>
    /// Shows the tile layers (reading the style first when the map has none yet) and answers what the style publishes;
    /// <c>null</c> when it can't be read (unreachable, refused, not a style), in which case nothing is drawn from it.
    /// Idempotent. Starts following the style's <c>{v}</c> (<see cref="TileStyleRefreshed"/>).
    /// </summary>
    Task<MapTileStyleInfo?> ShowTileLayersAsync(string divId);

    /// <summary>Takes the tile layers off the map until <see cref="ShowTileLayersAsync"/> shows them again.</summary>
    Task HideTileLayersAsync(string divId);

    /// <summary>
    /// Hides the tile copy of every feature in <paramref name="featureIds"/> (replacing the set hidden before): their
    /// GeoJSON overlay, or nothing for a deleted one, is drawn instead. MapLibre filters the vector tile layers by the
    /// tiles' promoted <c>id</c>, so a hidden copy is neither drawn nor hit by a click, hover or snap; a raster tile can't
    /// hide one feature, so the Leaflet split ignores this.
    /// </summary>
    Task SetHiddenTileFeaturesAsync(string divId, IReadOnlyCollection<string> featureIds);

    /// <summary>Raised after each re-read of a shown map's tile style (after this client's writes and periodically).</summary>
    event EventHandler<MapTileStyleRefreshedEventArgs>? TileStyleRefreshed;
}
