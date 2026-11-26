using GeoAssets.Ingestion.Spatial;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal sealed class IdentityCoordinateTransformer(string accuracy) : ICoordinateTransformer
{
    public string Accuracy { get; } = accuracy;

    public NtsGeometry Transform(NtsGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return geometry.Copy();
    }
}
