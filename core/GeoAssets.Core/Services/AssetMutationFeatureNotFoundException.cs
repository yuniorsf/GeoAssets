namespace GeoAssets.Core.Services;

/// <summary>
/// A batch mutation names a feature that is not in the catalog it is applied to (XD01-383). A
/// <see cref="KeyNotFoundException"/>, as before, that also says which feature — what the kernel's
/// <c>AssetMutationBatchRejectedV1</c> reports as the conflicting feature.
/// </summary>
public sealed class AssetMutationFeatureNotFoundException : KeyNotFoundException
{
    public AssetMutationFeatureNotFoundException(string featureId, string message)
        : base(message)
    {
        FeatureId = featureId;
    }

    public AssetMutationFeatureNotFoundException()
    {
        FeatureId = string.Empty;
    }

    public AssetMutationFeatureNotFoundException(string message)
        : base(message)
    {
        FeatureId = string.Empty;
    }

    public AssetMutationFeatureNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        FeatureId = string.Empty;
    }

    /// <summary>The feature the mutation names.</summary>
    public string FeatureId { get; }
}
