using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles.Electric;
using GeoAssets.Ingestion.Sources;
using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Profiles.EquatorialQgisMunicipal;

/// <summary>
/// <c>TRECHO_SECUNDARIO</c>: LV segments keyed (<c>TRECHO_BT</c>, <c>MSLINK</c>), "BT {MSLINK}", <c>Start</c>/<c>End</c>
/// snapped to the poles at the line's endpoints corrected by the layer's drawing offset, and — when
/// <c>MSLINK_INS</c> is positive — <c>FedBy</c> the transformer the circuit's group (<c>MSLINK_INS</c>) is anchored to
/// (group-anchor reference to <c>TRAFO</c>).
/// </summary>
internal sealed class TrechoSecundarioLayerMapper : ILayerMapper
{
    private static readonly Coordinate DrawingOffset = new(EquatorialFields.LvDrawingOffsetX, EquatorialFields.LvDrawingOffsetY);

    public MappingResult Map(RawRecord record, string scope)
    {
        ArgumentNullException.ThrowIfNull(record);
        var group = EquatorialElements.PositiveInteger(record.Attributes, EquatorialFields.FeedingInstallation);
        var fedBy = group is null ? null : new ElementReference(ReferenceRole.FedBy, EquatorialFields.NamespaceTrafo, null, GroupKey: group);
        return EquatorialSegments.Map(record, scope, EquatorialFields.NamespaceTrechoBt, "BT", ElectricAssetClasses.LvSegment, DrawingOffset, fedBy);
    }
}
