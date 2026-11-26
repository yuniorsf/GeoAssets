using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>
/// <c>REDE_BT</c>: LV segments keyed (<c>TRECHO_BT</c>, <c>NU_TRECHO_BT_ID</c>), "BT {id}", <c>Start</c>/<c>End</c>,
/// fed by their installation (<c>FedBy</c> <c>INST</c> <c>NU_INSTALACAO_MT_ID</c>, when set).
/// </summary>
internal sealed class RedeBtLayerMapper : ILayerMapper
{
    public MappingResult Map(RawRecord record, string scope) =>
        GseSegments.Map(
            record, scope, GseFields.NamespaceTrechoBt, GseFields.LvSegmentId, "BT", GseAssetClasses.LvSegment,
            extraReferences: attributes => GseSegments.PositiveReference(attributes, ReferenceRole.FedBy, GseFields.NamespaceInst, GseFields.InstallationId));
}
