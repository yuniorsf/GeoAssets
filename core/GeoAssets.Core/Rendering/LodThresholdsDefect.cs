namespace GeoAssets.Core.Rendering;

/// <summary>Why <see cref="LodThresholds.FindDefect"/> rejected a layer's LOD thresholds (XD01-431).</summary>
public enum LodThresholdsDefect
{
    /// <summary><see cref="LodThresholds.RasterFromZoom"/> is outside the supported zoom levels.</summary>
    RasterFromZoomOutOfBounds,

    /// <summary><see cref="LodThresholds.VectorFromZoom"/> is outside the supported zoom levels.</summary>
    VectorFromZoomOutOfBounds,

    /// <summary><see cref="LodThresholds.RasterFromZoom"/> is above <see cref="LodThresholds.VectorFromZoom"/>: the tiers would be out of order.</summary>
    RasterFromZoomAboveVectorFromZoom,

    /// <summary><see cref="LodThresholds.MinZoom"/> is outside the supported zoom levels.</summary>
    MinZoomOutOfBounds,

    /// <summary><see cref="LodThresholds.MaxZoom"/> is outside the supported zoom levels.</summary>
    MaxZoomOutOfBounds,

    /// <summary>Both visibility bounds are set and <see cref="LodThresholds.MinZoom"/> is above <see cref="LodThresholds.MaxZoom"/>.</summary>
    MinZoomAboveMaxZoom,
}
