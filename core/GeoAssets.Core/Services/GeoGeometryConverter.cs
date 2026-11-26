using System.Runtime.CompilerServices;
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
    /// <summary>
    /// The converter-less copy of each options instance this converter runs under, made once per instance (XD01-430):
    /// a fresh <see cref="JsonSerializerOptions"/> per geometry rebuilt the serializer's metadata cache for every one.
    /// Weakly keyed, so an options instance that goes away takes its copy with it.
    /// </summary>
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> InnerOptions = new();

    public override GeoGeometry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            return null;

        var type = typeProp.GetString();
        if (type is null) return null;

        var innerOptions = InnerOf(options);

        return type switch
        {
            "Point"      => root.Deserialize<GeoPoint>(innerOptions),
            "LineString" => root.Deserialize<GeoLineString>(innerOptions),
            "Polygon"    => root.Deserialize<GeoPolygon>(innerOptions),
            // MultiPolygon, MultiLineString, MultiPoint, GeometryCollection, etc.
            // — preserve verbatim so Leaflet can render them correctly.
            _ => new GeoRawGeometry(type, root.GetRawText())
        };
    }

    public override void Write(Utf8JsonWriter writer, GeoGeometry value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        switch (value)
        {
            case GeoPoint p:
                JsonSerializer.Serialize(writer, p, InnerOf(options));
                break;
            case GeoLineString l:
                JsonSerializer.Serialize(writer, l, InnerOf(options));
                break;
            case GeoPolygon poly:
                JsonSerializer.Serialize(writer, poly, InnerOf(options));
                break;
            case GeoRawGeometry raw:
                raw.WriteTo(writer);
                break;
        }
    }

    /// <summary><paramref name="options"/> without converters (avoids recursion), made once per instance.</summary>
    private static JsonSerializerOptions InnerOf(JsonSerializerOptions options) =>
        InnerOptions.GetValue(options, static outer =>
        {
            var inner = new JsonSerializerOptions(outer);
            inner.Converters.Clear();
            return inner;
        });
}
