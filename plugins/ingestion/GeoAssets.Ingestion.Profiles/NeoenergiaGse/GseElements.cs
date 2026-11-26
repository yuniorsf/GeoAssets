using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>Builds the canonical elements every GSE mapper produces.</summary>
internal static class GseElements
{
    /// <summary>A record without attributes (its layer's attribute table is missing).</summary>
    public static bool IsGeometryOnly(RawRecord record) => record.Attributes.Count == 0;

    /// <summary>
    /// The element key: (<paramref name="keyNamespace"/>, the record's <paramref name="keyField"/>), or — for a record
    /// without that key — the coordinate key of the geometry's representative point.
    /// </summary>
    public static SourceKey Key(RawRecord record, string keyNamespace, string keyField)
    {
        if (GseValues.Key(record.Attributes, keyField) is { } naturalKey)
            return new SourceKey(GseFields.ProfileId, keyNamespace, naturalKey);

        var point = GseGeometry.Representative(record.Geometry);
        return SourceKey.ForCoordinates(GseFields.ProfileId, keyNamespace, point.X, point.Y);
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
            Attributes = GseValues.Attributes(record.Attributes),
            References = references,
        };
}
