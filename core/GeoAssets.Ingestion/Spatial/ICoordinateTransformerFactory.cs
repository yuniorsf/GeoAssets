using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Spatial;

/// <summary>Builds a transformer for a source CRS given as WKT (ESRI or OGC), e.g. a <c>.prj</c> file's content.</summary>
public interface ICoordinateTransformerFactory
{
    /// <exception cref="NotSupportedException">The WKT cannot be interpreted.</exception>
    ICoordinateTransformer Create(string sourceCrsWkt);
}
