namespace GeoAssets.Core.Services;

/// <summary>
/// Thrown by <see cref="Providers.ValidatingAssetProvider"/> when a
/// <see cref="Models.GeoFeatureProperties.CustomAttributes"/> fail its asset type's
/// <see cref="Models.AssetType.AttributesSchemaJson"/> validation.
/// </summary>
public sealed class GeoFeatureAttributeValidationException : InvalidOperationException
{
    public Guid AssetTypeId { get; }
    public IReadOnlyList<string> Errors { get; }

    public GeoFeatureAttributeValidationException(Guid assetTypeId, IReadOnlyList<string> errors)
        : base($"GeoFeature custom attributes are invalid for asset type '{assetTypeId}': {string.Join("; ", errors)}")
    {
        AssetTypeId = assetTypeId;
        Errors = errors;
    }

    public GeoFeatureAttributeValidationException()
    {
        Errors = [];
    }

    public GeoFeatureAttributeValidationException(string message) : base(message)
    {
        Errors = [];
    }

    public GeoFeatureAttributeValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [];
    }
}
