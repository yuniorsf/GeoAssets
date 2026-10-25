namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>Progress snapshot reported by a background selection process.</summary>
public sealed record BackgroundSelectionProgress(
    int    PercentComplete,
    string Message,
    int    FeaturesFoundSoFar = 0);
