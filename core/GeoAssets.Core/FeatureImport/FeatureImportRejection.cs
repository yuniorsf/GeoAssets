namespace GeoAssets.Core.FeatureImport;

/// <summary>A feature of a chunk that was not written, and why (stable, lower-kebab <see cref="Reason"/> code).</summary>
public sealed record FeatureImportRejection(string FeatureId, string Reason);
