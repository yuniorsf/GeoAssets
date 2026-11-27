using System.Runtime.InteropServices;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// One measured tile of the LOD calibration (XD01-439): the features of one asset type its buffered envelope intersects
/// and the vertices they hold (<c>ST_NPoints</c> of each whole geometry — what a tile source simplifies and clips, so
/// what drawing the tile costs). <see cref="Saturated"/> when the read stopped at its per-tile cap: the counts are then
/// lower bounds.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TileDensitySample(TileAddress Tile, int Features, long Vertices, bool Saturated);
