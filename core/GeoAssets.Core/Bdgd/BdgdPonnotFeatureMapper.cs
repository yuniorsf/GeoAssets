using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Core.Bdgd;

/// <summary>
/// Maps a raw, generically-parsed <see cref="GeoFeature"/> (e.g. straight off
/// <c>GeoAssets.Provider.Shapefile.ShapefileParser</c>, which already reprojects to WGS84 and
/// copies every DBF column into <see cref="GeoFeatureProperties.CustomAttributes"/> verbatim) into
/// a properly-typed <see cref="BdgdAssetTypes.Ponnot"/> feature — pure and IO-free so it's testable
/// without a real Shapefile/File Geodatabase archive. The source dictionary's field-name casing is
/// matched case-insensitively (BDGD exports are consistently uppercase in practice, but this
/// doesn't assume it), while the mapped feature keeps the schema's own exact-case keys.
/// </summary>
public static class BdgdPonnotFeatureMapper
{
    /// <summary>
    /// Returns the mapped PONNOT feature, or <c>null</c> plus a human-readable reason when the
    /// raw feature can't be mapped (wrong geometry type, a required field missing, or a present
    /// field that fails <see cref="BdgdAssetTypes.Ponnot"/>'s attribute schema).
    /// </summary>
    public static (GeoFeature? Mapped, string? Reason) TryMap(GeoFeature raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (raw.Geometry is not GeoPoint point)
            return (null, $"feature '{raw.Id}': PONNOT requires Point geometry, got {raw.Geometry?.GetType().Name ?? "none"}");

        var source = raw.Properties.CustomAttributes;
        if (!TryGetField(source, "COD_ID", out var codId))
            return (null, $"feature '{raw.Id}': missing required COD_ID");

        var mappedAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COD_ID"] = codId,
        };

        foreach (var field in RequiredNonIdFields)
        {
            if (!TryGetField(source, field, out var value))
                return (null, $"feature '{raw.Id}' (COD_ID={codId}): missing required {field}");
            mappedAttributes[field] = value;
        }

        var errors = GeoFeatureAttributeValidator.Validate(BdgdAssetTypes.Ponnot, mappedAttributes);
        if (errors.Count > 0)
            return (null, $"feature '{raw.Id}' (COD_ID={codId}): {string.Join("; ", errors)}");

        var mapped = new GeoFeature
        {
            Geometry = point,
            Properties = new GeoFeatureProperties
            {
                Name = $"Pole {codId}",
                AssetTypeId = BdgdAssetTypeIds.Ponnot.ToString(),
                CustomAttributes = mappedAttributes,
            },
        };

        return (mapped, null);
    }

    private static readonly string[] RequiredNonIdFields = ["TIP_EST", "ALT_TOT", "CAP_TRC"];

    private static bool TryGetField(IReadOnlyDictionary<string, string> source, string field, out string value)
    {
        foreach (var (key, val) in source)
        {
            if (!string.Equals(key, field, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(val)) break;
            value = val;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
