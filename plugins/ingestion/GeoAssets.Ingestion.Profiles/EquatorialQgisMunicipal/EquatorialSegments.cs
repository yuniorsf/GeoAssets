using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles.Common;
using GeoAssets.Ingestion.Sources;
using NetTopologySuite.Geometries;

namespace GeoAssets.Ingestion.Profiles.EquatorialQgisMunicipal;

/// <summary>
/// The mapping both segment layers share: key (the layer's namespace, <c>MSLINK</c>) — the coordinate key
/// of the line's centroid without one —, name "{prefix} {MSLINK}", and <c>Start</c>/<c>End</c> → PG by snap only, from
/// the line's first/last coordinate minus the layer's drawing offset. Geometry-only records keep the same snap
/// references and no others.
/// </summary>
internal static class EquatorialSegments
{
    public static MappingResult Map(
        RawRecord record,
        string scope,
        string keyNamespace,
        string namePrefix,
        string assetClass,
        Coordinate drawingOffset,
        ElementReference? extraReference = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(drawingOffset);
        var id = SourceValues.Key(record.Attributes, EquatorialFields.SegmentId);
        var key = EquatorialElements.Key(record, keyNamespace, id);
        var references = new List<ElementReference>
        {
            new(ReferenceRole.Start, EquatorialFields.NamespacePg, null, SnapPoint: Corrected(SourceGeometry.Start(record.Geometry), drawingOffset)),
            new(ReferenceRole.End, EquatorialFields.NamespacePg, null, SnapPoint: Corrected(SourceGeometry.End(record.Geometry), drawingOffset)),
        };
        if (extraReference is not null)
            references.Add(extraReference);

        var name = id is null ? null : $"{namePrefix} {id}";
        return MappingResult.Mapped(EquatorialElements.Create(record, scope, key, ElementKind.Segment, assetClass, name, references));
    }

    /// <summary>Whether the line starts and ends on the same point, at centimetre precision.</summary>
    public static bool IsClosed(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var start = SourceGeometry.Start(geometry);
        var end = SourceGeometry.End(geometry);
        return Centimetres(start.X) == Centimetres(end.X) && Centimetres(start.Y) == Centimetres(end.Y);
    }

    private static Point Corrected(Point point, Coordinate offset) =>
        offset.X == 0 && offset.Y == 0 ? point : point.Factory.CreatePoint(new Coordinate(point.X - offset.X, point.Y - offset.Y));

    private static long Centimetres(double value) => (long)Math.Round(value * 100, MidpointRounding.AwayFromZero);
}
