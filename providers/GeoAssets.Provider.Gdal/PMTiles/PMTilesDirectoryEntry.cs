using System.Runtime.InteropServices;

namespace GeoAssets.Provider.Gdal.PMTiles;

/// <summary>
/// One entry of a PMTiles v3 directory (XD01-437): <see cref="RunLength"/> consecutive tile ids from
/// <see cref="TileId"/> share the bytes at <see cref="Offset"/> (from the tile data section) — or, when
/// <see cref="RunLength"/> is 0, the entry points at a leaf directory at <see cref="Offset"/> from the leaf directories
/// section.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PMTilesDirectoryEntry(ulong TileId, ulong Offset, uint Length, uint RunLength)
{
    /// <summary>Whether the entry points at a leaf directory instead of tile data.</summary>
    public bool IsLeafDirectory => RunLength == 0;
}
