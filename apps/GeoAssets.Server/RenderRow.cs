using NetTopologySuite.Geometries;

namespace GeoAssets.Server;

/// <summary>Minimal data required to draw one feature on a tile.</summary>
public sealed class RenderRow
{
    public required Geometry Geom        { get; init; }
    public required string   Color       { get; init; }
    public required string   Name        { get; init; }
    public required string   AssetTypeId { get; init; }
}
