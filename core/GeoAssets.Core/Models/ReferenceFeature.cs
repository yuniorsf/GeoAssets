using System.Text.Json.Serialization;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Models;

/// <summary>
/// Another organization's feature as a map reference (XD01-330): its geometry and a text label, nothing else — no
/// attributes, topology, organization ids or audit fields. These five members are the whole wire shape of
/// <c>GET {prefix}/organizations/{organizationId}/reference-features</c>; a full <see cref="GeoFeature"/> of another
/// organization never leaves the server.
/// </summary>
/// <param name="Id">The feature's id.</param>
/// <param name="Geometry">The feature's geometry, GeoJSON (RFC 7946, longitude before latitude).</param>
/// <param name="Label">The feature's name (<see cref="GeoFeatureProperties.Name"/>).</param>
/// <param name="AssetTypeId">The feature's asset type id (<see cref="GeoFeatureProperties.AssetTypeId"/>).</param>
/// <param name="AssetTypeName">That asset type's name as the owning organization's catalog has it; empty when it has none.</param>
public sealed record ReferenceFeature(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("geometry")] GeoGeometry Geometry,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("assetTypeId")] string AssetTypeId,
    [property: JsonPropertyName("assetTypeName")] string AssetTypeName);
