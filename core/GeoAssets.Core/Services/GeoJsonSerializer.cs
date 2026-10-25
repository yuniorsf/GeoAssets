using System.Text.Json;
using System.Text.Json.Serialization;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Services;

public static class GeoJsonSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new GeoGeometryConverter() }
    };

    private static readonly JsonSerializerOptions _compactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new GeoGeometryConverter() }
    };

    public static string Serialize(GeoFeatureCollection collection) =>
        JsonSerializer.Serialize(collection, _options);

    public static GeoFeatureCollection? Deserialize(string json) =>
        JsonSerializer.Deserialize<GeoFeatureCollection>(json, _options);

    public static string SerializeFeature(GeoFeature feature) =>
        JsonSerializer.Serialize(feature, _options);

    public static GeoFeature? DeserializeFeature(string json) =>
        JsonSerializer.Deserialize<GeoFeature>(json, _options);

    public static GeoGeometry? DeserializeGeometry(string json) =>
        JsonSerializer.Deserialize<GeoGeometry>(json, _options);

    public static JsonSerializerOptions Options => _options;

    /// <summary>Compact (non-indented) options for the JS interop path — same converter set as <see cref="Options"/> but without <c>WriteIndented</c>.</summary>
    public static JsonSerializerOptions CompactOptions => _compactOptions;
}
