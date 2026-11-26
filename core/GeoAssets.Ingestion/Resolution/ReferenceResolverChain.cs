using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>Tries each resolver in order (key, then snap, then anchor); the first that resolves wins.</summary>
internal sealed class ReferenceResolverChain(IReadOnlyList<IReferenceResolver> resolvers) : IReferenceResolver
{
    public static ReferenceResolverChain KeySnapAnchor(ReferenceIndex index, IssueLog issues) =>
        new([new KeyReferenceResolver(index), new SnapReferenceResolver(index), new AnchorReferenceResolver(index.Anchors, issues)]);

    public ResolvedReference? Resolve(ElementReference reference, LayerDefinition declaringLayer)
    {
        foreach (var resolver in resolvers)
        {
            var resolved = resolver.Resolve(reference, declaringLayer);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }
}
