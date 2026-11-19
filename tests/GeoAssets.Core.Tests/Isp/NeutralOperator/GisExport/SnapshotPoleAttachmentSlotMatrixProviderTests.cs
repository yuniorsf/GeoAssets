using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.GisExport;
using GeoAssets.NeutralOperator.Domain.Leasing;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;
using static GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy.OccupancyTestData;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.GisExport;

public sealed class SnapshotPoleAttachmentSlotMatrixProviderTests
{
    private const string Cnpj = "12.345.678/0001-90";

    private static PoleOccupancy OverCapacityPole() =>
        PoleOf(
        [
            .. Enumerable.Range(1, 6).Select(slotIndex => Entry(slotIndex) with { OperatorCnpj = Cnpj }),
            Entry(null) with { OperatorCnpj = Cnpj },
            Entry(null, PoleAttachmentOccupantStatus.ClandestineUnauthorized, identified: false),
        ]);

    [Fact]
    public void Constructor_NullDictionary_ThrowsArgumentNullException()
    {
        var act = () => new SnapshotPoleAttachmentSlotMatrixProvider(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetForPole_UnknownPole_ReturnsNull_AndItsOverCapacityEntriesAreEmpty()
    {
        var provider = SnapshotPoleAttachmentSlotMatrixProvider.From([PoleOf(Entry(1))]);

        provider.GetForPole("pole-unknown").Should().BeNull();
        provider.GetForPole("POLE-1").Should().BeNull("pole feature ids are compared ordinally");
        provider.GetOverCapacityEntries("pole-unknown").Should().BeEmpty();
    }

    [Fact]
    public void GetForPole_NullPole_ThrowsArgumentNullException()
    {
        var provider = new SnapshotPoleAttachmentSlotMatrixProvider(new Dictionary<string, PoleOccupancy>(StringComparer.Ordinal));

        var matrix = () => provider.GetForPole(null!);
        var overCapacity = () => provider.GetOverCapacityEntries(null!);

        matrix.Should().Throw<ArgumentNullException>();
        overCapacity.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetForPole_KnownPole_ReturnsItsRegulatedEntriesAsTheSixSlotMatrix()
    {
        var occupancy = PoleOf(Entry(2), Entry(5, PoleAttachmentOccupantStatus.AbandonedDeadDrop));
        var provider = new SnapshotPoleAttachmentSlotMatrixProvider(
            new Dictionary<string, PoleOccupancy>(StringComparer.Ordinal) { [Pole] = occupancy });

        var matrix = provider.GetForPole(Pole);

        matrix!.PoleFeatureId.Should().Be(Pole);
        matrix.Slots.Select(slot => (slot.SlotIndex, slot.Status)).Should().Equal(
            (2, PoleAttachmentOccupantStatus.AuthorizedContracted), (5, PoleAttachmentOccupantStatus.AbandonedDeadDrop));
        provider.GetForPole(Pole).Should().NotBeSameAs(matrix, "a matrix is mutable; changing one must not change the snapshot");
        provider.GetOverCapacityEntries(Pole).Should().BeEmpty();
    }

    [Fact]
    public void GetForPole_PoleOnFileWithNoRegulatedEntry_ReturnsAnEmptyMatrix_NotNull()
    {
        var provider = SnapshotPoleAttachmentSlotMatrixProvider.From([PoleOf()]);

        provider.GetForPole(Pole)!.Slots.Should().BeEmpty();
    }

    [Fact]
    public void GetForPole_OverCapacityPole_LeavesTheEntriesBeyondTheRegulatedSlotsOutOfTheMatrix_AndExposesThemSeparately()
    {
        var occupancy = OverCapacityPole();
        var provider = SnapshotPoleAttachmentSlotMatrixProvider.From([occupancy]);

        var matrix = provider.GetForPole(Pole);
        var overCapacity = provider.GetOverCapacityEntries(Pole);

        matrix!.Slots.Should().HaveCount(PoleAttachmentSlotMatrix.MaxSlots);
        matrix.Slots.Select(slot => slot.SlotIndex).Should().Equal(1, 2, 3, 4, 5, 6);
        overCapacity.Should().HaveCount(2).And.Equal(occupancy.OverCapacityEntries);
        overCapacity.Should().OnlyContain(entry => entry.SlotIndex == null);
        (matrix.Slots.Count + overCapacity.Count).Should().Be(occupancy.Entries.Count, "between them nothing on file is lost");
    }

    [Fact]
    public void From_NullOrTwoOccupanciesOfOnePole_Throws()
    {
        var nullInput = () => SnapshotPoleAttachmentSlotMatrixProvider.From(null!);
        var repeated = () => SnapshotPoleAttachmentSlotMatrixProvider.From([PoleOf(Entry(1)), PoleOf(Entry(2))]);

        nullInput.Should().Throw<ArgumentNullException>();
        repeated.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GisExportProjector_OverTheSnapshot_ReportsTheRegulatedSlots_AsItDoesForAnyMatrix()
    {
        var provider = SnapshotPoleAttachmentSlotMatrixProvider.From([OverCapacityPole()]);
        GeoFeature Feature(string id) => new()
        {
            Id = id,
            Geometry = new GeoPoint(-46.6, -23.5),
            Properties = new GeoFeatureProperties { AssetTypeId = "type-pole", Name = id },
        };

        var collection = GisExportProjector.Project([Feature(Pole), Feature("pole-unknown")], provider);

        collection[0].Attributes[GisExportSchema.FieldAttachmentSlotsTotal].Should().Be(6);
        collection[0].Attributes[GisExportSchema.FieldAttachmentSlotsOccupied].Should().Be(6);
        collection[0].Attributes[GisExportSchema.FieldAttachmentSlotsAvailable].Should().Be(0);
        collection[0].Attributes[GisExportSchema.FieldComplianceStatus].Should().Be(
            GisExportSchema.ComplianceCompliant, "the clandestine attachment is beyond the regulated slots, which the matrix does not carry");
        collection[1].Attributes[GisExportSchema.FieldComplianceStatus].Should().Be(GisExportSchema.ComplianceUnknown);
    }

    [Fact]
    public void LeaseBillingEngine_OverTheSnapshot_BillsTheRegulatedContractedSlots_AndNotTheOverCapacityEntries()
    {
        var provider = SnapshotPoleAttachmentSlotMatrixProvider.From([OverCapacityPole()]);

        var invoices = new LeaseBillingEngine().GenerateInvoices(
            [provider.GetForPole(Pole)!], new LeaseTariffSchedule(10m), [], new DateOnly(2026, 6, 1));

        var invoice = invoices.Should().ContainSingle().Subject;
        invoice.TenantCnpj.Should().Be(Cnpj);
        invoice.Total.Should().Be(60m, "six regulated slots at 10; the seventh, over-capacity, contracted attachment is exposed by GetOverCapacityEntries for later billing");
        provider.GetOverCapacityEntries(Pole).Count(entry => entry.IsContracted).Should().Be(1);
    }
}
