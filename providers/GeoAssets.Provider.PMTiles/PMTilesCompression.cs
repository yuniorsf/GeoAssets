namespace GeoAssets.Provider.PMTiles;

/// <summary>A PMTiles v3 compression code (header bytes 97 and 98).</summary>
public enum PMTilesCompression
{
    /// <summary>Not stated.</summary>
    Unknown = 0,

    /// <summary>Stored as is.</summary>
    None = 1,

    /// <summary>gzip.</summary>
    Gzip = 2,

    /// <summary>Brotli.</summary>
    Brotli = 3,

    /// <summary>Zstandard.</summary>
    Zstd = 4,
}
