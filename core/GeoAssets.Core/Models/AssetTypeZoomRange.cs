namespace GeoAssets.Core.Models;

/// <summary>
/// The rules of an <see cref="AssetType"/>'s optional zoom range
/// (<see cref="AssetType.MinZoom"/>/<see cref="AssetType.MaxZoom"/>): each bound is either open
/// (<c>null</c>) or a zoom level between <see cref="LowestZoom"/> and <see cref="HighestZoom"/>,
/// and a minimum never exceeds a maximum. The single definition shared by the editor, the map
/// interop, <c>ValidatingAssetProvider</c> and the server's asset-type endpoint.
/// </summary>
public static class AssetTypeZoomRange
{
    /// <summary>Lowest zoom level a bound may take.</summary>
    public const int LowestZoom = 0;

    /// <summary>Highest zoom level a bound may take.</summary>
    public const int HighestZoom = 24;

    /// <summary>Returns the first rule the range breaks, or <c>null</c> when it is valid.</summary>
    public static AssetTypeZoomRangeDefect? FindDefect(int? minZoom, int? maxZoom)
    {
        if (minZoom is < LowestZoom or > HighestZoom) return AssetTypeZoomRangeDefect.MinZoomOutOfBounds;
        if (maxZoom is < LowestZoom or > HighestZoom) return AssetTypeZoomRangeDefect.MaxZoomOutOfBounds;
        if (minZoom > maxZoom) return AssetTypeZoomRangeDefect.MinZoomAboveMaxZoom;
        return null;
    }

    /// <summary>Throws <see cref="InvalidAssetTypeZoomRangeException"/> when the range is invalid.</summary>
    public static void EnsureValid(int? minZoom, int? maxZoom)
    {
        if (FindDefect(minZoom, maxZoom) is { } defect)
            throw new InvalidAssetTypeZoomRangeException(defect, minZoom, maxZoom);
    }

    /// <summary>Whether a type with this range is drawn at <paramref name="zoom"/> (bounds inclusive, null = open).</summary>
    public static bool Contains(int? minZoom, int? maxZoom, int zoom) =>
        (minZoom is null || zoom >= minZoom) && (maxZoom is null || zoom <= maxZoom);
}
