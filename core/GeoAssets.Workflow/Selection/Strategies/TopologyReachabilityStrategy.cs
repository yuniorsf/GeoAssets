using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>
/// Selects features reachable from a seed feature via topology traversal.
/// Covers upstream (ancestors) and downstream (descendants) analysis —
/// essential for fault tracing, supply-path inspection, and impact assessment.
///
/// Required parameters:
///   featureId  (string)              — the seed feature to start from
///   direction  (TraversalDirection)  — Downstream | Upstream | Both
///
/// Optional parameters:
///   includeSeed  (bool, default true) — whether to include the seed feature itself
/// </summary>
[ExportFeatureSelectionStrategy("topology-reachability",
    Category    = "Topology",
    DisplayName = "Topology Reachability",
    Description = "Selects upstream, downstream, or all reachable features from a seed.")]
public sealed class TopologyReachabilityStrategy : IFeatureSelectionStrategy
{
    public string StrategyId  => "topology-reachability";
    public string DisplayName => "Topology Reachability";
    public string Description => "Selects upstream, downstream, or all reachable features from a seed.";

    public Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var featureId   = context.Parameters.GetString("featureId");
        var direction   = context.Parameters.TryGetValue("direction", out var d)
                            ? FeatureSelectionParameters.ToEnum<TraversalDirection>(d)
                            : TraversalDirection.Downstream;
        var includeSeed = context.Parameters.TryGetValue("includeSeed", out var s)
                            ? FeatureSelectionParameters.ToBoolean(s)
                            : true;

        var result = new List<GeoFeature>();

        if (direction is TraversalDirection.Downstream or TraversalDirection.Both)
            result.AddRange(context.Repository.GetDescendants(featureId));

        if (direction is TraversalDirection.Upstream or TraversalDirection.Both)
        {
            foreach (var ancestor in context.Repository.GetAncestors(featureId))
                if (result.All(f => !string.Equals(f.Id, ancestor.Id, StringComparison.Ordinal)))
                    result.Add(ancestor);
        }

        if (includeSeed)
        {
            var seed = context.Repository.GetById(featureId);
            if (seed is not null && result.All(f => !string.Equals(f.Id, featureId, StringComparison.Ordinal)))
                result.Insert(0, seed);
        }

        return Task.FromResult<IReadOnlyList<GeoFeature>>(result);
    }
}
