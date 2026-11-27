namespace GeoAssets.Provider.PMTiles.Mvt;

/// <summary>The geometry type of a vector tile feature (MVT 2.1 <c>GeomType</c>).</summary>
internal enum MvtGeometryType
{
    Unknown = 0,
    Point = 1,
    LineString = 2,
    Polygon = 3,
}
