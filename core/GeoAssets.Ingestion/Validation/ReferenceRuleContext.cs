using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Validation;

/// <summary>What a reference rule inspects: the validated element, its declaring layer and its resolved references.</summary>
internal sealed record ReferenceRuleContext(
    ValidatedElement Element,
    LayerDefinition Layer,
    IReadOnlyList<ResolvedReference> Resolved);
