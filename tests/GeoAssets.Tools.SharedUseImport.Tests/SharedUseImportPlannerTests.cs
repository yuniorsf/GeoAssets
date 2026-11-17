using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using Xunit;
using static GeoAssets.Tools.SharedUseImport.Tests.SharedUseTestData;

namespace GeoAssets.Tools.SharedUseImport.Tests;

public sealed class SharedUseImportPlannerTests
{
    [Fact]
    public void Plan_IdentifiedOccupant_IsOneContractedEntryOnSlotOne_WithItsOrganizationCnpjAndRoundedMeasurements()
    {
        var plan = Plan(Row(1));

        var pole = plan.Poles.Should().ContainSingle().Subject;
        pole.PoleFeatureId.Should().Be("pole-1");
        pole.LegacyPoleIds.Should().Equal(1);
        pole.LegacyRowIds.Should().Equal(1);
        pole.FlaggedRows.Should().BeEmpty();
        pole.Flags.Should().BeEmpty();
        pole.Occupancy.PoleOwnerOrganizationId.Should().Be(Owner);
        pole.Occupancy.Version.Should().Be(PoleOccupancy.FirstVersion);
        pole.Occupancy.UpdatedAt.Should().Be(Now);
        pole.Occupancy.Entries.Should().ContainSingle().Which.Should().Be(new PoleOccupancyEntry
        {
            Id = SharedUseImportPlanner.EntryId(Owner, "pole-1", 1, 0),
            PoleFeatureId = "pole-1",
            PoleOwnerOrganizationId = Owner,
            SlotIndex = 1,
            Status = PoleAttachmentOccupantStatus.AuthorizedContracted,
            OccupantOrganizationId = OccupantOrganization,
            OperatorCnpj = IdentifiedCnpj,
            CableDiameterMm = 12.346m,
            CableLoadDaN = 1.235m,
        });
        plan.OwnerOrganizationId.Should().Be(Owner);
        plan.RowsRead.Should().Be(1);
        plan.SkippedRows.Should().BeEmpty();
    }

    [Fact]
    public void Plan_RowCount_BecomesThatManyEntries_OnConsecutiveSlots()
    {
        var pole = Plan(Row(1, count: 3)).Poles.Single();

        pole.Occupancy.Entries.Select(entry => entry.SlotIndex).Should().Equal(1, 2, 3);
        pole.Occupancy.Entries.Select(entry => entry.Id).Should().OnlyHaveUniqueItems();
        pole.Occupancy.Class.Should().Be(OccupancyClass.Low);
    }

    [Fact]
    public void Plan_SlotsFollowAscendingLegacyRowId_WhateverTheInputOrder()
    {
        var pole = Plan(Row(30, occupant: IdentifiedOccupant), Row(10, occupant: UnmatchedOccupant), Row(20, occupant: OccupantWithoutCnpj)).Poles.Single();

        pole.LegacyRowIds.Should().Equal(10, 20, 30);
        pole.Occupancy.Entries.Select(entry => (entry.SlotIndex, entry.Id)).Should().Equal(
            (1, SharedUseImportPlanner.EntryId(Owner, "pole-1", 10, 0)),
            (2, SharedUseImportPlanner.EntryId(Owner, "pole-1", 20, 0)),
            (3, SharedUseImportPlanner.EntryId(Owner, "pole-1", 30, 0)));
    }

    [Fact]
    public void Plan_ExactlySixPoints_FillsTheSlots_IsHigh_AndIsNotFlagged()
    {
        var pole = Plan(Row(1, count: 4), Row(2, count: 2)).Poles.Single();

        pole.Occupancy.RegulatedEntries.Should().HaveCount(6);
        pole.Occupancy.OverCapacityEntries.Should().BeEmpty();
        pole.Occupancy.Class.Should().Be(OccupancyClass.High);
        pole.Flags.Should().BeEmpty();
    }

