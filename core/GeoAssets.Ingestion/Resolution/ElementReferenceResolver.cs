using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>
/// Resolves every reference of an element through the resolver chain, counting each by method; a reference no
/// resolver answers is counted unresolved and reported as <see cref="ImportIssueCodes.DanglingReference"/> (warning).
/// </summary>
internal sealed class ElementReferenceResolver(IReferenceResolver resolver, ImportStatistics statistics, IssueLog issues)
{
    public IReadOnlyList<ResolvedReference> ResolveAll(CanonicalElement element, LayerDefinition declaringLayer)
    {
        if (element.References.Count == 0)
        {
            return [];
        }

        var resolved = new List<ResolvedReference>(element.References.Count);
        foreach (var reference in element.References)
        {
            var result = resolver.Resolve(reference, declaringLayer);
            if (result is null)
            {
                statistics.CountUnresolved();
                issues.Add(
                    IssueSeverity.Warning,
                    ImportIssueCodes.DanglingReference,
                    element.Key.ToString(),
                    string.Concat(reference.Role.ToString(), " reference to ", reference.TargetNamespace, " ", reference.TargetKey ?? "(no key)", " did not resolve."));
                continue;
            }

            statistics.CountResolved(result.Method);
            resolved.Add(result);
        }

        return resolved;
    }
}
