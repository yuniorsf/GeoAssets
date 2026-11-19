using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Rendering;

/// <summary>What drawing one feature on a map image needs (XD01-372): its geometry and resolved color, nothing else.</summary>
public sealed record RenderableFeature(string Id, string Name, string AssetTypeId, GeoGeometry Geometry, string Color);