    [Fact]
    public void Plan_MoreThanSixPoints_ImportsEveryOne_TheExtraAsOverCapacityEntries_AndFlagsThePole()
    {
        var pole = Plan(Row(1, count: 4), Row(2, count: 3), Row(3, count: 2)).Poles.Single();

        pole.Occupancy.Entries.Should().HaveCount(9);
        pole.Occupancy.RegulatedEntries.Select(entry => entry.SlotIndex).Should().Equal(1, 2, 3, 4, 5, 6);
        pole.Occupancy.OverCapacityEntries.Should().HaveCount(3).And.OnlyContain(entry => entry.SlotIndex == null);
        pole.Occupancy.OverCapacityEntries.Select(entry => entry.Id).Should().BeEquivalentTo(new[]
        {
            SharedUseImportPlanner.EntryId(Owner, "pole-1", 2, 2),
            SharedUseImportPlanner.EntryId(Owner, "pole-1", 3, 0),
            SharedUseImportPlanner.EntryId(Owner, "pole-1", 3, 1),
        });
        pole.Occupancy.IsOverCapacity.Should().BeTrue();
        pole.Occupancy.IsIrregular.Should().BeTrue();
        pole.Occupancy.Class.Should().Be(OccupancyClass.OverCapacity);
        pole.Flags.Should().Equal(SharedUsePoleFlag.OverCapacity);
    }

    [Fact]
    public void Plan_OverCapacityAndUnidentified_ClassIsUnidentified_AndBothFlagsAreKept()
    {
        var pole = Plan(Row(1, count: 6), Row(2, occupant: LegacySharedUseRow.UnidentifiedOccupantId)).Poles.Single();

        pole.Occupancy.Class.Should().Be(OccupancyClass.Unidentified);
        pole.Flags.Should().Equal(SharedUsePoleFlag.OverCapacity, SharedUsePoleFlag.UnidentifiedOccupant);
    }

    [Theory]
    [InlineData(LegacySharedUseRow.UnidentifiedOccupantId, nameof(SharedUseRowFlag.UnidentifiedOccupant), null)]
    [InlineData(UnknownOccupant, nameof(SharedUseRowFlag.UnknownOccupant), null)]
    [InlineData(OccupantWithoutCnpj, nameof(SharedUseRowFlag.OccupantWithoutCnpj), null)]
    [InlineData(UnmatchedOccupant, nameof(SharedUseRowFlag.UnmatchedCnpj), UnmatchedCnpj)]
    public void Plan_OccupantWithoutAnOrganization_IsImportedAsUnidentifiedAndClandestine_AndTheRowIsFlagged(
        int occupant, string flagName, string? keptCnpj)
    {
        var flag = Enum.Parse<SharedUseRowFlag>(flagName);
        var pole = Plan(Row(5, occupant: occupant, count: 2)).Poles.Single();

        pole.Occupancy.Entries.Should().HaveCount(2).And.OnlyContain(entry =>
            entry.OccupantOrganizationId == null
            && entry.Status == PoleAttachmentOccupantStatus.ClandestineUnauthorized
            && entry.OperatorCnpj == keptCnpj);
        pole.FlaggedRows.Should().Equal(new FlaggedSharedUseRow(5, 6, flag));
        pole.Flags.Should().Equal(SharedUsePoleFlag.UnidentifiedOccupant);
        pole.Occupancy.Class.Should().Be(OccupancyClass.Unidentified);
    }

    [Fact]
    public void Plan_UnmappedWireType_ImportsTheRowWithoutMeasurements_AndFlagsIt_ButKeepsTheOccupantIdentified()
    {
        var pole = Plan(Row(4, wireType: UnmappedWireType)).Poles.Single();

        var entry = pole.Occupancy.Entries.Single();
        entry.CableDiameterMm.Should().Be(0m);
        entry.CableLoadDaN.Should().Be(0m);
        entry.OccupantOrganizationId.Should().Be(OccupantOrganization);
        pole.FlaggedRows.Should().Equal(new FlaggedSharedUseRow(4, 5, SharedUseRowFlag.UnmappedWireType));
        pole.Flags.Should().BeEmpty();
    }

    [Fact]
    public void Plan_RowWithTwoProblems_CarriesBothFlags()
    {
        var pole = Plan(Row(4, occupant: UnknownOccupant, wireType: UnmappedWireType)).Poles.Single();

        pole.FlaggedRows.Select(row => row.Flag).Should().Equal(SharedUseRowFlag.UnknownOccupant, SharedUseRowFlag.UnmappedWireType);
    }

