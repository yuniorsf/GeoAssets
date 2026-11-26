using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Model;

/// <summary>
/// An unresolved reference declared by a mapper. Resolution tries the key first (<see cref="TargetNamespace"/> +
/// <see cref="TargetKey"/>), then — when <see cref="SnapPoint"/> is set — the nearest indexed structure within the
/// layer's snap tolerance. <see cref="SnapPoint"/> is in the source's native (projected, metre) CRS, already
/// corrected by the layer's drawing offset.
/// <para>
/// A <see cref="GroupKey"/> makes it a <b>group-anchor</b> reference (formats that link a circuit to its device only by
/// position, e.g. an LV circuit and its transformer): the target is the single element of a layer whose
/// <see cref="Profiles.LayerDefinition.AnchorNamespace"/> equals <see cref="TargetNamespace"/> and whose point lies,
/// within the declaring layer's snap tolerance, on a node of the group — the Start/End snap points (or geometry
/// endpoints) of every element declaring the same <see cref="GroupKey"/>. <see cref="TargetKey"/> is ignored.
/// </para>
/// </summary>
public sealed record ElementReference(
    ReferenceRole Role,
    string TargetNamespace,
    string? TargetKey,
    int? LocalNode = null,
    Point? SnapPoint = null,
    string? Qualifier = null,
    string? GroupKey = null);
