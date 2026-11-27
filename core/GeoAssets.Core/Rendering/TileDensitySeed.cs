using System.Runtime.InteropServices;

namespace GeoAssets.Core.Rendering;

/// <summary>A sampled feature's centroid (EPSG:4326, longitude/latitude) — where <see cref="LodCalibrator"/> looks for the tiles to measure (XD01-439).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TileDensitySeed(double Longitude, double Latitude);
