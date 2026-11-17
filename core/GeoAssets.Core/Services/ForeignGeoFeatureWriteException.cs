namespace GeoAssets.Core.Services;

/// <summary>
/// Thrown by an <see cref="Interfaces.IAssetProvider"/> scoped to one organization (or to the
/// organization-less features) when a write names a feature id whose row belongs to another
/// organization (XD01-340): the provider refuses to overwrite a row it cannot see, and nothing is
/// written. A host answering a caller reports it as "not found", so another organization's ids
/// are not disclosed.
/// </summary>
public sealed class ForeignGeoFeatureWriteException : InvalidOperationException
{
    public IReadOnlyList<string> FeatureIds { get; } = [];

    public ForeignGeoFeatureWriteException(IReadOnlyList<string> featureIds)
        : base($"{featureIds?.Count ?? 0} feature id(s) of the write belong to another organization; nothing was written.")
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        FeatureIds = featureIds;
    }

    public ForeignGeoFeatureWriteException()
    {
    }

    public ForeignGeoFeatureWriteException(string message) : base(message)
    {
    }

    public ForeignGeoFeatureWriteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
