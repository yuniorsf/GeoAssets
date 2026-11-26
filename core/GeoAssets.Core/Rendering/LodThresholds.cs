using System.Runtime.InteropServices;
using GeoAssets.Core.Models;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// One layer's zoom thresholds for the 3-tier hybrid LOD (ADR-002 D6, XD01-431): below <see cref="RasterFromZoom"/> it
/// is <see cref="TileRenderTier.Aggregate"/>, from <see cref="RasterFromZoom"/> <see cref="TileRenderTier.Raster"/>,
/// from <see cref="VectorFromZoom"/> <see cref="TileRenderTier.Vector"/> — and <see cref="TileRenderTier.Hidden"/>
/// outside [<see cref="MinZoom"/>, <see cref="MaxZoom"/>], the asset type's zoom range with
/// <see cref="AssetTypeZoomRange"/>'s rules (bounds inclusive, <c>null</c> = open). <c>RasterFromZoom = 0</c> skips
/// aggregation; <c>VectorFromZoom = 0</c> is vector at every zoom (<see cref="VectorOnly"/>, also <c>default</c>).
/// Not validated on construction: <see cref="FindDefect"/> / <see cref="EnsureValid"/> say whether it is valid, and
/// <see cref="LodPolicy"/> refuses an invalid one (fail-fast at startup, ADR-002 D7).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LodThresholds(int RasterFromZoom, int VectorFromZoom, int? MinZoom = null, int? MaxZoom = null)
{
    /// <summary>Discrete vector features at every zoom, never hidden.</summary>
    public static LodThresholds VectorOnly => default;

    /// <summary>The first rule the thresholds break, or <c>null</c> when they are valid.</summary>
    public LodThresholdsDefect? FindDefect()
    {
        if (RasterFromZoom is < TileAddress.MinZoom or > TileAddress.MaxZoom) return LodThresholdsDefect.RasterFromZoomOutOfBounds;
        if (VectorFromZoom is < TileAddress.MinZoom or > TileAddress.MaxZoom) return LodThresholdsDefect.VectorFromZoomOutOfBounds;
        if (RasterFromZoom > VectorFromZoom) return LodThresholdsDefect.RasterFromZoomAboveVectorFromZoom;

        return AssetTypeZoomRange.FindDefect(MinZoom, MaxZoom) switch
        {
            AssetTypeZoomRangeDefect.MinZoomOutOfBounds => LodThresholdsDefect.MinZoomOutOfBounds,
            AssetTypeZoomRangeDefect.MaxZoomOutOfBounds => LodThresholdsDefect.MaxZoomOutOfBounds,
            AssetTypeZoomRangeDefect.MinZoomAboveMaxZoom => LodThresholdsDefect.MinZoomAboveMaxZoom,
            _ => null,
        };
    }

    /// <summary>Throws <see cref="InvalidLodPolicyException"/> (naming <paramref name="layerId"/>, <c>null</c> = the default thresholds) when the thresholds are invalid.</summary>
    public void EnsureValid(string? layerId = null)
    {
        if (FindDefect() is { } defect)
            throw new InvalidLodPolicyException(defect, layerId, this);
    }

    /// <summary>The tier at <paramref name="zoom"/> (meaningful for valid thresholds).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoom"/> is outside [<see cref="TileAddress.MinZoom"/>, <see cref="TileAddress.MaxZoom"/>].</exception>
    public TileRenderTier Decide(int zoom)
    {
        TileAddress.EnsureZoom(zoom, nameof(zoom));

        if (!AssetTypeZoomRange.Contains(MinZoom, MaxZoom, zoom)) return TileRenderTier.Hidden;
        if (zoom >= VectorFromZoom) return TileRenderTier.Vector;
        if (zoom >= RasterFromZoom) return TileRenderTier.Raster;
        return TileRenderTier.Aggregate;
    }
}
