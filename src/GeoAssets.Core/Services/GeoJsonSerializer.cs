using System.Text.Json;
using System.Text.Json.Serialization;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

public static class GeoJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new GeoGeometryConverter() }
    };

    public static string Serialize(GeoFeatureCollection collection) =>
        JsonSerializer.Serialize(collection, Options);

    public static GeoFeatureCollection? Deserialize(string json) =>
        JsonSerializer.Deserialize<GeoFeatureCollection>(json, Options);

    public static string SerializeFeature(GeoFeature feature) =>
        JsonSerializer.Serialize(feature, Options);

    public static GeoFeature? DeserializeFeature(string json) =>
        JsonSerializer.Deserialize<GeoFeature>(json, Options);

    public static GeoGeometry? DeserializeGeometry(string json) =>
        JsonSerializer.Deserialize<GeoGeometry>(json, Options);

    public static JsonSerializerOptions GetOptions() => Options;
}

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
        var json = root.GetRawText();

        var innerOptions = new JsonSerializerOptions(options);
        innerOptions.Converters.Clear(); // avoid recursion

        return type switch
        {
            "Point"      => JsonSerializer.Deserialize<GeoPoint>(json, innerOptions),
            "LineString" => JsonSerializer.Deserialize<GeoLineString>(json, innerOptions),
            "Polygon"    => JsonSerializer.Deserialize<GeoPolygon>(json, innerOptions),
            _            => null
        };
    }

    public override void Write(Utf8JsonWriter writer, GeoGeometry value, JsonSerializerOptions options)
    {
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
        }
    }
}
