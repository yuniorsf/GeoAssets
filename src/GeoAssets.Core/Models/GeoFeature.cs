using System.Text.Json.Serialization;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Models;

/// <summary>Maps 1:1 to a GeoJSON Feature object (RFC 7946 §3.2)</summary>
public sealed class GeoFeature
{
    [JsonPropertyName("type")]
    public string Type => "Feature";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("geometry")]
    public GeoGeometry? Geometry { get; set; }

    [JsonPropertyName("properties")]
    public GeoFeatureProperties Properties { get; set; } = new();

    /// <summary>
    /// Directed topological edges leaving this feature (outgoing links in the network graph).
    /// An empty list means this feature is an isolated node.
    /// Serialized as part of the GeoJSON feature for portability.
    /// </summary>
    [JsonPropertyName("topology")]
    public List<TopoEdge> Topology { get; set; } = [];
}

public sealed class GeoFeatureProperties
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("assetTypeId")]
    public string AssetTypeId { get; set; } = AssetType.Point.Id.ToString();

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("layerId")]
    public string LayerId { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Flexible user-defined attributes stored as a JSON object</summary>
    [JsonPropertyName("customAttributes")]
    public Dictionary<string, string> CustomAttributes { get; set; } = new();
}
