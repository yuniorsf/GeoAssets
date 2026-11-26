using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>
/// <c>REDE_MT</c>: MV segments keyed (<c>TRECHO_MT</c>, <c>NU_TRECHO_MT_ID</c>), "MT {id}". A segment whose start and
/// end are the same PG and node is an <c>MvJumper</c>. Besides <c>Start</c>/<c>End</c> it is a member of its feeder
/// head (<c>INST</c> <c>NU_INSTALACAO_MT_ID</c>, <c>feeder-head</c>) and protection zone (<c>CHAVE</c>
/// <c>NU_CHAVE_MT_ID</c>, <c>protection-zone</c>), when set.
/// </summary>
internal sealed class RedeMtLayerMapper : ILayerMapper
{
    public MappingResult Map(RawRecord record, string scope) =>
        GseSegments.Map(
            record, scope, GseFields.NamespaceTrechoMt, GseFields.MvSegmentId, "MT", GseAssetClasses.MvSegment,
            Classify,
            attributes => GseSegments.PositiveReference(attributes, ReferenceRole.MemberOf, GseFields.NamespaceInst, GseFields.InstallationId, GseFields.FeederHead)
                .Concat(GseSegments.PositiveReference(attributes, ReferenceRole.MemberOf, GseFields.NamespaceChave, GseFields.SwitchId, GseFields.ProtectionZone)));

    private static string Classify(IReadOnlyDictionary<string, object?> attributes)
    {
        var start = GseValues.Key(attributes, GseFields.StartPgId);
        var jumper = start is not null
            && string.Equals(start, GseValues.Key(attributes, GseFields.EndPgId), StringComparison.Ordinal)
            && GseValues.Node(attributes, GseFields.StartNode) == GseValues.Node(attributes, GseFields.EndNode);
        return jumper ? GseAssetClasses.MvJumper : GseAssetClasses.MvSegment;
    }
}
