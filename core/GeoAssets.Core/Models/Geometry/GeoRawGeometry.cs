using System.Text.Json;
using System.Text.Json.Serialization;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace GeoAssets.Core.Models.Geometry;

/// <summary>
/// Pass-through geometry for GeoJSON types that have no native domain class
/// (MultiPoint, MultiLineString, MultiPolygon, GeometryCollection, …).
///
/// Read from GeoJSON, the original JSON text is stored verbatim and round-tripped on
/// serialization so that Leaflet can render it correctly; NTS spatial operations are parsed
/// lazily from the raw JSON so that persistence to PostGIS works correctly for all geometry types.
///
/// Wrapped from an NTS geometry (<see cref="GeoGeometry.FromNts"/>), the NTS object is the source
/// of truth, as for the typed geometries: it is kept as is — SRID included — and written straight
/// to the JSON output, with no intermediate GeoJSON text (XD01-430).
/// </summary>
public sealed class GeoRawGeometry : GeoGeometry
{
    private readonly string? _rawJson;
    private readonly string _type;
    private NtsGeometry? _ntsGeometry;

    public GeoRawGeometry(string type, string rawJson)
    {
        _type    = type;
        _rawJson = rawJson;
    }

    /// <summary>Wraps an existing NTS geometry — no copy, no GeoJSON text until one is asked for.</summary>
    internal GeoRawGeometry(NtsGeometry geometry)
    {
        _type        = geometry.GeometryType;
        _ntsGeometry = geometry;
    }

    [JsonPropertyName("type")]
    public override string Type => _type;

    [JsonIgnore]
    public override GeometryType GeometryType => GeometryType.Raw;

    /// <summary>
    /// The wrapped NTS geometry, or — read from GeoJSON — the raw GeoJSON parsed into one so that
    /// PostGIS persistence stores the actual geometry instead of an empty collection.
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
                _ntsGeometry = JsonSerializer.Deserialize<NtsGeometry>(_rawJson!, _ntsJsonOpts)
                               ?? GeoFactory.Wgs84.CreateGeometryCollection([]);
            }
            catch
            {
                _ntsGeometry = GeoFactory.Wgs84.CreateGeometryCollection([]);
            }
            return _ntsGeometry;
        }
    }

    /// <summary>Writes the geometry: the original GeoJSON text verbatim, or the wrapped NTS geometry directly.</summary>
    internal void WriteTo(Utf8JsonWriter writer)
    {
        if (_rawJson is not null)
            writer.WriteRawValue(_rawJson);
        else
            JsonSerializer.Serialize(writer, _ntsGeometry, _ntsJsonOpts);
    }
}
