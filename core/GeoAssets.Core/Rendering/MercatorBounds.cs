using System.Runtime.InteropServices;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// An EPSG:3857 (Web Mercator) envelope in meters (XD01-431) — what a tile covers on the projected plane
/// (<see cref="TileAddress.Envelope3857"/>, the box PostGIS <c>ST_TileEnvelope</c> answers for the same tile).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MercatorBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>East-west extent in meters.</summary>
    public double Width => MaxX - MinX;

    /// <summary>North-south extent in meters.</summary>
    public double Height => MaxY - MinY;
}
