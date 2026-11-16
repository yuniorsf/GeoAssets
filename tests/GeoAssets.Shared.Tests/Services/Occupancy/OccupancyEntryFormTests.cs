using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Shared.Services.Occupancy;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Occupancy;

/// <summary><see cref="OccupancyEntryForm"/>: what the editor's entry form accepts, and the change it becomes (XD01-315).</summary>
public sealed class OccupancyEntryFormTests
{
    private static readonly Guid Organization = new("0c0c0c0c-0000-4000-8000-00000000000a");

    [Fact]
    public void ANewForm_IsAContractedEntryWithNothingElseFilledIn_AndValidates()
    {
        var form = new OccupancyEntryForm();

        form.Should().BeEquivalentTo(new
        {
            Status = PoleAttachmentOccupantStatus.AuthorizedContracted, OccupantOrganizationId = "", OperatorCnpj = "", AnatelLicenseNumber = "",
            CableDiameterMm = "0", CableLoadDaN = "0", AttachedCableFeatureId = "",
        });
        form.Validate(beyondRegulatedSlots: false).Should().BeEmpty();
        form.Validate(beyondRegulatedSlots: true).Should().BeEmpty();
    }

    [Fact]
    public void Validate_NamesEachInvalidField_WithItsMessageKey()
    {
        var form = new OccupancyEntryForm
        {
            Status = (PoleAttachmentOccupantStatus)42,
            OccupantOrganizationId = "not-a-guid",
            OperatorCnpj = new string('1', 19),
            AnatelLicenseNumber = new string('L', 65),
            AttachedCableFeatureId = new string('c', 65),
            CableDiameterMm = "1.2345",
            CableLoadDaN = "-1",
        };

        form.Validate(beyondRegulatedSlots: false).Should().BeEquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OccupancyEntryForm.StatusField] = "occupancy.validation.status",
            [OccupancyEntryForm.OrganizationField] = "occupancy.validation.organization",
            [OccupancyEntryForm.CnpjField] = "occupancy.validation.cnpj",
            [OccupancyEntryForm.LicenseField] = "occupancy.validation.license",
            [OccupancyEntryForm.CableField] = "occupancy.validation.cable",
            [OccupancyEntryForm.DiameterField] = "occupancy.validation.diameter",
            [OccupancyEntryForm.LoadField] = "occupancy.validation.load",
        });
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("0", true)]
    [InlineData("12.5", true)]
    [InlineData("12.345", true)]
    [InlineData("99999.999", true)]
    [InlineData("100000", false)]
    [InlineData("12.3456", false)]
    [InlineData("-0.1", false)]
    [InlineData("12,5", false)]
    [InlineData("1e3", false)]
    [InlineData("abc", false)]
    public void Validate_Diameter_IsANonNegativeNumberWithAtMostThreeDecimals_UpToTheStoresMaximum(string text, bool valid)
    {
        var errors = new OccupancyEntryForm { CableDiameterMm = text }.Validate(beyondRegulatedSlots: false);

        errors.ContainsKey(OccupancyEntryForm.DiameterField).Should().Be(!valid);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("0c0c0c0c-0000-4000-8000-00000000000a", true)]
    [InlineData(" 0C0C0C0C-0000-4000-8000-00000000000A ", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("acme", false)]
    public void Validate_Organization_IsBlankOrANonEmptyGuid(string text, bool valid)
    {
        var errors = new OccupancyEntryForm { OccupantOrganizationId = text }.Validate(beyondRegulatedSlots: false);

        errors.ContainsKey(OccupancyEntryForm.OrganizationField).Should().Be(!valid);
    }

    [Fact]
    public void Validate_AReservation_IsOnlyForARegulatedSlot()
    {
        var form = new OccupancyEntryForm { Status = PoleAttachmentOccupantStatus.ActiveReservation };

        form.Validate(beyondRegulatedSlots: false).Should().BeEmpty();
        form.Validate(beyondRegulatedSlots: true).Should().Equal(
            new Dictionary<string, string>(StringComparer.Ordinal) { [OccupancyEntryForm.StatusField] = "occupancy.validation.reservationBeyondSlots" });
    }

    [Fact]
    public void ToChange_CarriesTheFormAsThePayload_TrimmingText_AndTurningBlankIntoNull()
    {
        var entryId = Guid.NewGuid();
        var form = new OccupancyEntryForm
        {
            Status = PoleAttachmentOccupantStatus.AbandonedDeadDrop,
            OccupantOrganizationId = $" {Organization} ",
            OperatorCnpj = " 12.345.678/0001-90 ",
            AnatelLicenseNumber = "  ",
            CableDiameterMm = " 12.345 ",
            CableLoadDaN = "",
            AttachedCableFeatureId = "cable-7",
        };

        var change = form.ToChange(OccupancyChangeKind.Add, slotIndex: 3, entryId);

        change.Should().Be(new OccupancyChange
        {
            Kind = OccupancyChangeKind.Add,
            SlotIndex = 3,
            EntryId = entryId,
            Status = PoleAttachmentOccupantStatus.AbandonedDeadDrop,
            OccupantOrganizationId = Organization,
            OperatorCnpj = "12.345.678/0001-90",
            AnatelLicenseNumber = null,
            CableDiameterMm = 12.345m,
            CableLoadDaN = 0m,
            AttachedCableFeatureId = "cable-7",
        });
    }

    [Fact]
    public void ToChange_ABlankOrganization_IsAnUnidentifiedOccupant_AndAnInvalidFormIsRefused()
    {
        new OccupancyEntryForm().ToChange(OccupancyChangeKind.Modify, 1, null).OccupantOrganizationId.Should().BeNull();

        var act = () => new OccupancyEntryForm { CableLoadDaN = "heavy" }.ToChange(OccupancyChangeKind.Modify, 1, null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void From_AnEntryOrAPendingChange_PrefillsTheForm_SoThatItRoundTrips()
    {
        var entry = new PoleOccupancyEntry
        {
            Id = Guid.NewGuid(),
            PoleFeatureId = "pole-1",
            PoleOwnerOrganizationId = Guid.NewGuid(),
            SlotIndex = 2,
            Status = PoleAttachmentOccupantStatus.ClandestineUnauthorized,
            OccupantOrganizationId = Organization,
            OperatorCnpj = "12.345.678/0001-90",
            AnatelLicenseNumber = "SCM-1",
            CableDiameterMm = 12.345m,
            CableLoadDaN = 0.5m,
            AttachedCableFeatureId = "cable-1",
        };

        var fromEntry = OccupancyEntryForm.From(entry);
        var change = fromEntry.ToChange(OccupancyChangeKind.Modify, 2, null);
        var fromChange = OccupancyEntryForm.From(change);

        fromEntry.Should().BeEquivalentTo(new
        {
            Status = PoleAttachmentOccupantStatus.ClandestineUnauthorized, OccupantOrganizationId = Organization.ToString("D"),
            OperatorCnpj = "12.345.678/0001-90", AnatelLicenseNumber = "SCM-1", CableDiameterMm = "12.345", CableLoadDaN = "0.5", AttachedCableFeatureId = "cable-1",
        });
        fromChange.Should().BeEquivalentTo(fromEntry);
        fromEntry.Validate(beyondRegulatedSlots: false).Should().BeEmpty();
        OccupancyEntryForm.From(new OccupancyChange { Kind = OccupancyChangeKind.Remove, SlotIndex = 1 }).Status
            .Should().Be(PoleAttachmentOccupantStatus.AuthorizedContracted);
        ((Action)(() => OccupancyEntryForm.From((PoleOccupancyEntry)null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => OccupancyEntryForm.From((OccupancyChange)null!))).Should().Throw<ArgumentNullException>();
    }
}
