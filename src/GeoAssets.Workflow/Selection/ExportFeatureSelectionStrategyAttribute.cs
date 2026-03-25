using System.ComponentModel.Composition;

namespace GeoAssets.Workflow.Selection;

/// <summary>
/// Combined [Export] + [ExportMetadata] attribute for feature selection strategies.
/// Apply to any class implementing <see cref="IFeatureSelectionStrategy"/>
/// to make it discoverable by <see cref="FeatureSelectionRegistry"/>.
/// </summary>
[MetadataAttribute]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ExportFeatureSelectionStrategyAttribute(string strategyId)
    : ExportAttribute(typeof(IFeatureSelectionStrategy)), IFeatureSelectionStrategyMetadata
{
    public string StrategyId  { get; }       = strategyId;
    public string Category    { get; init; } = "General";
    public string DisplayName { get; init; } = strategyId;
    public string Description { get; init; } = string.Empty;
}
