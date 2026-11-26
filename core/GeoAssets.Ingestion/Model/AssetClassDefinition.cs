using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Ingestion.Model;

/// <summary>
/// A canonical asset class a profile maps elements to (Pole, Transformer, MvSegment…). The importer reuses the
/// catalog's asset type carrying <see cref="Role"/> (or any of <see cref="ReuseRoles"/>), otherwise creates one with
/// <see cref="TypeId"/>, <see cref="DisplayName"/>, <see cref="Geometry"/>, <see cref="Color"/> and roles
/// [<see cref="Role"/>].
/// </summary>
public sealed record AssetClassDefinition(
    string Name,
    string Role,
    Guid TypeId,
    string DisplayName,
    GeometryType Geometry,
    string Color,
    IReadOnlyList<string> ReuseRoles);
