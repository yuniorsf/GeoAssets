using GeoAssets.Ingestion.Profiles.Electric;
using GeoAssets.Ingestion.Sources;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary><c>REDE_LT</c>: HV segments keyed (<c>TRECHO_LT</c>, <c>NU_TRECHO_LT_ID</c>), "LT {id}", <c>Start</c>/<c>End</c>.</summary>
internal sealed class RedeLtLayerMapper : ILayerMapper
{
    public MappingResult Map(RawRecord record, string scope) =>
        GseSegments.Map(record, scope, GseFields.NamespaceTrechoLt, GseFields.HvSegmentId, "LT", ElectricAssetClasses.HvSegment);
}
