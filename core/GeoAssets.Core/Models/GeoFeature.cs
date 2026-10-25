using System.Text.Json.Serialization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Models;

/// <summary>Maps 1:1 to a GeoJSON Feature object (RFC 7946 §3.2)</summary>
public sealed class GeoFeature : IOrgOwnedResource, IScopeableResource
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
    public List<TopoEdge> Topology { get; init; } = [];

    /// <summary>See <see cref="IOrgOwnedResource"/>. Delegates to <see cref="Properties"/>,
    /// which is the JSON source of truth (<see cref="GeoFeatureProperties.OrganizationId"/>).</summary>
    Guid IOrgOwnedResource.OrganizationId => Properties.OrganizationId;

    /// <summary>See <see cref="IScopeableResource"/>. A plain, well-known key in
    /// <see cref="GeoFeatureProperties.CustomAttributes"/> — no dedicated column, so this costs
    /// nothing for features that never need scoping (XD01-174).</summary>
    string? IScopeableResource.ScopeId => Properties.CustomAttributes.GetValueOrDefault("scopeId");
}
