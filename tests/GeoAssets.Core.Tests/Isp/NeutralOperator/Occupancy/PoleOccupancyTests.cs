using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using Xunit;
using static GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy.OccupancyTestData;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy;

public sealed class PoleOccupancyTests
{
    // ── Create: what it exposes ───────────────────────────────────────────────

    [Fact]
    public void Create_ExposesThePoleItsOwnerVersionStampAndEntries_OrderedBySlotThenById()
    {
        var beyondLate = Entry(null) with { Id = new Guid("00000000-0000-4000-8000-0000000000ff") };
        var beyondEarly = Entry(null) with { Id = new Guid("00000000-0000-4000-8000-000000000001") };
        var slot5 = Entry(5);
        var slot2 = Entry(2);

        var occupancy = PoleOccupancy.Create(Pole, Owner, [beyondLate, slot5, beyondEarly, slot2], version: 4, Now);

        occupancy.PoleFeatureId.Should().Be(Pole);
        occupancy.PoleOwnerOrganizationId.Should().Be(Owner);
        occupancy.Version.Should().Be(4);
        occupancy.UpdatedAt.Should().Be(Now);
        occupancy.Entries.Should().Equal(slot2, slot5, beyondEarly, beyondLate);
        occupancy.RegulatedEntries.Should().Equal(slot2, slot5);
        occupancy.OverCapacityEntries.Should().Equal(beyondEarly, beyondLate);
    }

    [Fact]
    public void Create_NoEntries_IsAnEmptyRegularPole()
    {
        var occupancy = PoleOf();

        occupancy.Entries.Should().BeEmpty();
        occupancy.OccupiedCount.Should().Be(0);
        occupancy.IsOverCapacity.Should().BeFalse();
        occupancy.HasUnidentifiedOccupant.Should().BeFalse();
        occupancy.IsIrregular.Should().BeFalse();
        occupancy.Class.Should().Be(OccupancyClass.None);
    }

    [Theory]
    [InlineData(6, 1, 7)]
    [InlineData(6, 30, 36)]
    public void Create_MoreThanSixOccupiedPoints_KeepsEveryEntry_AndIsOverCapacity(int regulated, int beyond, int occupied)
    {
        var entries = Entries(regulated, beyond);

        var occupancy = PoleOf([.. entries]);

        occupancy.Entries.Should().HaveCount(occupied).And.BeEquivalentTo(entries);
        occupancy.OverCapacityEntries.Should().HaveCount(beyond);
        occupancy.OccupiedCount.Should().Be(occupied);
        occupancy.IsOverCapacity.Should().BeTrue();
        occupancy.IsIrregular.Should().BeTrue();
    }

    [Theory]
    [InlineData(6, 0)]
    [InlineData(5, 1)]
    [InlineData(0, 6)]
    [InlineData(1, 0)]
    public void Create_SixOccupiedPointsOrFewer_IsNotOverCapacity_WhereverTheEntriesSit(int regulated, int beyond)
    {
        var occupancy = PoleOf([.. Entries(regulated, beyond)]);

        occupancy.OccupiedCount.Should().Be(regulated + beyond);
        occupancy.IsOverCapacity.Should().BeFalse();
        occupancy.IsIrregular.Should().BeFalse();
    }

    [Fact]
    public void Create_AReservedSlotIsNotAnOccupiedPoint_SoSixCablesPlusAReservationIsNotOverCapacity()
    {
        var entries = Entries(regulated: 5, beyond: 1);
        entries.Add(Entry(6, PoleAttachmentOccupantStatus.ActiveReservation));

        var occupancy = PoleOf([.. entries]);

        occupancy.Entries.Should().HaveCount(7);
        occupancy.OccupiedCount.Should().Be(6);
        occupancy.IsOverCapacity.Should().BeFalse();
    }

