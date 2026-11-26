using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Spatial;

/// <summary>
/// Transforms native-CRS geometries to SIRGAS 2000 geographic (lon/lat), stamped SRID 4326 (WGS84-equivalent at map
/// scale). <see cref="Accuracy"/> labels the method actually used (<c>grid</c>, <c>helmert</c>, <c>none</c>) and is
/// stamped on features as <c>import.positionAccuracy</c>.
/// </summary>
public interface ICoordinateTransformer
{
    string Accuracy { get; }

    Geometry Transform(Geometry geometry);
}
