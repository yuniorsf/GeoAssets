using System.Text.Json.Serialization;

namespace GeoAssets.Core.Models;

/// <summary>
/// A directed topological edge from the owning <see cref="GeoFeature"/> to another feature.
/// Used to model ordered networks: electric circuits, hydraulic pipelines, road graphs, etc.
/// </summary>
public sealed class TopoEdge
{
    /// <summary>ID of the downstream (target) feature.</summary>
    [JsonPropertyName("targetId")]
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Domain-specific relationship kind.
    /// Examples: <c>"electric-flow"</c>, <c>"water-flow"</c>, <c>"road"</c>, <c>"pipeline"</c>.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "generic";

    /// <summary>
    /// Edge weight — interpreted per domain:
    /// resistance (electrical), flow capacity (hydraulic), distance (routing), cost (logistics), etc.
    /// Defaults to 1.0 (unit cost / unit capacity).
    /// </summary>
    [JsonPropertyName("weight")]
    public double Weight { get; set; } = 1.0;

    /// <summary>Arbitrary domain-specific attributes (voltage, pressure, speed limit…).</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}
