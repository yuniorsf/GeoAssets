using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Model;

/// <summary>
/// An unresolved reference declared by a mapper. Resolution tries the key first (<see cref="TargetNamespace"/> +
/// <see cref="TargetKey"/>), then — when <see cref="SnapPoint"/> is set — the nearest indexed structure within the
/// layer's snap tolerance. <see cref="SnapPoint"/> is in the source's native (projected, metre) CRS, already
/// corrected by the layer's drawing offset.
/// </summary>
public sealed record ElementReference(
    ReferenceRole Role,
    string TargetNamespace,
    string? TargetKey,
    int? LocalNode = null,
    Point? SnapPoint = null,
    string? Qualifier = null);
