using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>
/// One pipeline run. <see cref="Regions"/> selects the regions whose elements are imported (empty = all);
/// the reference index is always built from every usable region, so cross-region references resolve.
/// <see cref="Layers"/> optionally restricts the imported layers (empty = all). <see cref="ScopePrefix"/> prefixes
/// each element's scope: <c>{ScopePrefix}/{Region}/{Layer}</c>. <see cref="IncludeGeometryOnly"/> imports
/// <see cref="LayerVerdict.GeometryOnly"/> layers too.
/// </summary>
public sealed record ImportRequest
{
    public required DeliveryInventory Inventory { get; init; }
    public required ISourceProfile Profile { get; init; }
    public required string ScopePrefix { get; init; }
    public Guid OrganizationId { get; init; }
    public IReadOnlyList<string> Regions { get; init; } = [];
    public IReadOnlyList<string> Layers { get; init; } = [];
    public bool IncludeGeometryOnly { get; init; }
    public int ChunkSize { get; init; } = 5000;
}
