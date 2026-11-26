using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>
/// <c>POSTE</c>: geographic points keyed (<c>PG</c>, <c>NU_PG_ID</c>). <c>CD_TIPO_PG</c> 90/91 are geographic points
/// ("Ponto …"), every other type a pole ("Poste …"); geometry-only records are poles. No references.
/// </summary>
internal sealed class PosteLayerMapper : ILayerMapper
{
    public MappingResult Map(RawRecord record, string scope)
    {
        ArgumentNullException.ThrowIfNull(record);
        var key = GseElements.Key(record, GseFields.NamespacePg, GseFields.PgId);
        if (GseElements.IsGeometryOnly(record))
            return MappingResult.Mapped(GseElements.Create(record, scope, key, ElementKind.Structure, GseAssetClasses.Pole, null, []));

        var id = GseValues.Key(record.Attributes, GseFields.PgId);
        var geographic = GseValues.Key(record.Attributes, GseFields.PgType) is "90" or "91";
        var assetClass = geographic ? GseAssetClasses.GeographicPoint : GseAssetClasses.Pole;
        var name = id is null ? null : (geographic ? "Ponto " : "Poste ") + id;
        return MappingResult.Mapped(GseElements.Create(record, scope, key, ElementKind.Structure, assetClass, name, []));
    }
}
