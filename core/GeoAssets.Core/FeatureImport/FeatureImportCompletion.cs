namespace GeoAssets.Core.FeatureImport;

/// <summary>Request to finish an import.</summary>
public sealed record FeatureImportCompletion(
    Guid OrganizationId,
    string ImportId,
    string Scope,
    FeatureImportCompletionMode Mode);
