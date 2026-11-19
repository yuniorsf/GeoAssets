using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

public sealed class PoleOccupancyMapperTests
{
    private static readonly Guid Owner = PoleOccupancyEntryBuilder.DefaultPoleOwnerOrganizationId;
    private static readonly Guid Occupant = PoleOccupancyEntryBuilder.DefaultOccupantOrganizationId;

    [Fact]
    public void ToRecord_CopiesThePoleHeader_WithTheStampAsUtc()
    {
        var updatedAt = new DateTimeOffset(2026, 6, 1, 6, 0, 0, TimeSpan.FromHours(-3));
        var occupancy = new PoleOccupancyBuilder().ForPole("pole-9").OwnedBy(Owner).AtVersion(7).UpdatedAt(updatedAt).Build();
        var id = Guid.NewGuid();

        var record = PoleOccupancyMapper.ToRecord(occupancy, id);

        record.Should().BeEquivalentTo(new PoleOccupancyRecord
        {
            Id = id,
            PoleFeatureId = "pole-9",
            OrganizationId = Owner,
            Version = 7,
            UpdatedAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
        });
        record.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToEntryRecord_CopiesEveryField_AndStoresTheStatusByName()
    {
        var entry = new PoleOccupancyEntryBuilder().ForPole("pole-9").OwnedBy(Owner).OnSlot(4).OccupiedBy(Occupant)
            .WithStatus(PoleAttachmentOccupantStatus.AbandonedDeadDrop).WithOperatorCnpj("12.345.678/0001-90")
            .WithAnatelLicenseNumber("SCM-1").WithCableDiameterMm(8.125m).WithCableLoadDaN(42.5m).WithAttachedCableFeatureId("cable-1").Build();
        var poleId = Guid.NewGuid();

        var record = PoleOccupancyMapper.ToEntryRecord(entry, poleId);

        record.Should().BeEquivalentTo(new PoleOccupancyEntryRecord
        {
            Id = entry.Id,
            PoleOccupancyId = poleId,
            SlotIndex = 4,
            Status = "AbandonedDeadDrop",
            OccupantOrganizationId = Occupant,
            OperatorCnpj = "12.345.678/0001-90",
            AnatelLicenseNumber = "SCM-1",
            CableDiameterMm = 8.125m,
            CableLoadDaN = 42.5m,
            AttachedCableFeatureId = "cable-1",
            OrganizationId = Owner,
        });
    }

    [Fact]
    public void ToEntryRecord_OverCapacityUnidentifiedEntry_KeepsItsNulls()
    {
        var entry = new PoleOccupancyEntryBuilder().BeyondRegulatedSlots().Unidentified()
            .WithStatus(PoleAttachmentOccupantStatus.ClandestineUnauthorized).Build();

        var record = PoleOccupancyMapper.ToEntryRecord(entry, Guid.NewGuid());

        record.SlotIndex.Should().BeNull();
        record.OccupantOrganizationId.Should().BeNull();
        record.OperatorCnpj.Should().BeNull();
        record.AnatelLicenseNumber.Should().BeNull();
        record.AttachedCableFeatureId.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void ToDomain_RoundTripsAPoleWithItsEntries_HandingEachEntryThePoleAndOwnerOfTheHeader(int regulated)
    {
        var builder = new PoleOccupancyBuilder().ForPole("pole-9").OwnedBy(Owner).AtVersion(3);
        for (var slotIndex = 1; slotIndex <= regulated; slotIndex++)
            builder.WithSlot(slotIndex, occupantOrganizationId: Occupant, operatorCnpj: "12345678000190");
        var occupancy = builder.WithOverCapacityEntry().WithOverCapacityEntry(PoleAttachmentOccupantStatus.AuthorizedContracted, Occupant).Build();
        var poleId = Guid.NewGuid();
        var header = PoleOccupancyMapper.ToRecord(occupancy, poleId);
        var entries = occupancy.Entries.Select(entry => PoleOccupancyMapper.ToEntryRecord(entry, poleId)).Reverse().ToList();

        var roundTripped = PoleOccupancyMapper.ToDomain(header, entries);

        roundTripped.Should().BeEquivalentTo(occupancy);
        roundTripped.Entries.Should().Equal(occupancy.Entries);
        roundTripped.UpdatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ToDomain_AStampReadBackWithoutAKind_IsTakenAsUtc()
    {
        var header = new PoleOccupancyRecord
        {
            Id = Guid.NewGuid(),
            PoleFeatureId = "pole-9",
            OrganizationId = Owner,
            Version = 1,
            UpdatedAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Unspecified),
        };

        var occupancy = PoleOccupancyMapper.ToDomain(header, []);

        occupancy.UpdatedAt.Should().Be(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
        occupancy.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ToDomain_RowsThatBreakAPoleRule_FailInsteadOfProducingAHalfValidPole()
    {
        var poleId = Guid.NewGuid();
        var header = new PoleOccupancyRecord { Id = poleId, PoleFeatureId = "pole-9", OrganizationId = Owner, Version = 1 };
        PoleOccupancyEntryRecord Row() => new()
        {
            Id = Guid.NewGuid(),
            PoleOccupancyId = poleId,
            SlotIndex = 2,
            Status = "AuthorizedContracted",
            OrganizationId = Owner,
        };

        var act = () => PoleOccupancyMapper.ToDomain(header, [Row(), Row()]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(PoleAttachmentOccupantStatus.ActiveReservation, "ActiveReservation")]
    [InlineData(PoleAttachmentOccupantStatus.AuthorizedContracted, "AuthorizedContracted")]
    [InlineData(PoleAttachmentOccupantStatus.AbandonedDeadDrop, "AbandonedDeadDrop")]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized, "ClandestineUnauthorized")]
    public void StatusText_IsTheMemberName_InBothDirections(PoleAttachmentOccupantStatus status, string text)
    {
        PoleOccupancyMapper.ToStatusText(status).Should().Be(text);
        PoleOccupancyMapper.FromStatusText(text).Should().Be(status);
    }

    [Fact]
    public void StatusText_CoversEveryStatus()
    {
        foreach (var status in Enum.GetValues<PoleAttachmentOccupantStatus>())
            PoleOccupancyMapper.FromStatusText(PoleOccupancyMapper.ToStatusText(status)).Should().Be(status);
    }

    [Fact]
    public void StatusText_UnknownValueOrText_Throws()
    {
        var unknownValue = () => PoleOccupancyMapper.ToStatusText((PoleAttachmentOccupantStatus)99);
        var unknownText = () => PoleOccupancyMapper.FromStatusText("authorizedcontracted");

        unknownValue.Should().Throw<ArgumentOutOfRangeException>();
        unknownText.Should().Throw<InvalidOperationException>().WithMessage("*authorizedcontracted*");
    }
}
