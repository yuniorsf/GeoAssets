using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Shared.Services;

/// <summary>
/// The part of a serialized feature its map style depends on (XD01-429) — its id, asset type, layer override and custom
/// attributes — read into a geometry-less <see cref="GeoFeature"/>, so <see cref="LayerResolver"/> can style a feature
/// that only travels as JSON on its way to the map. A custom attribute that is not a JSON string keeps its raw JSON
/// text; a missing asset type keeps <see cref="GeoFeatureProperties"/>' default, as a full deserialization would.
/// </summary>
public static class FeatureStyleKeys
{
    /// <summary>The style key of every feature in <paramref name="rawFeaturesJson"/>, a JSON array of features; none
    /// when it is not an array.</summary>
    public static IReadOnlyList<GeoFeature> Read(string rawFeaturesJson)
    {
        ArgumentNullException.ThrowIfNull(rawFeaturesJson);
        using var document = JsonDocument.Parse(rawFeaturesJson);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? [.. document.RootElement.EnumerateArray().Select(Read)]
            : [];
    }

    /// <summary>The style key of one serialized feature.</summary>
    public static GeoFeature Read(JsonElement feature)
    {
        var key = new GeoFeature { Id = string.Empty };
        if (feature.ValueKind != JsonValueKind.Object) return key;

        key.Id = Text(feature, "id") ?? string.Empty;
        if (!feature.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return key;

        if (Text(properties, "assetTypeId") is { } assetTypeId) key.Properties.AssetTypeId = assetTypeId;
        if (Text(properties, "layerId") is { } layerId) key.Properties.LayerId = layerId;
        if (properties.TryGetProperty("customAttributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
        {
            foreach (var attribute in attributes.EnumerateObject())
                key.Properties.CustomAttributes[attribute.Name] = ScalarText(attribute.Value);
        }
        return key;
    }

    private static string? Text(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? ScalarText(value)
            : null;

    private static string ScalarText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
}
