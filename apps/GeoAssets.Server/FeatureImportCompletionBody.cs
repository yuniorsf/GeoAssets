using GeoAssets.Core.FeatureImport;

namespace GeoAssets.Server;

/// <summary>Body of <c>POST …/feature-imports/{importId}/complete</c>: <c>{"scope":"…","mode":"merge"|"replaceScope"}</c>.</summary>
public sealed record FeatureImportCompletionBody(string? Scope, FeatureImportCompletionMode Mode = FeatureImportCompletionMode.Merge);
