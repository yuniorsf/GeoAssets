using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>
/// Resolves a reference to the nearest snap target within the declaring layer's tolerance; group-anchor references are
/// left to <see cref="AnchorReferenceResolver"/>.
/// </summary>
internal sealed class SnapReferenceResolver(ReferenceIndex index) : IReferenceResolver
{
    public ResolvedReference? Resolve(ElementReference reference, LayerDefinition declaringLayer)
    {
        var point = reference.SnapPoint;
        if (point is null || point.IsEmpty || GroupAnchorKey.IsGroupAnchor(reference)
            || !index.TryFindNearest(point.X, point.Y, declaringLayer.SnapToleranceMeters, out var nearest, out var distance))
        {
            return null;
        }

        return new ResolvedReference(reference, nearest.FeatureIdText, ResolutionMethod.Snap, distance);
    }
}
