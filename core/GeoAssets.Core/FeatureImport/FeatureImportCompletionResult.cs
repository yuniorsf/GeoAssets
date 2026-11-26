namespace GeoAssets.Core.FeatureImport;

/// <summary>Outcome of finishing an import: how many features of the scope were deleted.</summary>
public sealed record FeatureImportCompletionResult(int Removed);
