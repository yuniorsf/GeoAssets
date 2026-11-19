using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using Xunit;
using static GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy.OccupancyTestPoles;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy;

/// <summary>
/// <see cref="OccupancyChangeApplier"/>: the apply rules OCC-B1..B4 (XD01-315) on regulated
/// slots and on entries beyond them, and the preview it produces — occupied count, the two
/// irregularity flags and the class.
/// </summary>
public sealed class OccupancyChangeApplierTests
{
    private static OccupancyProjection Apply(PoleOccupancy? baseline, params OccupancyChange[] changes) =>
        OccupancyChangeApplier.Apply(baseline, Pole, Owner, changes);

    // ── OCC-B1 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PoleAttachmentOccupantStatus.AuthorizedContracted)]
    [InlineData(PoleAttachmentOccupantStatus.ActiveReservation)]
    [InlineData(PoleAttachmentOccupantStatus.AbandonedDeadDrop)]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized)]
    public void OCC_B1_AddOnARegulatedSlotThatHoldsAnEntry_ThrowsInvalidOperationException(PoleAttachmentOccupantStatus held)
    {
        var baseline = PoleOf(1, Entry(1, slotIndex: 3, held));

        var act = () => Apply(baseline, Add(50, slotIndex: 3));

        act.Should().Throw<InvalidOperationException>().Which.Should().NotBeAssignableTo<KeyNotFoundException>();
    }

    [Fact]
    public void OCC_B1_AddOnAFreeRegulatedSlot_PutsTheEntryThere_WithTheChangesPayload()
    {
        var baseline = PoleWith(regulated: 2, beyond: 0);
        var add = Add(50, slotIndex: 5) with { AnatelLicenseNumber = "SCM-7", AttachedCableFeatureId = "cable-9" };

        var result = Apply(baseline, add);

        result.Entries.Should().HaveCount(3);
        result.Entries[2].Should().BeEquivalentTo(new PoleOccupancyEntry
        {
            Id = Id(50),
            PoleFeatureId = Pole,
            PoleOwnerOrganizationId = Owner,
            SlotIndex = 5,
            Status = PoleAttachmentOccupantStatus.AuthorizedContracted,
            OccupantOrganizationId = OccupantB,
            OperatorCnpj = "98.765.432/0001-10",
            AnatelLicenseNumber = "SCM-7",
            CableDiameterMm = 9.5m,
            CableLoadDaN = 80m,
            AttachedCableFeatureId = "cable-9",
        });
    }

    [Fact]
    public void OCC_B1_AddOnAPoleWithNothingOnFile_StartsItsOccupancy()
    {
        var result = Apply(baseline: null, Add(50, slotIndex: 1), Add(51, slotIndex: null));

        result.Entries.Select(e => e.Id).Should().Equal(Id(50), Id(51));
        result.OccupiedCount.Should().Be(2);
        result.Class.Should().Be(OccupancyClass.Low);
    }

    [Fact]
    public void OCC_B1_AddBeyondTheRegulatedSlots_NeverCollides_HoweverManyThePoleAlreadyHolds()
    {
        var baseline = PoleWith(regulated: 6, beyond: 4);

        var result = Apply(baseline, Add(50, null), Add(51, null), Add(52, null));

        result.Entries.Should().HaveCount(13);
        result.Entries.Count(e => e.IsBeyondRegulatedSlots).Should().Be(7);
    }

    [Fact]
    public void OCC_B1_AddBeyondTheRegulatedSlotsAsAReservation_ThrowsArgumentException_OnlyARegulatedSlotCanBeReserved()
    {
        var act = () => Apply(PoleWith(6, 0), Add(50, null, PoleAttachmentOccupantStatus.ActiveReservation));

        act.Should().Throw<ArgumentException>();
    }

    // ── OCC-B2 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OccupancyChangeKind.Remove)]
    [InlineData(OccupancyChangeKind.Modify)]
    [InlineData(OccupancyChangeKind.MarkUnidentified)]
    public void OCC_B2_RemoveModifyOrMarkOnASlotHoldingNothing_ThrowsKeyNotFoundException(OccupancyChangeKind kind)
    {
        var baseline = PoleWith(regulated: 2, beyond: 1);

        var act = () => Apply(baseline, OnSlot(kind, 4, PoleAttachmentOccupantStatus.AuthorizedContracted));

        act.Should().Throw<KeyNotFoundException>().Which.Should().BeOfType<OccupancyEntryNotFoundException>().Which.SlotIndex.Should().Be(4);
    }

    [Theory]
    [InlineData(OccupancyChangeKind.Remove)]
    [InlineData(OccupancyChangeKind.Modify)]
    [InlineData(OccupancyChangeKind.MarkUnidentified)]
    public void OCC_B2_RemoveModifyOrMarkOfAnEntryNotOnThePole_ThrowsKeyNotFoundException(OccupancyChangeKind kind)
    {
        var baseline = PoleWith(regulated: 2, beyond: 1);

        var act = () => Apply(baseline, OnEntry(kind, 999, PoleAttachmentOccupantStatus.AuthorizedContracted));

        act.Should().Throw<KeyNotFoundException>().Which.Should().BeOfType<OccupancyEntryNotFoundException>().Which.EntryId.Should().Be(Id(999));
    }

    [Fact]
    public void OCC_B2_AnythingButAnAddOnAPoleWithNothingOnFile_ThrowsKeyNotFoundException()
    {
        ((Action)(() => Apply(null, OnSlot(OccupancyChangeKind.Remove, 1)))).Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void OCC_B2_RemoveOfARegulatedSlot_FreesIt_AndLeavesEveryOtherEntryAsItWas()
    {
        var baseline = PoleWith(regulated: 3, beyond: 1);

        var result = Apply(baseline, OnSlot(OccupancyChangeKind.Remove, 2));

        result.Entries.Should().Equal(baseline.Entries.Where(e => e.SlotIndex != 2));
        result.OccupiedCount.Should().Be(3);
    }

    [Fact]
    public void OCC_B2_RemoveOfAnOverCapacityEntry_ByItsId_TakesOnlyThatEntryOff()
    {
        var baseline = PoleWith(regulated: 6, beyond: 3);

        var result = Apply(baseline, OnEntry(OccupancyChangeKind.Remove, 102));

        result.Entries.Should().Equal(baseline.Entries.Where(e => e.Id != Id(102)));
        result.Entries.Count(e => e.IsBeyondRegulatedSlots).Should().Be(2);
    }

    [Fact]
    public void OCC_B2_ARegulatedEntryAddressedByItsId_IsTheSameAsAddressingItsSlot()
    {
        var baseline = PoleWith(regulated: 3, beyond: 0);

        Apply(baseline, OnEntry(OccupancyChangeKind.Remove, 2)).Should().BeEquivalentTo(Apply(baseline, OnSlot(OccupancyChangeKind.Remove, 2)));
    }

    // ── OCC-B3 ────────────────────────────────────────────────────────────────

    public static TheoryData<PoleAttachmentOccupantStatus, PoleAttachmentOccupantStatus> EveryStatusPair()
    {
        var pairs = new TheoryData<PoleAttachmentOccupantStatus, PoleAttachmentOccupantStatus>();
        foreach (var from in Enum.GetValues<PoleAttachmentOccupantStatus>())
            foreach (var to in Enum.GetValues<PoleAttachmentOccupantStatus>())
                pairs.Add(from, to);
        return pairs;
    }

    [Theory]
    [MemberData(nameof(EveryStatusPair))]
    public void OCC_B3_ModifyOnARegulatedSlot_FollowsTheSlotTransitionGraph(PoleAttachmentOccupantStatus from, PoleAttachmentOccupantStatus to)
    {
        var baseline = PoleOf(1, Entry(1, slotIndex: 2, from));
        var act = () => Apply(baseline, OnSlot(OccupancyChangeKind.Modify, 2, to));

        if (PoleAttachmentSlotTransitions.IsValid(from, to))
        {
            act().Entries.Should().ContainSingle().Which.Status.Should().Be(to);
        }
        else
        {
            act.Should().Throw<InvalidPoleAttachmentSlotTransitionException>()
                .Which.Should().BeEquivalentTo(new { From = from, To = to });
        }
    }

    [Fact]
    public void OCC_B3_ModifyOfAnOverCapacityEntry_IsNotSubjectToTheSlotGraph()
    {
        // Abandoned → contracted is not an allowed move on a regulated slot.
        var baseline = PoleOf(1, Entry(101, slotIndex: null, PoleAttachmentOccupantStatus.AbandonedDeadDrop));

        var result = Apply(baseline, OnEntry(OccupancyChangeKind.Modify, 101, PoleAttachmentOccupantStatus.AuthorizedContracted));

        result.Entries.Should().ContainSingle().Which.Status.Should().Be(PoleAttachmentOccupantStatus.AuthorizedContracted);
    }

    [Fact]
    public void OCC_B3_Modify_ReplacesTheWholePayload_AndKeepsTheEntrysIdAndPosition()
    {
        var baseline = PoleOf(4,
            Entry(1, slotIndex: 2) with { AnatelLicenseNumber = "SCM-1", AttachedCableFeatureId = "cable-1" },
            Entry(101, slotIndex: null));
        var modify = new OccupancyChange
        {
            Kind = OccupancyChangeKind.Modify,
            SlotIndex = 2,
            Status = PoleAttachmentOccupantStatus.AbandonedDeadDrop,
            OccupantOrganizationId = OccupantB,
            CableDiameterMm = 1.125m,
        };

        var result = Apply(baseline, modify);

        result.Entries[0].Should().BeEquivalentTo(new PoleOccupancyEntry
        {
            Id = Id(1),
            PoleFeatureId = Pole,
            PoleOwnerOrganizationId = Owner,
            SlotIndex = 2,
            Status = PoleAttachmentOccupantStatus.AbandonedDeadDrop,
            OccupantOrganizationId = OccupantB,
            OperatorCnpj = null,
            AnatelLicenseNumber = null,
            CableDiameterMm = 1.125m,
            CableLoadDaN = 0m,
            AttachedCableFeatureId = null,
        }, "a Modify does not merge: what it leaves out is cleared");
        result.Entries[1].Should().Be(baseline.Entries[1]);
    }

    [Fact]
    public void OCC_B3_ModifyWithAnInvalidPayload_FailsAsTheEntryRulesDo()
    {
        var baseline = PoleWith(regulated: 1, beyond: 0);
        OccupancyChange Modify(Func<OccupancyChange, OccupancyChange> with) =>
            with(OnSlot(OccupancyChangeKind.Modify, 1, PoleAttachmentOccupantStatus.AuthorizedContracted));

        ((Action)(() => Apply(baseline, Modify(c => c with { CableDiameterMm = 1.2345m })))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => Apply(baseline, Modify(c => c with { CableLoadDaN = -1m })))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => Apply(baseline, Modify(c => c with { OperatorCnpj = " " })))).Should().Throw<ArgumentException>();
        ((Action)(() => Apply(baseline, Modify(c => c with { OccupantOrganizationId = Guid.Empty })))).Should().Throw<ArgumentException>();
        ((Action)(() => Apply(baseline, Modify(c => c with { Status = (PoleAttachmentOccupantStatus)77 })))).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── OCC-B4 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PoleAttachmentOccupantStatus.AuthorizedContracted)]
    [InlineData(PoleAttachmentOccupantStatus.AbandonedDeadDrop)]
    [InlineData(PoleAttachmentOccupantStatus.ActiveReservation)]
    [InlineData(PoleAttachmentOccupantStatus.ClandestineUnauthorized)]
    public void OCC_B4_MarkUnidentifiedOnARegulatedSlot_ClearsOrganizationAndCnpj_AndSetsTheClandestineStatus(PoleAttachmentOccupantStatus from)
    {
        var entry = Entry(1, slotIndex: 3, from) with { AnatelLicenseNumber = "SCM-1", AttachedCableFeatureId = "cable-1" };
        var baseline = PoleOf(2, entry, Entry(2, slotIndex: 4));

        var result = Apply(baseline, OnSlot(OccupancyChangeKind.MarkUnidentified, 3));

        result.Entries[0].Should().Be(entry with
        {
            OccupantOrganizationId = null,
            OperatorCnpj = null,
            Status = PoleAttachmentOccupantStatus.ClandestineUnauthorized,
        });
        result.Entries[0].IsUnidentified.Should().BeTrue();
        result.Entries[1].Should().Be(baseline.Entries[1]);
        result.HasUnidentifiedOccupant.Should().BeTrue();
        result.Class.Should().Be(OccupancyClass.Unidentified);
    }

    [Fact]
    public void OCC_B4_MarkUnidentifiedOnAnOverCapacityEntry_ByItsId_DoesTheSame()
    {
        var baseline = PoleWith(regulated: 6, beyond: 2);

        var result = Apply(baseline, OnEntry(OccupancyChangeKind.MarkUnidentified, 102));

        var marked = result.Entries.Single(e => e.Id == Id(102));
        marked.Should().Be(baseline.Entries.Single(e => e.Id == Id(102)) with
        {
            OccupantOrganizationId = null,
            OperatorCnpj = null,
            Status = PoleAttachmentOccupantStatus.ClandestineUnauthorized,
        });
        marked.SlotIndex.Should().BeNull();
        result.Entries.Where(e => e.Id != Id(102)).Should().OnlyContain(e => !e.IsUnidentified);
        result.IsOverCapacity.Should().BeTrue();
        result.HasUnidentifiedOccupant.Should().BeTrue();
        result.Class.Should().Be(OccupancyClass.Unidentified, "an unidentified occupant outranks over capacity");
    }

    [Fact]
    public void OCC_B4_MarkUnidentified_IgnoresWhateverPayloadTheChangeCarries()
    {
        var baseline = PoleWith(regulated: 1, beyond: 0);
        var change = OnSlot(OccupancyChangeKind.MarkUnidentified, 1, PoleAttachmentOccupantStatus.AuthorizedContracted) with { OperatorCnpj = "ignored" };

        var marked = Apply(baseline, change).Entries.Single();

        marked.Status.Should().Be(PoleAttachmentOccupantStatus.ClandestineUnauthorized);
        marked.OccupantOrganizationId.Should().BeNull();
        marked.OperatorCnpj.Should().BeNull();
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 1, 1, false, OccupancyClass.Low)]
    [InlineData(4, 0, 1, 5, false, OccupancyClass.Low)]
    [InlineData(5, 0, 1, 6, false, OccupancyClass.High)]
    [InlineData(6, 0, 1, 7, true, OccupancyClass.OverCapacity)]
    [InlineData(6, 3, 2, 11, true, OccupancyClass.OverCapacity)]
    [InlineData(3, 2, 1, 6, false, OccupancyClass.High)]
    public void Preview_AddingBeyondTheRegulatedSlots_CountsEveryCable_AndGoesOverCapacityOnlyPastSix(
        int regulated, int beyond, int added, int expectedOccupied, bool expectedOverCapacity, OccupancyClass expectedClass)
    {
        var baseline = PoleWith(regulated, beyond);
        var adds = Enumerable.Range(0, added).Select(n => Add(50 + n, slotIndex: null)).ToArray();

        var preview = Apply(baseline, adds);

        preview.OccupiedCount.Should().Be(expectedOccupied);
        preview.IsOverCapacity.Should().Be(expectedOverCapacity);
        preview.HasUnidentifiedOccupant.Should().BeFalse();
        preview.IsIrregular.Should().Be(expectedOverCapacity);
        preview.Class.Should().Be(expectedClass);
    }

    [Fact]
    public void Preview_GoingFromSixToSevenOccupied_TurnsAFullPoleIntoAnOverCapacityOne_AndBackWhenTheSeventhIsRemoved()
    {
        var full = PoleWith(regulated: 6, beyond: 0);
        OccupancyProjection.From(full).Should().BeEquivalentTo(new { OccupiedCount = 6, IsOverCapacity = false, Class = OccupancyClass.High });

        var seven = Apply(full, Add(50, slotIndex: null));

        seven.Should().BeEquivalentTo(new { OccupiedCount = 7, IsOverCapacity = true, HasUnidentifiedOccupant = false, Class = OccupancyClass.OverCapacity });
        seven.Entries.Should().HaveCount(7);
        seven.Entries[^1].SlotIndex.Should().BeNull();

        var sevenOnFile = PoleOccupancy.Create(Pole, Owner, seven.Entries, 4, Now);
        Apply(sevenOnFile, OnEntry(OccupancyChangeKind.Remove, 50))
            .Should().BeEquivalentTo(new { OccupiedCount = 6, IsOverCapacity = false, Class = OccupancyClass.High });
    }

    [Fact]
    public void Preview_AnUnidentifiedAddBeyondTheSlots_SetsBothFlags_AndClassifiesAsUnidentified()
    {
        var unidentified = Add(50, null, PoleAttachmentOccupantStatus.ClandestineUnauthorized) with { OccupantOrganizationId = null, OperatorCnpj = null };

        var preview = Apply(PoleWith(regulated: 6, beyond: 0), unidentified);

        preview.Should().BeEquivalentTo(new
        {
            OccupiedCount = 7, IsOverCapacity = true, HasUnidentifiedOccupant = true, IsIrregular = true, Class = OccupancyClass.Unidentified,
        });
    }

    [Fact]
    public void Preview_AReservationIsNotAnOccupiedPoint_AndRemovingEverythingEmptiesThePole()
    {
        var baseline = PoleWith(regulated: 2, beyond: 0);

        Apply(baseline, Add(50, 3, PoleAttachmentOccupantStatus.ActiveReservation) with { OccupantOrganizationId = null, OperatorCnpj = null })
            .Should().BeEquivalentTo(new { OccupiedCount = 2, HasUnidentifiedOccupant = false, Class = OccupancyClass.Low });
        Apply(baseline, OnSlot(OccupancyChangeKind.Remove, 1), OnSlot(OccupancyChangeKind.Remove, 2))
            .Should().BeEquivalentTo(new { OccupiedCount = 0, IsOverCapacity = false, HasUnidentifiedOccupant = false, Class = OccupancyClass.None });
    }

    [Fact]
    public void Preview_AMixedSet_AppliesEveryChange_AndOrdersTheResultAsAPoleDoes()
    {
        var baseline = PoleWith(regulated: 4, beyond: 2);
        var set = Stage(baseline,
            Add(51, slotIndex: null),
            OnSlot(OccupancyChangeKind.Remove, 1),
            Add(50, slotIndex: 6),
            OnEntry(OccupancyChangeKind.MarkUnidentified, 101),
            OnSlot(OccupancyChangeKind.Modify, 2, PoleAttachmentOccupantStatus.AbandonedDeadDrop));

        var preview = OccupancyChangeApplier.Apply(baseline, set);

        preview.Entries.Select(e => e.SlotIndex).Should().Equal(2, 3, 4, 6, null, null, null);
        preview.Entries.Where(e => e.IsBeyondRegulatedSlots).Select(e => e.Id).Should().Equal(Id(51), Id(101), Id(102));
        preview.OccupiedCount.Should().Be(7);
        preview.IsOverCapacity.Should().BeTrue();
        preview.HasUnidentifiedOccupant.Should().BeTrue();
    }

    [Fact]
    public void Preview_NoChanges_IsTheBaselineItself_AndAPoleWithNothingOnFileIsEmpty()
    {
        var baseline = PoleWith(regulated: 3, beyond: 1);

        Apply(baseline).Should().BeEquivalentTo(OccupancyProjection.From(baseline));
        Apply(null).Should().BeEquivalentTo(OccupancyProjection.Empty);
        OccupancyProjection.From(null).Should().BeSameAs(OccupancyProjection.Empty);
    }

    [Fact]
    public void Apply_NeverChangesTheBaseline()
    {
        var baseline = PoleWith(regulated: 3, beyond: 1);
        var before = baseline.Entries.ToList();

        Apply(baseline, OnSlot(OccupancyChangeKind.Remove, 1), Add(50, null), OnEntry(OccupancyChangeKind.MarkUnidentified, 101));

        baseline.Entries.Should().Equal(before);
        baseline.Version.Should().Be(3);
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_NullOrBlankArguments_AndABaselineOfAnotherPole_AreRejected()
    {
        var baseline = PoleWith(regulated: 1, beyond: 0);

        ((Action)(() => OccupancyChangeApplier.Apply(baseline, (OccupancyChangeSet)null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => OccupancyChangeApplier.Apply(baseline, Pole, Owner, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => OccupancyChangeApplier.Apply(baseline, " ", Owner, []))).Should().Throw<ArgumentException>();
        ((Action)(() => OccupancyChangeApplier.Apply(baseline, "pole-2", Owner, []))).Should().Throw<ArgumentException>();
        ((Action)(() => OccupancyChangeApplier.Apply(baseline, Pole, OccupantA, []))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Apply_ChecksEachChangesShape_EvenWithoutAChangeSet()
    {
        var baseline = PoleWith(regulated: 1, beyond: 0);

        ((Action)(() => Apply(baseline, Add(50, slotIndex: 7)))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => Apply(baseline, new OccupancyChange { Kind = OccupancyChangeKind.Remove }))).Should().Throw<ArgumentException>();
        ((Action)(() => Apply(baseline, [null!]))).Should().Throw<ArgumentException>();
    }
}
