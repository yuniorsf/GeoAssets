using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>
/// The mapping the three GSE segment layers share: key (<paramref name="keyNamespace"/>, <paramref name="idField"/>),
/// name "{prefix} {id}", <c>Start</c> → PG <c>NU_PG_INICIAL_ID</c> at node <c>NU_NO_INICIAL</c> and <c>End</c> → PG
/// <c>NU_PG_FINAL_ID</c> at node <c>NU_NO_FINAL</c>, each snapping from the line's own endpoint. Geometry-only records
/// keep the snap-only endpoints and the layer's default class.
/// </summary>
internal static class GseSegments
{
    public static MappingResult Map(
        RawRecord record,
        string scope,
        string keyNamespace,
        string idField,
        string namePrefix,
        string defaultClass,
        Func<IReadOnlyDictionary<string, object?>, string>? classify = null,
        Func<IReadOnlyDictionary<string, object?>, IEnumerable<ElementReference>>? extraReferences = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        var key = GseElements.Key(record, keyNamespace, idField);
        var start = GseGeometry.Start(record.Geometry);
        var end = GseGeometry.End(record.Geometry);
        if (GseElements.IsGeometryOnly(record))
        {
            ElementReference[] snapped =
            [
                new(ReferenceRole.Start, GseFields.NamespacePg, null, SnapPoint: start),
                new(ReferenceRole.End, GseFields.NamespacePg, null, SnapPoint: end),
            ];
            return MappingResult.Mapped(GseElements.Create(record, scope, key, ElementKind.Segment, defaultClass, null, snapped));
        }

        var attributes = record.Attributes;
        var references = new List<ElementReference>
        {
            new(ReferenceRole.Start, GseFields.NamespacePg, GseValues.Key(attributes, GseFields.StartPgId), GseValues.Node(attributes, GseFields.StartNode), start),
            new(ReferenceRole.End, GseFields.NamespacePg, GseValues.Key(attributes, GseFields.EndPgId), GseValues.Node(attributes, GseFields.EndNode), end),
        };
        if (extraReferences is not null)
            references.AddRange(extraReferences(attributes));

        var id = GseValues.Key(attributes, idField);
        var name = id is null ? null : $"{namePrefix} {id}";
        var assetClass = classify?.Invoke(attributes) ?? defaultClass;
        return MappingResult.Mapped(GseElements.Create(record, scope, key, ElementKind.Segment, assetClass, name, references));
    }

    /// <summary>A <c>MemberOf</c>/<c>FedBy</c> reference to (<paramref name="targetNamespace"/>, <paramref name="field"/>) when the field is a positive key.</summary>
    public static IEnumerable<ElementReference> PositiveReference(
        IReadOnlyDictionary<string, object?> attributes, ReferenceRole role, string targetNamespace, string field, string? qualifier = null)
    {
        var target = GseValues.Key(attributes, field);
        if (target is not null && !target.StartsWith('-'))
            yield return new ElementReference(role, targetNamespace, target, Qualifier: qualifier);
    }
}
