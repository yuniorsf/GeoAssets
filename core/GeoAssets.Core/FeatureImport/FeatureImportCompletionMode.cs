namespace GeoAssets.Core.FeatureImport;

/// <summary>How <see cref="IFeatureBulkImporter.CompleteAsync"/> treats the scope's features this import did not write.</summary>
public enum FeatureImportCompletionMode
{
    /// <summary>Keep them.</summary>
    Merge,

    /// <summary>Delete them: the import is the scope's new full snapshot.</summary>
    ReplaceScope,
}
