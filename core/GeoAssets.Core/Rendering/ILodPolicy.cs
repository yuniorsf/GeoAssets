namespace GeoAssets.Core.Rendering;

/// <summary>
/// The level-of-detail policy (ADR-002 D6, XD01-431): which <see cref="TileRenderTier"/> a layer is drawn as at a zoom.
/// Pure — no I/O, same answer for the same arguments — so the tile endpoints, <c>/tiles/style.json</c>'s
/// <c>minzoom</c>/<c>maxzoom</c> and the client renderer agree on the thresholds. Its thresholds are valid by
/// construction (<see cref="LodPolicy"/>); a density-calibrated policy is another implementation of the same port.
/// </summary>
public interface ILodPolicy
{
    /// <summary>The tier <paramref name="layerId"/> is drawn as at <paramref name="zoom"/> — the policy's default thresholds' answer for a layer it has none for.</summary>
    /// <exception cref="ArgumentException"><paramref name="layerId"/> is <c>null</c> or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoom"/> is outside [<see cref="TileAddress.MinZoom"/>, <see cref="TileAddress.MaxZoom"/>].</exception>
    TileRenderTier Decide(string layerId, int zoom);

    /// <summary>The thresholds <see cref="Decide"/> applies to <paramref name="layerId"/> (the default ones for a layer the policy has none for) — what a style document publishes as zoom bounds.</summary>
    /// <exception cref="ArgumentException"><paramref name="layerId"/> is <c>null</c> or blank.</exception>
    LodThresholds GetThresholds(string layerId);
}
