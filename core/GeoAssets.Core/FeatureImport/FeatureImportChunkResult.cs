namespace GeoAssets.Core.FeatureImport;

/// <summary>Outcome of one chunk.</summary>
public sealed record FeatureImportChunkResult(int Inserted, int Updated, IReadOnlyList<FeatureImportRejection> Rejected);
