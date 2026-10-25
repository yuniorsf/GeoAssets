using System.Text.Json.Serialization;

namespace GeoAssets.Core.Models;

public sealed class GeoFeatureCollectionMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Mis Activos GIS";

    /// <summary>
    /// Defaults to <see cref="DateTime.MinValue"/> — a "creation time unknown" sentinel, not the
    /// current clock; see <see cref="GeoFeatureProperties.CreatedAt"/> for the rationale. Callers
    /// that build a collection explicitly (e.g. <c>AssetService.BuildCollection</c>, export flows)
    /// set this themselves from an injected <see cref="TimeProvider"/>.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("assetTypes")]
    public List<AssetType> AssetTypes { get; init; } = [.. AssetType.Defaults];
}