    [Fact]
    public void Plan_RepeatedLegacyRowId_KeepsTheFirst_AndSkipsTheRest_EvenWhenTheyDiffer()
    {
        var plan = Plan(Row(1, count: 1), Row(1, count: 5), Row(1, pole: 2));

        plan.Poles.Should().ContainSingle().Which.Occupancy.Entries.Should().ContainSingle();
        plan.SkippedRows.Should().Equal(
            new SkippedSharedUseRow(1, 1, 2, SharedUseRowSkipReason.DuplicateRow),
            new SkippedSharedUseRow(1, 2, 2, SharedUseRowSkipReason.DuplicateRow));
        plan.RowsRead.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Plan_RowWithNoPoints_IsSkipped(int count)
    {
        var plan = Plan(Row(7, count: count));

        plan.Poles.Should().BeEmpty();
        plan.SkippedRows.Should().Equal(new SkippedSharedUseRow(7, 1, 8, SharedUseRowSkipReason.NonPositiveCount));
    }

    [Fact]
    public void Plan_RowOnAnUnmappedLegacyPole_IsSkipped()
    {
        var plan = Plan(Row(7, pole: 404));

        plan.Poles.Should().BeEmpty();
        plan.SkippedRows.Should().Equal(new SkippedSharedUseRow(7, 404, 8, SharedUseRowSkipReason.UnmappedPole));
    }

    [Fact]
    public void Plan_TwoLegacyPolesOnOneFeature_AreCombined_AndFlagged()
    {
        var mapping = Mapping(Document(poles: [new(1, "pole-1"), new(2, "pole-1")]));

        var plan = SharedUseImportPlanner.Plan([Row(2, pole: 2), Row(1, pole: 1)], mapping, Owner, Now);

        var pole = plan.Poles.Should().ContainSingle().Subject;
        pole.LegacyPoleIds.Should().Equal(1, 2);
        pole.Occupancy.Entries.Should().HaveCount(2);
        pole.Flags.Should().Equal(SharedUsePoleFlag.MergedLegacyPoles);
    }

    [Fact]
    public void Plan_PolesAreInOrdinalOrderOfFeatureId()
    {
        var mapping = Mapping(Document(poles: [new(1, "b"), new(2, "B"), new(3, "a")]));

        var plan = SharedUseImportPlanner.Plan([Row(1, pole: 1), Row(2, pole: 2), Row(3, pole: 3)], mapping, Owner, Now);

        plan.Poles.Select(pole => pole.PoleFeatureId).Should().Equal("B", "a", "b");
    }

    [Fact]
    public void Plan_SameInputTwice_PlansTheSameEntries()
    {
        LegacySharedUseRow[] rows = [Row(3, count: 5), Row(1, occupant: UnknownOccupant, count: 4), Row(2, pole: 2)];

        var first = Plan(rows);
        var second = Plan([.. rows.Reverse()]);

        second.Poles.Select(pole => pole.Occupancy.Entries).Should().BeEquivalentTo(
            first.Poles.Select(pole => pole.Occupancy.Entries), options => options.WithStrictOrdering());
    }

    [Fact]
    public void EntryId_DependsOnOwnerPoleRowAndPoint()
    {
        var id = SharedUseImportPlanner.EntryId(Owner, "pole-1", 1, 0);

        id.Should().Be(SharedUseImportPlanner.EntryId(Owner, "pole-1", 1, 0)).And.NotBe(Guid.Empty);
        new[]
        {
            SharedUseImportPlanner.EntryId(OccupantOrganization, "pole-1", 1, 0),
            SharedUseImportPlanner.EntryId(Owner, "pole-2", 1, 0),
            SharedUseImportPlanner.EntryId(Owner, "pole-1", 2, 0),
            SharedUseImportPlanner.EntryId(Owner, "pole-1", 1, 1),
        }.Should().NotContain(id).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Plan_EmptyOwner_Throws()
    {
        var act = () => SharedUseImportPlanner.Plan([], Mapping(), Guid.Empty, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("ownerOrganizationId");
    }

    [Fact]
    public void Plan_NullArguments_Throw()
    {
        var nullRows = () => SharedUseImportPlanner.Plan(null!, Mapping(), Owner, Now);
        var nullMapping = () => SharedUseImportPlanner.Plan([], null!, Owner, Now);

        nullRows.Should().Throw<ArgumentNullException>().WithParameterName("rows");
        nullMapping.Should().Throw<ArgumentNullException>().WithParameterName("mapping");
    }
}
