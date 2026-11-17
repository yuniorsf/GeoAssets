namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>A planned selection-strategy run for an order (XD01-312).</summary>
/// <param name="StrategyId">The strategy the server runs.</param>
/// <param name="Parameters">The strategy's parameters.</param>
/// <param name="IsFence">Whether the run is the selection's fence (<c>polygon-fence</c>) rather than its ids.</param>
/// <param name="IdentifierCount">How many ids an <c>identifier-list</c> run sends; 0 for a fence.</param>
public sealed record OrderTargetSelectionPlan(
    string StrategyId,
    IReadOnlyDictionary<string, object> Parameters,
    bool IsFence,
    int IdentifierCount);
