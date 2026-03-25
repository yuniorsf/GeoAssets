namespace GeoAssets.Workflow.Selection;

/// <summary>
/// Serialisable record of which strategy was used and with which parameters.
/// Stored on <see cref="Orders.IServiceOrder.SelectionSpec"/> for audit and replay.
/// </summary>
public sealed record FeatureSelectionSpec
{
    /// <summary>Identifies the strategy (matches <see cref="IFeatureSelectionStrategy.StrategyId"/>).</summary>
    public string StrategyId { get; init; } = string.Empty;

    /// <summary>The parameters that were passed to the strategy at execution time.</summary>
    public IReadOnlyDictionary<string, object> Parameters { get; init; } =
        new Dictionary<string, object>();

    /// <summary>UTC timestamp when the selection was executed.</summary>
    public DateTime ExecutedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Optional free-text note explaining why this selection was made.</summary>
    public string? Note { get; init; }
}
