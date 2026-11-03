using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.GisExport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.GisExport;

public class GisExportProjectorTests
{
    private sealed class FakeSlotMatrixProvider(PoleAttachmentSlotMatrix matrix) : IPoleAttachmentSlotMatrixProvider
    {
        public PoleAttachmentSlotMatrix? GetForPole(string poleFeatureId) =>
            string.Equals(poleFeatureId, matrix.PoleFeatureId, StringComparison.Ordinal) ? matrix : null;
    }

    private static GeoFeature Pole(string id, string name = "Pole") => new()
    {
        Id = id,
        Geometry = new GeoPoint(-46.6, -23.5),
        Properties = new GeoFeatureProperties { AssetTypeId = "type-pole", Name = name },
    };

    private static PoleAttachmentSlot Slot(int index, PoleAttachmentOccupantStatus status) =>
        new() { SlotIndex = index, Status = status };

    [Fact]
    public void Project_NoSlotMatrixProvider_ReportsComplianceUnknown()
    {
        var collection = GisExportProjector.Project([Pole("pole-1")]);

        var feature = collection.Should().ContainSingle().Subject;
        feature.Attributes[GisExportSchema.FieldSchemaVersion].Should().Be(GisExportSchema.Version);
        feature.Attributes[GisExportSchema.FieldFeatureId].Should().Be("pole-1");
        feature.Attributes[GisExportSchema.FieldComplianceStatus].Should().Be(GisExportSchema.ComplianceUnknown);
        feature.Attributes[GisExportSchema.FieldAttachmentSlotsTotal].Should().Be(0);
        feature.Attributes[GisExportSchema.FieldAttachmentSlotsAvailable].Should().Be(PoleAttachmentSlotMatrix.MaxSlots);
    }

    [Fact]
    public void Project_SlotMatrixWithNoSlots_ReportsNoAttachments()
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        var collection = GisExportProjector.Project([Pole("pole-1")], new FakeSlotMatrixProvider(matrix));

        collection.Should().ContainSingle().Subject
            .Attributes[GisExportSchema.FieldComplianceStatus].Should().Be(GisExportSchema.ComplianceNoAttachments);
    }

    [Fact]
    public void Project_AllSlotsAuthorizedOrReserved_ReportsCompliant()
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        matrix.AddSlot(Slot(1, PoleAttachmentOccupantStatus.AuthorizedContracted));
        matrix.AddSlot(Slot(2, PoleAttachmentOccupantStatus.ActiveReservation));

        var collection = GisExportProjector.Project([Pole("pole-1")], new FakeSlotMatrixProvider(matrix));

        var feature = collection.Should().ContainSingle().Subject;
        feature.Attributes[GisExportSchema.FieldComplianceStatus].Should().Be(GisExportSchema.ComplianceCompliant);
        feature.Attributes[GisExportSchema.FieldAttachmentSlotsTotal].Should().Be(2);
        feature.Attributes[GisExportSchema.FieldAttachmentSlotsOccupied].Should().Be(1);
        feature.Attributes[GisExportSchema.FieldAttachmentSlotsAvailable].Should().Be(4);
    }

    [Theory]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized)]
    [InlineData(PoleAttachmentOccupantStatus.AbandonedDeadDrop)]
    public void Project_AnyClandestineOrAbandonedSlot_ReportsNonCompliant(PoleAttachmentOccupantStatus badStatus)
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        matrix.AddSlot(Slot(1, PoleAttachmentOccupantStatus.AuthorizedContracted));
        matrix.AddSlot(Slot(2, badStatus));

        var collection = GisExportProjector.Project([Pole("pole-1")], new FakeSlotMatrixProvider(matrix));

        collection.Should().ContainSingle().Subject
            .Attributes[GisExportSchema.FieldComplianceStatus].Should().Be(GisExportSchema.ComplianceNonCompliant);
    }

    [Fact]
    public void Project_FeatureWithoutGeometry_IsSkipped()
    {
        var noGeometry = new GeoFeature { Id = "no-geom", Properties = new GeoFeatureProperties { AssetTypeId = "type-pole" } };

        var collection = GisExportProjector.Project([Pole("pole-1"), noGeometry]);

        collection.Should().ContainSingle().Subject.Attributes[GisExportSchema.FieldFeatureId].Should().Be("pole-1");
    }

    [Fact]
    public void ToGeoJson_ProducesWellFormedFeatureCollectionConsumableWithoutGeoAssetsKnowledge()
    {
        var collection = GisExportProjector.Project([Pole("pole-1", "Poste da Esquina")]);

        var json = GisExportProjector.ToGeoJson(collection);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("FeatureCollection");
        var feature = doc.RootElement.GetProperty("features")[0];
        feature.GetProperty("type").GetString().Should().Be("Feature");
        feature.GetProperty("geometry").GetProperty("type").GetString().Should().Be("Point");

        var props = feature.GetProperty("properties");
        props.GetProperty(GisExportSchema.FieldSchemaVersion).GetString().Should().Be("1");
        props.GetProperty(GisExportSchema.FieldName).GetString().Should().Be("Poste da Esquina");
        // Plain, flat, self-describing properties only — no GeoAssets-internal shape (no
        // customAttributes dict, no topology array, no nested GeoAssets type references).
        props.EnumerateObject().Select(p => p.Value.ValueKind)
            .Should().OnlyContain(kind => kind == JsonValueKind.String || kind == JsonValueKind.Number);
    }
}