    [Theory]
    [InlineData(PoleAttachmentOccupantStatus.AuthorizedContracted)]
    [InlineData(PoleAttachmentOccupantStatus.AbandonedDeadDrop)]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized)]
    public void Create_AnOccupiedPointWithoutAnOrganization_FlagsThePoleAsHavingAnUnidentifiedOccupant(PoleAttachmentOccupantStatus status)
    {
        var occupancy = PoleOf(Entry(1), Entry(2, status, identified: false));

        occupancy.HasUnidentifiedOccupant.Should().BeTrue();
        occupancy.IsOverCapacity.Should().BeFalse();
        occupancy.IsIrregular.Should().BeTrue();
        occupancy.Class.Should().Be(OccupancyClass.Unidentified);
    }

    // ── Entry flags ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PoleAttachmentOccupantStatus.ActiveReservation, true, false, false, false)]
    [InlineData(PoleAttachmentOccupantStatus.ActiveReservation, false, false, false, false)]
    [InlineData(PoleAttachmentOccupantStatus.AuthorizedContracted, true, true, false, true)]
    [InlineData(PoleAttachmentOccupantStatus.AuthorizedContracted, false, true, true, false)]
    [InlineData(PoleAttachmentOccupantStatus.AbandonedDeadDrop, true, true, false, false)]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized, true, true, false, false)]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized, false, true, true, false)]
    public void Entry_Flags_FollowStatusAndWhetherTheOccupantIsKnown(
        PoleAttachmentOccupantStatus status, bool identified, bool isOccupied, bool isUnidentified, bool isContracted)
    {
        var entry = Entry(1, status, identified);

        entry.IsOccupied.Should().Be(isOccupied);
        entry.IsUnidentified.Should().Be(isUnidentified);
        entry.IsContracted.Should().Be(isContracted);
    }

    [Fact]
    public void Entry_WithASlot_IsRegulated_AndWithoutOne_IsBeyondTheRegulatedSlots()
    {
        Entry(3).IsRegulated.Should().BeTrue();
        Entry(3).IsBeyondRegulatedSlots.Should().BeFalse();
        Entry(null).IsRegulated.Should().BeFalse();
        Entry(null).IsBeyondRegulatedSlots.Should().BeTrue();
    }

    // ── Create: invariants ────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-3)]
    public void Create_SlotIndexOutsideOneToSix_ThrowsArgumentOutOfRange(int slotIndex)
    {
        var act = () => PoleOf(Entry(slotIndex));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void Create_SlotIndexAtEitherEndOfTheRange_IsAccepted(int slotIndex)
    {
        PoleOf(Entry(slotIndex)).RegulatedEntries.Should().ContainSingle();
    }

    [Fact]
    public void Create_TwoEntriesOnOneRegulatedSlot_ThrowsInvalidOperation()
    {
        var act = () => PoleOf(Entry(2), Entry(2));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Slot index 2*");
    }

    [Fact]
    public void Create_AnyNumberOfEntriesWithoutASlot_IsAccepted()
    {
        PoleOf([.. Entries(regulated: 0, beyond: 500)]).OverCapacityEntries.Should().HaveCount(500);
    }

    [Fact]
    public void Create_AReservationBeyondTheRegulatedSlots_ThrowsArgumentException()
    {
        var act = () => PoleOf(Entry(null, PoleAttachmentOccupantStatus.ActiveReservation));

        act.Should().Throw<ArgumentException>().WithMessage("*reservation*");
    }

    [Fact]
    public void Create_EntryIdEmptyOrRepeated_ThrowsArgumentException()
    {
        var entry = Entry(1);

        var empty = () => PoleOf(entry with { Id = Guid.Empty });
        var repeated = () => PoleOf(entry, Entry(2) with { Id = entry.Id });

        empty.Should().Throw<ArgumentException>();
        repeated.Should().Throw<ArgumentException>().WithMessage("*more than once*");
    }

    [Fact]
    public void Create_EntryOfAnotherPoleOrAnotherOwner_OrANullEntry_ThrowsArgumentException()
    {
        var otherPole = () => PoleOf(Entry(1) with { PoleFeatureId = "pole-2" });
        var otherCase = () => PoleOf(Entry(1) with { PoleFeatureId = "POLE-1" });
        var otherOwner = () => PoleOf(Entry(1) with { PoleOwnerOrganizationId = OccupantA });
        var nullEntry = () => PoleOf(Entry(1), null!);

        otherPole.Should().Throw<ArgumentException>();
        otherCase.Should().Throw<ArgumentException>("pole feature ids are compared ordinally");
        otherOwner.Should().Throw<ArgumentException>();
        nullEntry.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_EmptyOccupantOrganization_ThrowsArgumentException_ButNullIsAnUnidentifiedOccupant()
    {
        var empty = () => PoleOf(Entry(1) with { OccupantOrganizationId = Guid.Empty });

        empty.Should().Throw<ArgumentException>();
        PoleOf(Entry(1) with { OccupantOrganizationId = null }).HasUnidentifiedOccupant.Should().BeTrue();
    }

    [Fact]
    public void Create_UndefinedStatus_ThrowsArgumentOutOfRange()
    {
        var act = () => PoleOf(Entry(1) with { Status = (PoleAttachmentOccupantStatus)42 });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_BlankOrOverLongText_ThrowsArgumentException_AndTextAtTheLimitIsAccepted()
    {
        var acts = new Action[]
        {
            () => PoleOf(Entry(1) with { OperatorCnpj = " " }),
            () => PoleOf(Entry(1) with { OperatorCnpj = new string('1', PoleOccupancyEntry.MaxOperatorCnpjLength + 1) }),
            () => PoleOf(Entry(1) with { AnatelLicenseNumber = "" }),
            () => PoleOf(Entry(1) with { AnatelLicenseNumber = new string('a', PoleOccupancyEntry.MaxAnatelLicenseNumberLength + 1) }),
            () => PoleOf(Entry(1) with { AttachedCableFeatureId = "\t" }),
            () => PoleOf(Entry(1) with { AttachedCableFeatureId = new string('c', PoleOccupancyEntry.MaxFeatureIdLength + 1) }),
        };

        foreach (var act in acts)
            act.Should().Throw<ArgumentException>();

        var atTheLimit = Entry(1) with
        {
            OperatorCnpj = new string('1', PoleOccupancyEntry.MaxOperatorCnpjLength),
            AnatelLicenseNumber = new string('a', PoleOccupancyEntry.MaxAnatelLicenseNumberLength),
            AttachedCableFeatureId = new string('c', PoleOccupancyEntry.MaxFeatureIdLength),
        };
        PoleOf(atTheLimit).Entries.Should().Equal(atTheLimit);
    }

    [Fact]
    public void Create_MeasurementNegativeTooLargeOrTooPrecise_ThrowsArgumentOutOfRange_AndTheLimitsAreAccepted()
    {
        var acts = new Action[]
        {
            () => PoleOf(Entry(1) with { CableDiameterMm = -0.001m }),
            () => PoleOf(Entry(1) with { CableDiameterMm = PoleOccupancyEntry.MaxCableDiameterMm + 0.001m }),
            () => PoleOf(Entry(1) with { CableDiameterMm = 1.2345m }),
            () => PoleOf(Entry(1) with { CableLoadDaN = -1m }),
            () => PoleOf(Entry(1) with { CableLoadDaN = PoleOccupancyEntry.MaxCableLoadDaN + 0.001m }),
            () => PoleOf(Entry(1) with { CableLoadDaN = 0.0001m }),
        };

        foreach (var act in acts)
            act.Should().Throw<ArgumentOutOfRangeException>();

        var atTheLimit = Entry(1) with { CableDiameterMm = PoleOccupancyEntry.MaxCableDiameterMm, CableLoadDaN = PoleOccupancyEntry.MaxCableLoadDaN };
        PoleOf(atTheLimit, Entry(2) with { CableDiameterMm = 0m, CableLoadDaN = 1.5000m }).Entries.Should().HaveCount(2);
    }

    [Fact]
    public void Create_BlankOrOverLongPole_NullEntries_OrAVersionBelowOne_Throw()
    {
        var blank = () => PoleOccupancy.Create(" ", Owner, [], 1, Now);
        var nullPole = () => PoleOccupancy.Create(null!, Owner, [], 1, Now);
        var tooLong = () => PoleOccupancy.Create(new string('p', PoleOccupancyEntry.MaxFeatureIdLength + 1), Owner, [], 1, Now);
        var nullEntries = () => PoleOccupancy.Create(Pole, Owner, null!, 1, Now);
        var versionZero = () => PoleOccupancy.Create(Pole, Owner, [], PoleOccupancy.UnsavedVersion, Now);

        blank.Should().Throw<ArgumentException>();
        nullPole.Should().Throw<ArgumentNullException>();
        tooLong.Should().Throw<ArgumentException>();
        nullEntries.Should().Throw<ArgumentNullException>();
        versionZero.Should().Throw<ArgumentOutOfRangeException>();
        PoleOccupancy.Create(new string('p', PoleOccupancyEntry.MaxFeatureIdLength), Owner, [], 1, Now).Version.Should().Be(PoleOccupancy.FirstVersion);
    }

    // ── Revise ────────────────────────────────────────────────────────────────

    [Fact]
    public void Revise_ReplacesTheEntries_AddsOneToTheVersion_AndLeavesTheOriginalUntouched()
    {
        var kept = Entry(1);
        var original = PoleOccupancy.Create(Pole, Owner, [kept, Entry(2)], version: 3, Now);
        var added = Entry(null);

        var revised = original.Revise([kept, added], Now.AddHours(1));

        revised.Version.Should().Be(4);
        revised.UpdatedAt.Should().Be(Now.AddHours(1));
        revised.PoleFeatureId.Should().Be(Pole);
        revised.PoleOwnerOrganizationId.Should().Be(Owner);
        revised.Entries.Should().Equal(kept, added);
        original.Version.Should().Be(3);
        original.Entries.Should().HaveCount(2);
    }

    public static TheoryData<PoleAttachmentOccupantStatus, PoleAttachmentOccupantStatus> StatusPairs()
    {
        var pairs = new TheoryData<PoleAttachmentOccupantStatus, PoleAttachmentOccupantStatus>();
        foreach (var from in Enum.GetValues<PoleAttachmentOccupantStatus>())
            foreach (var to in Enum.GetValues<PoleAttachmentOccupantStatus>())
                pairs.Add(from, to);
        return pairs;
    }

    [Theory]
    [MemberData(nameof(StatusPairs))]
    public void Revise_StatusChangeOnARegulatedSlot_IsAcceptedExactlyWhenTheSlotTransitionGraphAllowsIt(
        PoleAttachmentOccupantStatus from, PoleAttachmentOccupantStatus to)
    {
        var entry = Entry(3, from);
        var original = PoleOf(entry);

        var act = () => original.Revise([entry with { Status = to }], Now);

        if (PoleAttachmentSlotTransitions.IsValid(from, to))
        {
            act.Should().NotThrow();
        }
        else
        {
            var thrown = act.Should().Throw<InvalidPoleAttachmentSlotTransitionException>().Which;
            thrown.From.Should().Be(from);
            thrown.To.Should().Be(to);
        }
    }

    [Fact]
    public void Revise_TheSlotDecides_NotTheEntry_SoADifferentEntryOnTheSameSlotIsStillAStatusChange()
    {
        var original = PoleOf(Entry(3, PoleAttachmentOccupantStatus.ActiveReservation));

        var act = () => original.Revise([Entry(3, PoleAttachmentOccupantStatus.AbandonedDeadDrop)], Now);

        act.Should().Throw<InvalidPoleAttachmentSlotTransitionException>();
    }

    [Fact]
    public void Revise_EmptyingASlot_FillingAFreeSlot_AndOverCapacityEntries_AreNotSubjectToTheTransitionGraph()
    {
        var beyond = Entry(null, PoleAttachmentOccupantStatus.AuthorizedContracted);
        var original = PoleOf(Entry(1, PoleAttachmentOccupantStatus.ActiveReservation), beyond);

        var revised = original.Revise(
            [Entry(2, PoleAttachmentOccupantStatus.AbandonedDeadDrop), beyond with { Status = PoleAttachmentOccupantStatus.AbandonedDeadDrop }], Now);
        var promoted = revised.Revise([beyond with { SlotIndex = 1, Status = PoleAttachmentOccupantStatus.AuthorizedContracted }], Now);

        revised.Entries.Select(entry => entry.Status).Should().OnlyContain(status => status == PoleAttachmentOccupantStatus.AbandonedDeadDrop);
        promoted.RegulatedEntries.Should().ContainSingle().Which.Id.Should().Be(beyond.Id);
        promoted.Version.Should().Be(3);
    }

    [Fact]
    public void Revise_AppliesTheSameRulesAsCreate()
    {
        var original = PoleOf(Entry(1));

        var duplicateSlot = () => original.Revise([Entry(2), Entry(2)], Now);
        var outOfRange = () => original.Revise([Entry(9)], Now);
        var nullEntries = () => original.Revise(null!, Now);

        duplicateSlot.Should().Throw<InvalidOperationException>();
        outOfRange.Should().Throw<ArgumentOutOfRangeException>();
        nullEntries.Should().Throw<ArgumentNullException>();
    }

    // ── ToSlotMatrix ──────────────────────────────────────────────────────────

    [Fact]
    public void ToSlotMatrix_ProjectsTheRegulatedEntriesOnly_FieldByField()
    {
        var slot2 = Entry(2, PoleAttachmentOccupantStatus.ClandestineUnauthorized) with
        {
            OperatorCnpj = "12.345.678/0001-90",
            AnatelLicenseNumber = "SCM-7",
            CableDiameterMm = 9.5m,
            CableLoadDaN = 120m,
            AttachedCableFeatureId = "cable-1",
        };
        var occupancy = PoleOf([Entry(6), slot2, .. Entries(regulated: 0, beyond: 3)]);

        var matrix = occupancy.ToSlotMatrix();

        matrix.PoleFeatureId.Should().Be(Pole);
        matrix.Slots.Select(slot => slot.SlotIndex).Should().Equal(2, 6);
        matrix.Slots[0].Should().BeEquivalentTo(new PoleAttachmentSlot
        {
            SlotIndex = 2,
            Status = PoleAttachmentOccupantStatus.ClandestineUnauthorized,
            OperatorCnpj = "12.345.678/0001-90",
            AnatelLicenseNumber = "SCM-7",
            CableDiameterMm = 9.5m,
            CableLoadDaN = 120m,
            AttachedCableFeatureId = "cable-1",
        });
        occupancy.ToSlotMatrix().Should().NotBeSameAs(matrix, "the matrix is mutable, so every caller gets its own");
    }

    [Fact]
    public void ToSlotMatrix_AFullPoleWithOverCapacityEntries_FitsTheSixSlotMatrix()
    {
        var matrix = PoleOf([.. Entries(regulated: 6, beyond: 10)]).ToSlotMatrix();

        matrix.Slots.Should().HaveCount(PoleAttachmentSlotMatrix.MaxSlots);
    }
}
