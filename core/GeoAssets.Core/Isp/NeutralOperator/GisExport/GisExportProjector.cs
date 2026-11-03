using System.Text.Json;
using GeoAssets.Core.Models;
using NetTopologySuite.Features;
using NetTopologySuite.IO.Converters;

namespace GeoAssets.Core.Isp.NeutralOperator.GisExport;

/// <summary>
/// Projects pole <see cref="GeoFeature"/>s — optionally joined with their
/// <see cref="PoleAttachmentSlotMatrix"/> via <see cref="IPoleAttachmentSlotMatrixProvider"/> —
/// into the generic, versioned GIS export contract (XD01-271). See <see cref="GisExportSchema"/>
/// for the field-by-field reference. Output is a plain NTS <see cref="FeatureCollection"/> with
/// flat, self-describing <see cref="AttributesTable"/> properties and standard RFC 7946
/// geometry — reuses the same <see cref="GeoJsonConverterFactory"/> this codebase already uses
/// for GeoJSON (see <c>GeoGeometry</c>), rather than a hand-rolled writer.
/// </summary>
public static class GisExportProjector
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new GeoJsonConverterFactory() },
    };

    /// <summary>
    /// Builds the export <see cref="FeatureCollection"/>. Poles with no geometry are skipped —
    /// a GIS import has nothing to place them at. <paramref name="slotMatrixProvider"/> defaults
    /// to <see cref="NullPoleAttachmentSlotMatrixProvider.Instance"/>, exporting every pole with
    /// <see cref="GisExportSchema.ComplianceUnknown"/> rather than requiring a persisted
    /// slot-matrix store that doesn't exist yet.
    /// </summary>
    public static FeatureCollection Project(
        IEnumerable<GeoFeature> poleFeatures,
        IPoleAttachmentSlotMatrixProvider? slotMatrixProvider = null)
    {
        ArgumentNullException.ThrowIfNull(poleFeatures);
        var provider = slotMatrixProvider ?? NullPoleAttachmentSlotMatrixProvider.Instance;

        var collection = new FeatureCollection();
        foreach (var pole in poleFeatures)
        {
            var geometry = pole.Geometry?.NtsGeometry;
            if (geometry is null)
                continue;

            var matrix = provider.GetForPole(pole.Id);
            var (total, occupied) = SlotCounts(matrix);

            var attributes = new AttributesTable(StringComparer.Ordinal)
            {
                { GisExportSchema.FieldSchemaVersion, GisExportSchema.Version },
                { GisExportSchema.FieldFeatureId, pole.Id },
                { GisExportSchema.FieldAssetTypeId, pole.Properties.AssetTypeId },
                { GisExportSchema.FieldName, pole.Properties.Name },
                { GisExportSchema.FieldAttachmentSlotsTotal, total },
                { GisExportSchema.FieldAttachmentSlotsOccupied, occupied },
                { GisExportSchema.FieldAttachmentSlotsAvailable, PoleAttachmentSlotMatrix.MaxSlots - total },
                { GisExportSchema.FieldComplianceStatus, ComplianceStatus(matrix) },
            };

            collection.Add(new Feature(geometry, attributes));
        }

        return collection;
    }

    /// <summary>Serializes a projected <see cref="FeatureCollection"/> to GeoJSON text.</summary>
    public static string ToGeoJson(FeatureCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return JsonSerializer.Serialize(collection, JsonOptions);
    }

    private static (int Total, int Occupied) SlotCounts(PoleAttachmentSlotMatrix? matrix)
    {
        if (matrix is null)
            return (0, 0);

        var total = matrix.Slots.Count;
        var occupied = matrix.Slots.Count(s => s.Status is
            PoleAttachmentOccupantStatus.AuthorizedContracted or
            PoleAttachmentOccupantStatus.AbandonedDeadDrop or
            PoleAttachmentOccupantStatus.ClandestineUnauthorized);
        return (total, occupied);
    }

    private static string ComplianceStatus(PoleAttachmentSlotMatrix? matrix)
    {
        if (matrix is null)
            return GisExportSchema.ComplianceUnknown;

        if (matrix.Slots.Count == 0)
            return GisExportSchema.ComplianceNoAttachments;

        var nonCompliant = matrix.Slots.Any(s => s.Status is
            PoleAttachmentOccupantStatus.ClandestineUnauthorized or
            PoleAttachmentOccupantStatus.AbandonedDeadDrop);

        return nonCompliant ? GisExportSchema.ComplianceNonCompliant : GisExportSchema.ComplianceCompliant;
    }
}
