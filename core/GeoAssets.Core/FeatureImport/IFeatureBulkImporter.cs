namespace GeoAssets.Core.FeatureImport;

/// <summary>
/// Port for bulk-loading features into one organization's catalog in chunks (a domain-agnostic kernel capability:
/// distributor network imports are its first caller). An import is identified by an <c>importId</c> chosen by the
/// caller and covers one <c>scope</c> (e.g. <c>coelba/METROPOLITANA/REDE_BT</c>). Every imported feature is stamped
/// with <see cref="FeatureImportAttributes.Scope"/> and <see cref="FeatureImportAttributes.ImportId"/>.
/// </summary>
public interface IFeatureBulkImporter
{
    /// <summary>
    /// Upserts one chunk by feature id inside a single transaction. Features whose id already belongs to another
    /// organization, whose asset type is unknown to the organization, whose geometry does not match the asset type's
    /// <c>AllowedGeometryType</c>, or whose name exceeds 256 characters are rejected (reported, not thrown); the rest
    /// of the chunk is written. Re-sending the same chunk is harmless (same ids → updates).
    /// </summary>
    Task<FeatureImportChunkResult> ImportChunkAsync(FeatureImportChunkRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Finishes an import. <see cref="FeatureImportCompletionMode.ReplaceScope"/> deletes the organization's features
    /// of the scope whose <see cref="FeatureImportAttributes.ImportId"/> differs from this import's;
    /// <see cref="FeatureImportCompletionMode.Merge"/> deletes nothing. Either way the organization's cached catalog is
    /// refreshed once.
    /// </summary>
    Task<FeatureImportCompletionResult> CompleteAsync(FeatureImportCompletion completion, CancellationToken cancellationToken);
}
