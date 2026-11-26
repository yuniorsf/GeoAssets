using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;

namespace GeoAssets.Ingestion.Validation;

/// <summary>
/// The blocking rules of an element: its asset class must be one of the profile's
/// (<see cref="ImportIssueCodes.UnmappedRecord"/> otherwise) and its geometry must fit the class's kind
/// (<see cref="ImportIssueCodes.GeometryMismatch"/> otherwise). A failing element is skipped with an error.
/// </summary>
internal sealed class ElementShapeValidator
{
    private readonly Dictionary<string, AssetClassDefinition> _classes = new(StringComparer.Ordinal);

    public ElementShapeValidator(IReadOnlyList<AssetClassDefinition> assetClasses)
    {
        foreach (var assetClass in assetClasses)
        {
            _classes.TryAdd(assetClass.Name, assetClass);
        }
    }

    public ValidatedElement? Validate(CanonicalElement element, IssueLog issues)
    {
        var subject = element.Key.ToString();
        if (!_classes.TryGetValue(element.AssetClass, out var assetClass))
        {
            issues.Add(IssueSeverity.Error, ImportIssueCodes.UnmappedRecord, subject, string.Concat("Unknown asset class '", element.AssetClass, "'."));
            return null;
        }

        var geometry = GeometryKindNormalizer.Normalize(element.Geometry, assetClass.Geometry);
        if (geometry is null)
        {
            issues.Add(
                IssueSeverity.Error,
                ImportIssueCodes.GeometryMismatch,
                subject,
                string.Concat("A ", element.Geometry.GeometryType, " geometry does not fit asset class '", assetClass.Name, "' (", assetClass.Geometry.ToString(), ")."));
            return null;
        }

        return new ValidatedElement(element, assetClass, geometry);
    }
}
