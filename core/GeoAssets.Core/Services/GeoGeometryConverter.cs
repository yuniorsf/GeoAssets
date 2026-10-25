using System.Text.Json;
using System.Text.Json.Serialization;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

/// <summary>
/// Custom polymorphic converter for GeoGeometry that reads the "type" field
/// to determine the concrete geometry class to instantiate.
/// </summary>
public sealed class GeoGeometryConverter : JsonConverter<GeoGeometry>
{
    public override GeoGeometry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            return null;

        var type = typeProp.GetString();
        if (type is null) return null;

        var json = root.GetRawText();

        var innerOptions = new JsonSerializerOptions(options);
        innerOptions.Converters.Clear(); // avoid recursion

        return type switch
        {
            "Point"      => JsonSerializer.Deserialize<GeoPoint>(json, innerOptions),
            "LineString" => JsonSerializer.Deserialize<GeoLineString>(json, innerOptions),
            "Polygon"    => JsonSerializer.Deserialize<GeoPolygon>(json, innerOptions),
            // MultiPolygon, MultiLineString, MultiPoint, GeometryCollection, etc.
            // — preserve verbatim so Leaflet can render them correctly.
            _ => new GeoRawGeometry(type, json)
        };
    }

    public override void Write(Utf8JsonWriter writer, GeoGeometry value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var innerOptions = new JsonSerializerOptions(options);
        innerOptions.Converters.Clear();

        switch (value)
        {
            case GeoPoint p:
                JsonSerializer.Serialize(writer, p, innerOptions);
                break;
            case GeoLineString l:
                JsonSerializer.Serialize(writer, l, innerOptions);
                break;
            case GeoPolygon poly:
                JsonSerializer.Serialize(writer, poly, innerOptions);
                break;
            case GeoRawGeometry raw:
                writer.WriteRawValue(raw.RawJson);
                break;
        }
    }
}
