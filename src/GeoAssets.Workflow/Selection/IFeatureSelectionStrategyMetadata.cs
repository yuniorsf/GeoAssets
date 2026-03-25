namespace GeoAssets.Workflow.Selection;

/// <summary>
/// MEF metadata contract for feature selection strategies.
/// Allows the registry to read StrategyId / Category / DisplayName
/// without instantiating the strategy class.
/// </summary>
public interface IFeatureSelectionStrategyMetadata
{
    string StrategyId   { get; }
    string Category     { get; }
    string DisplayName  { get; }
    string Description  { get; }
}
