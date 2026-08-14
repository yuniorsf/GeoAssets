namespace GeoAssets.Core.Services;

/// <summary>
/// Thrown by <see cref="Providers.ValidatingAssetProvider"/> when a
/// <see cref="Models.GeoFeatureProperties.CustomAttributes"/> fail its asset type's
/// <see cref="Models.AssetType.AttributesSchemaJson"/> validation.
/// </summary>
public sealed class GeoFeatureAttributeValidationException(Guid assetTypeId, IReadOnlyList<string> errors)
    : InvalidOperationException(
        $"GeoFeature custom attributes are invalid for asset type '{assetTypeId}': {string.Join("; ", errors)}")
{
    public Guid AssetTypeId { get; } = assetTypeId;
    public IReadOnlyList<string> Errors { get; } = errors;
}
