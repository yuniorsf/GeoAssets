namespace GeoAssets.Core.Messaging.Events;

/// <summary>The <see cref="AssetMutationBatchRejectedEvent.ReasonCode"/> values (XD01-383): stable and few, so they can label a metric.</summary>
public static class AssetMutationBatchRejectionCodes
{
    /// <summary>A mutation names a feature that is not in the organization's catalog.</summary>
    public const string FeatureNotFound = "feature-not-found";

    /// <summary>A mutation's attributes or geometry do not satisfy its asset type.</summary>
    public const string ValidationFailed = "validation-failed";

    /// <summary>A mutation kind or geometry the catalog does not support.</summary>
    public const string Unsupported = "unsupported";

    /// <summary>The batch itself is malformed (no organization, a bad argument).</summary>
    public const string InvalidBatch = "invalid-batch";
}
