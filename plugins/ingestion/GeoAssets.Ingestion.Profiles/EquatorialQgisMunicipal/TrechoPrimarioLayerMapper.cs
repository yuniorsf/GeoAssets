using GeoAssets.Ingestion.Profiles.Electric;
using GeoAssets.Ingestion.Sources;
using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Profiles.EquatorialQgisMunicipal;

/// <summary>
/// <c>TRECHO_PRIMARIO</c>: MV segments keyed (<c>TRECHO_MT</c>, <c>MSLINK</c>), "MT {MSLINK}", <c>Start</c>/<c>End</c>
/// snapped to the poles at the line's own endpoints. A line that starts and ends on the same point (to the centimetre)
/// is an <c>MvJumper</c>.
/// </summary>
internal sealed class TrechoPrimarioLayerMapper : ILayerMapper
{
    private static readonly Coordinate NoOffset = new(0, 0);

    public MappingResult Map(RawRecord record, string scope)
    {
        ArgumentNullException.ThrowIfNull(record);
        var assetClass = EquatorialSegments.IsClosed(record.Geometry) ? ElectricAssetClasses.MvJumper : ElectricAssetClasses.MvSegment;
        return EquatorialSegments.Map(record, scope, EquatorialFields.NamespaceTrechoMt, "MT", assetClass, NoOffset);
    }
}
