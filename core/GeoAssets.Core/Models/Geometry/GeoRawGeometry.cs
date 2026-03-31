using System.Text.Json;
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
/// NTS spatial operations are parsed lazily from the raw JSON so that
/// persistence to PostGIS works correctly for all geometry types.
/// </summary>
public sealed class GeoRawGeometry : GeoGeometry
{
    private readonly string _rawJson;
    private readonly string _type;
    private NtsGeometry? _ntsGeometry;

    public GeoRawGeometry(string type, string rawJson)
    {
        _type    = type;
        _rawJson = rawJson;
    }

    [JsonPropertyName("type")]
    public override string Type => _type;

    [JsonIgnore]
    public override GeometryType GeometryType => GeometryType.Raw;

    /// <summary>
    /// Parses the raw GeoJSON into an NTS geometry so that PostGIS persistence
    /// stores the actual geometry instead of an empty collection.
    /// Falls back to an empty geometry collection if parsing fails.
    /// </summary>
    [JsonIgnore]
    public override NtsGeometry NtsGeometry
    {
        get
        {
            if (_ntsGeometry is not null) return _ntsGeometry;
            try
            {
                _ntsGeometry = JsonSerializer.Deserialize<NtsGeometry>(_rawJson, _ntsJsonOpts)
                               ?? GeoFactory.Wgs84.CreateGeometryCollection([]);
            }
            catch
            {
                _ntsGeometry = GeoFactory.Wgs84.CreateGeometryCollection([]);
            }
            return _ntsGeometry;
        }
    }

    /// <summary>The original GeoJSON text, used to write the geometry back without modification.</summary>
    internal string RawJson => _rawJson;
}
