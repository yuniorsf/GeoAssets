namespace GeoAssets.Core.OfflinePacks;

/// <summary>What a stored offline map pack holds (XD01-464).</summary>
public enum OfflineMapPackKind
{
    /// <summary>Vector features, drawn as the pack's points, lines and areas.</summary>
    Vector = 0,

    /// <summary>Basemap images, drawn as a tile layer under the features.</summary>
    Raster = 1,

    /// <summary>The file's header can't be read (e.g. it is damaged); it can still be deleted.</summary>
    Unreadable = 2,
}
