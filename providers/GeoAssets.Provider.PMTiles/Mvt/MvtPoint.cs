using System.Runtime.InteropServices;

namespace GeoAssets.Provider.PMTiles.Mvt;

/// <summary>A vertex in tile coordinates: <c>0 … extent</c> from the tile's top-left corner, X east, Y south.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MvtPoint(int X, int Y);
