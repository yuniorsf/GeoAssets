using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>Resolves a reference by (<see cref="ElementReference.TargetNamespace"/>, <see cref="ElementReference.TargetKey"/>).</summary>
internal sealed class KeyReferenceResolver(ReferenceIndex index) : IReferenceResolver
{
    public ResolvedReference? Resolve(ElementReference reference, LayerDefinition declaringLayer)
    {
        if (string.IsNullOrWhiteSpace(reference.TargetKey)
            || !index.TryGet(new IndexKey(reference.TargetNamespace, reference.TargetKey.Trim()), out var entry))
        {
            return null;
        }

        return new ResolvedReference(reference, entry.FeatureIdText, ResolutionMethod.Key, DistanceMeters: null);
    }
}
