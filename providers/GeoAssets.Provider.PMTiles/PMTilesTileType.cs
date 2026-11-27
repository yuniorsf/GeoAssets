namespace GeoAssets.Provider.PMTiles;

/// <summary>A PMTiles v3 tile type (header byte 99).</summary>
public enum PMTilesTileType
{
    /// <summary>Not stated.</summary>
    Unknown = 0,

    /// <summary>Mapbox Vector Tile.</summary>
    Mvt = 1,

    /// <summary>PNG.</summary>
    Png = 2,

    /// <summary>JPEG.</summary>
    Jpeg = 3,

    /// <summary>WebP.</summary>
    Webp = 4,

    /// <summary>AVIF.</summary>
    Avif = 5,
}
