namespace GeoAssets.Core.Models;

/// <summary>Why <see cref="AssetTypeZoomRange"/> rejected a zoom range.</summary>
public enum AssetTypeZoomRangeDefect
{
    /// <summary>The minimum zoom is outside the supported zoom levels.</summary>
    MinZoomOutOfBounds,

    /// <summary>The maximum zoom is outside the supported zoom levels.</summary>
    MaxZoomOutOfBounds,

    /// <summary>Both bounds are set and the minimum is above the maximum.</summary>
    MinZoomAboveMaxZoom,
}
