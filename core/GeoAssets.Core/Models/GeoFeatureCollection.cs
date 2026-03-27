using System.Text.Json.Serialization;

namespace GeoAssets.Core.Models;

/// <summary>RFC 7946 §3.3: FeatureCollection</summary>
public sealed class GeoFeatureCollection
{
    [JsonPropertyName("type")]
    public string Type => "FeatureCollection";

    [JsonPropertyName("features")]
    public List<GeoFeature> Features { get; set; } = [];

    [JsonPropertyName("metadata")]
    public GeoFeatureCollectionMetadata Metadata { get; set; } = new();
}

public sealed class GeoFeatureCollectionMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Mis Activos GIS";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("assetTypes")]
    public List<AssetType> AssetTypes { get; set; } = [.. AssetType.Defaults];
}
