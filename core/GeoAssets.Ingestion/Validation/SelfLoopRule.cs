using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;

namespace GeoAssets.Ingestion.Validation;

/// <summary>Start and End resolved to the same target and local node: <see cref="ImportIssueCodes.SelfLoop"/> (info).</summary>
internal sealed class SelfLoopRule : IReferenceRule
{
    public void Check(ReferenceRuleContext context, IssueLog issues)
    {
        var start = Find(context.Resolved, ReferenceRole.Start);
        var end = Find(context.Resolved, ReferenceRole.End);
        if (start is null || end is null
            || !string.Equals(start.TargetFeatureId, end.TargetFeatureId, StringComparison.Ordinal)
            || start.Reference.LocalNode != end.Reference.LocalNode)
        {
            return;
        }

        issues.Add(
            IssueSeverity.Info,
            ImportIssueCodes.SelfLoop,
            context.Element.Element.Key.ToString(),
            string.Concat("Start and End resolve to the same target (", start.TargetFeatureId, ") and node."));
    }

    private static ResolvedReference? Find(IReadOnlyList<ResolvedReference> resolved, ReferenceRole role)
    {
        foreach (var reference in resolved)
        {
            if (reference.Reference.Role == role)
            {
                return reference;
            }
        }

        return null;
    }
}
