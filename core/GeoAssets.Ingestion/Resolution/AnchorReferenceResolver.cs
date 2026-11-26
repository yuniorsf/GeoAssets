using System.Globalization;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>
/// Resolves a group-anchor reference (<see cref="ElementReference.GroupKey"/>) to the single candidate of its
/// <see cref="ElementReference.TargetNamespace"/> within the declaring layer's snap tolerance of any node of its group.
/// Each group's outcome is computed once and shared by every element of the group; a group with no candidate is
/// reported once as <see cref="ImportIssueCodes.DanglingReference"/>, one with several as
/// <see cref="ImportIssueCodes.AmbiguousAnchor"/> (warnings), and its references stay unresolved.
/// </summary>
internal sealed class AnchorReferenceResolver(AnchorIndex anchors, IssueLog issues) : IReferenceResolver
{
    private readonly Dictionary<IndexKey, AnchorMatch> _outcomes = [];

    public ResolvedReference? Resolve(ElementReference reference, LayerDefinition declaringLayer)
    {
        if (!GroupAnchorKey.TryGet(reference, out var group))
        {
            return null;
        }

        if (!_outcomes.TryGetValue(group, out var match))
        {
            var tolerance = declaringLayer.SnapToleranceMeters;
            match = anchors.Match(group, tolerance);
            _outcomes.Add(group, match);
            Report(group, match, tolerance);
        }

        return match.IsResolved
            ? new ResolvedReference(reference, match.Anchor.FeatureIdText, ResolutionMethod.Anchor, match.DistanceMeters)
            : null;
    }

    private void Report(IndexKey group, AnchorMatch match, double tolerance)
    {
        if (match.IsResolved)
        {
            return;
        }

        var subject = string.Concat(group.Namespace, " group ", group.Value);
        if (match.CandidateCount == 0)
        {
            issues.Add(
                IssueSeverity.Warning,
                ImportIssueCodes.DanglingReference,
                subject,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"No {group.Namespace} anchor lies within {tolerance:F2} m of the {match.NodeCount} node(s) of group {group.Value}; its references did not resolve."));
            return;
        }

        issues.Add(
            IssueSeverity.Warning,
            ImportIssueCodes.AmbiguousAnchor,
            subject,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{match.CandidateCount} {group.Namespace} candidates lie within {tolerance:F2} m of the {match.NodeCount} node(s) of group {group.Value}; its references did not resolve."));
    }
}
