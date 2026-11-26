using System.Globalization;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles.Common;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles.EquatorialQgisMunicipal;

/// <summary>Builds the canonical elements and reads the values every <c>equatorial-qgis-municipal</c> mapper shares.</summary>
internal static class EquatorialElements
{
    /// <summary>A record without attributes (its layer's attribute table is missing).</summary>
    public static bool IsGeometryOnly(RawRecord record) => record.Attributes.Count == 0;

    /// <summary>The coordinate key (<paramref name="keyNamespace"/>, the geometry's representative point).</summary>
    public static SourceKey CoordinateKey(RawRecord record, string keyNamespace)
    {
        var point = SourceGeometry.Representative(record.Geometry);
        return SourceKey.ForCoordinates(EquatorialFields.ProfileId, keyNamespace, point.X, point.Y);
    }

    /// <summary>(<paramref name="keyNamespace"/>, <paramref name="naturalKey"/>), or the coordinate key when it is absent.</summary>
    public static SourceKey Key(RawRecord record, string keyNamespace, string? naturalKey) =>
        naturalKey is null ? CoordinateKey(record, keyNamespace) : new SourceKey(EquatorialFields.ProfileId, keyNamespace, naturalKey);

    /// <summary>A positive integral value as an invariant integer string; <c>null</c> when absent, not integral or not positive.</summary>
    public static string? PositiveInteger(IReadOnlyDictionary<string, object?> attributes, string field)
    {
        if (!attributes.TryGetValue(field, out var value) || value is null or bool or DateTime)
            return null;

        long? number = value switch
        {
            long integer => integer,
            int integer => integer,
            string text => long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
            IConvertible convertible => Integral(convertible.ToDouble(CultureInfo.InvariantCulture)),
            _ => null,
        };
        return number is { } positive && positive > 0 ? positive.ToString("D", CultureInfo.InvariantCulture) : null;
    }

    public static CanonicalElement Create(
        RawRecord record,
        string scope,
        SourceKey key,
        ElementKind kind,
        string assetClass,
        string? name,
        IReadOnlyList<ElementReference> references) =>
        new()
        {
            Key = key,
            Kind = kind,
            AssetClass = assetClass,
            Geometry = record.Geometry,
            Scope = scope,
            Name = name,
            Completeness = IsGeometryOnly(record) ? Completeness.GeometryOnly : Completeness.Full,
            Attributes = SourceValues.Attributes(record.Attributes),
            References = references,
        };

    private static long? Integral(double number) =>
        double.IsFinite(number) && Math.Abs(number - Math.Round(number)) < 1e-9 && Math.Abs(number) < 9.2e18
            ? (long)Math.Round(number)
            : null;
}
