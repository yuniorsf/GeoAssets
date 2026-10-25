using System.Text.Json.Serialization;

namespace GeoAssets.Core.Models;

/// <summary>RFC 7946 §3.3: FeatureCollection</summary>
public sealed class GeoFeatureCollection
{
    [JsonPropertyName("type")]
    public string Type => "FeatureCollection";

    [JsonPropertyName("features")]
    public List<GeoFeature> Features { get; init; } = [];

    [JsonPropertyName("metadata")]
    public GeoFeatureCollectionMetadata Metadata { get; set; } = new();
}
