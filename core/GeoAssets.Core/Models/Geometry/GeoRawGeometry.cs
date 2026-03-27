using System.Text.Json.Serialization;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace GeoAssets.Core.Models.Geometry;

/// <summary>
/// Pass-through geometry for GeoJSON types that have no native domain class
/// (MultiPoint, MultiLineString, MultiPolygon, GeometryCollection, …).
///
/// The original JSON text is stored verbatim and round-tripped on
/// serialization so that Leaflet can render it correctly.
///
/// NTS spatial operations (<see cref="NtsGeometry"/>) return an empty
/// geometry collection — spatial queries against these features will not
/// produce meaningful results until a typed subclass is added.
/// </summary>
public sealed class GeoRawGeometry : GeoGeometry
{
    private readonly string _rawJson;
    private readonly string _type;

    public GeoRawGeometry(string type, string rawJson)
    {
        _type   = type;
        _rawJson = rawJson;
    }

    [JsonPropertyName("type")]
    public override string Type => _type;

    [JsonIgnore]
    public override GeometryType GeometryType => GeometryType.Raw;

    /// <summary>
    /// Returns an empty geometry collection.
    /// Spatial predicates and measurements are not supported on raw geometries.
    /// </summary>
    [JsonIgnore]
    public override NtsGeometry NtsGeometry =>
        GeoFactory.Wgs84.CreateGeometryCollection([]);

    /// <summary>The original GeoJSON text, used to write the geometry back without modification.</summary>
    internal string RawJson => _rawJson;
}
