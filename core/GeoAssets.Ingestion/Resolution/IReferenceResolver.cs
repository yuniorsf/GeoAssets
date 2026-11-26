using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>One strategy of the resolver chain; answers null when it cannot resolve the reference.</summary>
internal interface IReferenceResolver
{
    ResolvedReference? Resolve(ElementReference reference, LayerDefinition declaringLayer);
}
