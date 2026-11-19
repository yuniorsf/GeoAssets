using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using Xunit;
using static GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy.OccupancyTestPoles;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy;

/// <summary>
/// <see cref="OccupancyChangeSet"/>: the staging guards OCC-G1..G4 and the life-cycle rules
/// OCC-B5..B8 (XD01-315), as amended for entries beyond the regulated slots.
/// </summary>
public sealed class OccupancyChangeSetTests
{
    private static readonly OccupancyChange[] OneChange = [Add(50, 2)];

    // ── OCC-G1 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "tgt-1", "pole-1", "crew-1")]
    [InlineData(" ", "tgt-1", "pole-1", "crew-1")]
    [InlineData("ord-1", "", "pole-1", "crew-1")]
    [InlineData("ord-1", "\t", "pole-1", "crew-1")]
    [InlineData("ord-1", "tgt-1", "", "crew-1")]
    [InlineData("ord-1", "tgt-1", "  ", "crew-1")]
    [InlineData("ord-1", "tgt-1", "pole-1", "")]
    public void OCC_G1_Stage_BlankOrderTargetPoleOrUser_ThrowsArgumentException(string orderId, string targetId, string pole, string stagedBy)
    {
        var act = () => OccupancyChangeSet.Stage(orderId, targetId, pole, Owner, OneChange, baseline: null, stagedBy, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OCC_G1_Stage_NullOrderTargetOrPole_ThrowsArgumentNullException()
    {
        ((Action)(() => OccupancyChangeSet.Stage(null!, "tgt-1", Pole, Owner, OneChange, null, "crew-1", Now))).Should().Throw<ArgumentNullException>();
        ((Action)(() => OccupancyChangeSet.Stage("ord-1", null!, Pole, Owner, OneChange, null, "crew-1", Now))).Should().Throw<ArgumentNullException>();
        ((Action)(() => OccupancyChangeSet.Stage("ord-1", "tgt-1", null!, Owner, OneChange, null, "crew-1", Now))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OCC_G1_Stage_NullChanges_ThrowsArgumentNullException()
    {
        var act = () => OccupancyChangeSet.Stage("ord-1", "tgt-1", Pole, Owner, null!, null, "crew-1", Now);

        act.Should().Throw<ArgumentNullException>().WithParameterName("changes");
    }

    [Fact]
    public void OCC_G1_Stage_KeysLongerThanAStoreAccepts_ThrowArgumentException()
    {
        var longKey = new string('x', OccupancyChangeSet.MaxOrderKeyLength + 1);

        ((Action)(() => OccupancyChangeSet.Stage(longKey, "tgt-1", Pole, Owner, OneChange, null, "crew-1", Now))).Should().Throw<ArgumentException>();
        ((Action)(() => OccupancyChangeSet.Stage("ord-1", longKey, Pole, Owner, OneChange, null, "crew-1", Now))).Should().Throw<ArgumentException>();
        ((Action)(() => OccupancyChangeSet.Stage("ord-1", "tgt-1", new string('p', 65), Owner, OneChange, null, "crew-1", Now))).Should().Throw<ArgumentException>();
        ((Action)(() => OccupancyChangeSet.Stage("ord-1", "tgt-1", Pole, Owner, OneChange, null, new string('u', 257), Now))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OCC_G1_Stage_BaselineOfAnotherPoleOrAnotherOwner_ThrowsArgumentException()
    {
        var otherPole = PoleOccupancy.Create("pole-2", Owner, [], 1, Now);
        var otherOwner = PoleOccupancy.Create(Pole, OccupantA, [], 1, Now);

        ((Action)(() => Stage(otherPole, OneChange))).Should().Throw<ArgumentException>();
        ((Action)(() => Stage(otherOwner, OneChange))).Should().Throw<ArgumentException>();
    }

    // ── OCC-G2 ────────────────────────────────────────────────────────────────

    [Fact]
    public void OCC_G2_Stage_EmptyChanges_ThrowsArgumentException()
    {
        var act = () => Stage(PoleWith(regulated: 2, beyond: 0));

        act.Should().Throw<ArgumentException>().WithParameterName("changes");
    }

    [Fact]
    public void OCC_G2_Stage_MoreChangesThanASetHolds_ThrowsArgumentException_AndExactlyTheMaximumIsAccepted()
    {
        OccupancyChange[] Adds(int count) => [.. Enumerable.Range(1, count).Select(n => Add(1000 + n, slotIndex: null))];

        ((Action)(() => Stage(null, Adds(OccupancyChangeSet.MaxChanges + 1)))).Should().Throw<ArgumentException>();
        Stage(null, Adds(OccupancyChangeSet.MaxChanges)).Changes.Should().HaveCount(OccupancyChangeSet.MaxChanges);
    }

    // ── OCC-G3 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void OCC_G3_Stage_RegulatedSlotChangeOutsideOneToSix_ThrowsArgumentOutOfRangeException(int slotIndex)
    {
        ((Action)(() => Stage(null, Add(50, slotIndex)))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => Stage(PoleWith(6, 0), OnSlot(OccupancyChangeKind.Remove, slotIndex)))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void OCC_G3_Stage_RegulatedSlotChangeOnTheFirstAndTheLastSlot_IsAccepted(int slotIndex)
    {
        Stage(null, Add(50, slotIndex)).Changes.Should().ContainSingle().Which.SlotIndex.Should().Be(slotIndex);
    }

    [Theory]
    [InlineData(OccupancyChangeKind.Remove)]
    [InlineData(OccupancyChangeKind.Modify)]
    [InlineData(OccupancyChangeKind.MarkUnidentified)]
    public void OCC_G3_Stage_ChangeReferencingAnEntryIdNotInTheBaseline_ThrowsKeyNotFoundException(OccupancyChangeKind kind)
    {
        var baseline = PoleWith(regulated: 2, beyond: 1);
        var change = OnEntry(kind, id: 999, PoleAttachmentOccupantStatus.AuthorizedContracted);

        ((Action)(() => Stage(baseline, change))).Should().Throw<KeyNotFoundException>()
            .Which.Should().BeOfType<OccupancyEntryNotFoundException>().Which.EntryId.Should().Be(Id(999));
        ((Action)(() => Stage(null, change))).Should().Throw<KeyNotFoundException>("a pole with nothing on file holds no entry at all");
    }

    [Fact]
    public void OCC_G3_Stage_OverCapacityAddOnAPoleAlreadyFull_IsAccepted_ItIsAnIrregularityToRecordNotAnInvalidTarget()
    {
        var full = PoleWith(regulated: 6, beyond: 3);

        var set = Stage(full, Add(50, slotIndex: null), Add(51, slotIndex: null));

        set.Changes.Should().HaveCount(2).And.OnlyContain(change => change.AddsBeyondRegulatedSlots);
    }

    [Fact]
    public void OCC_G3_Stage_MalformedChange_ThrowsArgumentException()
    {
        var baseline = PoleWith(regulated: 2, beyond: 1);
        OccupancyChange[] malformed =
        [
            new() { Kind = OccupancyChangeKind.Add, SlotIndex = 3, Status = PoleAttachmentOccupantStatus.AuthorizedContracted },
            new() { Kind = OccupancyChangeKind.Add, SlotIndex = 3, EntryId = Guid.Empty, Status = PoleAttachmentOccupantStatus.AuthorizedContracted },
            new() { Kind = OccupancyChangeKind.Add, SlotIndex = 3, EntryId = Id(50) },
            new() { Kind = OccupancyChangeKind.Modify, SlotIndex = 1 },
            new() { Kind = OccupancyChangeKind.Remove },
            new() { Kind = OccupancyChangeKind.Remove, SlotIndex = 1, EntryId = Id(1) },
            new() { Kind = OccupancyChangeKind.MarkUnidentified },
            Add(1, slotIndex: null),
        ];

        foreach (var change in malformed)
            ((Action)(() => Stage(baseline, change))).Should().Throw<ArgumentException>($"{change} is malformed");

        ((Action)(() => Stage(baseline, [null!]))).Should().Throw<ArgumentException>();
        ((Action)(() => Stage(baseline, new OccupancyChange { Kind = (OccupancyChangeKind)42, SlotIndex = 1 }))).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── OCC-G4 ────────────────────────────────────────────────────────────────

    [Fact]
    public void OCC_G4_Stage_TwoChangesOnTheSameRegulatedSlot_ThrowsArgumentException()
    {
        var baseline = PoleWith(regulated: 3, beyond: 0);

        var act = () => Stage(baseline, OnSlot(OccupancyChangeKind.Remove, 2), OnSlot(OccupancyChangeKind.MarkUnidentified, 2));

        act.Should().Throw<ArgumentException>().WithParameterName("changes");
    }

    [Fact]
    public void OCC_G4_Stage_TwoChangesOnTheSameEntry_ThrowsArgumentException()
    {
        var baseline = PoleWith(regulated: 6, beyond: 2);

        var act = () => Stage(baseline, OnEntry(OccupancyChangeKind.MarkUnidentified, 101), OnEntry(OccupancyChangeKind.Remove, 101));

        act.Should().Throw<ArgumentException>().WithParameterName("changes");
    }

    [Fact]
    public void OCC_G4_Stage_ASlotAndTheEntrySittingOnThatSlot_AreTheSameAddress()
    {
        var baseline = PoleWith(regulated: 3, beyond: 0);

        var act = () => Stage(baseline, OnSlot(OccupancyChangeKind.Remove, 2), OnEntry(OccupancyChangeKind.MarkUnidentified, 2));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OCC_G4_Stage_TwoAddsOnOneSlot_OrWithOneEntryId_ThrowArgumentException_AndRemovePlusAddOnASlotToo()
    {
        var baseline = PoleWith(regulated: 2, beyond: 0);

        ((Action)(() => Stage(baseline, Add(50, 4), Add(51, 4)))).Should().Throw<ArgumentException>();
        ((Action)(() => Stage(baseline, Add(50, null), Add(50, null)))).Should().Throw<ArgumentException>();
        ((Action)(() => Stage(baseline, OnSlot(OccupancyChangeKind.Remove, 1), Add(50, 1)))).Should().Throw<ArgumentException>(
            "one change per slot: replacing an occupant is a Modify");
    }

    [Fact]
    public void OCC_G4_Stage_ChangesOnDifferentSlotsAndEntries_AreAccepted_InTheOrderGiven()
    {
        var baseline = PoleWith(regulated: 3, beyond: 2);
        OccupancyChange[] changes =
        [
            OnEntry(OccupancyChangeKind.Remove, 102),
            OnSlot(OccupancyChangeKind.MarkUnidentified, 1),
            Add(50, 5),
            Add(51, null),
            OnSlot(OccupancyChangeKind.Modify, 2, PoleAttachmentOccupantStatus.AbandonedDeadDrop),
            OnEntry(OccupancyChangeKind.MarkUnidentified, 101),
        ];

        Stage(baseline, changes).Changes.Should().Equal(changes);
    }

    // ── OCC-B5 / OCC-B6 ───────────────────────────────────────────────────────

    [Fact]
    public void OCC_B5_Restage_ReturnsANewStagedSetForTheSameTarget_WithTheNewChangesAndTheCurrentBaseline()
    {
        var first = Stage(PoleWith(regulated: 2, beyond: 0, version: 3), Add(50, 4));
        var moved = PoleWith(regulated: 3, beyond: 0, version: 4);

        var second = first.Restage([OnSlot(OccupancyChangeKind.Remove, 3)], moved, "crew-2", Now.AddHours(1));

        second.Id.Should().NotBe(first.Id);
        second.Should().BeEquivalentTo(new
        {
            first.OrderId, first.TargetId, first.PoleFeatureId, first.PoleOwnerOrganizationId,
            Status = OccupancyChangeSetStatus.Staged, BaselineVersion = 4, StagedBy = "crew-2", StagedAt = Now.AddHours(1),
        });
        second.Changes.Should().ContainSingle().Which.Kind.Should().Be(OccupancyChangeKind.Remove);
        first.Changes.Should().ContainSingle().Which.Kind.Should().Be(OccupancyChangeKind.Add, "the replaced set itself is not altered");
    }

    [Theory]
    [InlineData(OccupancyChangeSetStatus.Committed)]
    [InlineData(OccupancyChangeSetStatus.Discarded)]
    public void OCC_B6_Restage_OnACommittedOrDiscardedSet_ThrowsInvalidOperationException(OccupancyChangeSetStatus status)
    {
        var closed = Restored(status);

        var act = () => closed.Restage(OneChange, null, "crew-1", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OCC_B6_Restage_StillChecksTheNewChanges()
    {
        var staged = Stage(null, Add(50, 1));

        ((Action)(() => staged.Restage([], null, "crew-1", Now))).Should().Throw<ArgumentException>();
        ((Action)(() => staged.Restage([Add(51, 9)], null, "crew-1", Now))).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── OCC-B7 ────────────────────────────────────────────────────────────────

    [Fact]
    public void OCC_B7_Discard_OnAStagedSet_GivesADiscardedSetClosedAtTheGivenInstant_WithEverythingElseKept()
    {
        var staged = Stage(PoleWith(2, 0), Add(50, 4));

        var discarded = staged.Discard(Now.AddMinutes(5));

        discarded.Status.Should().Be(OccupancyChangeSetStatus.Discarded);
        discarded.ClosedAt.Should().Be(Now.AddMinutes(5));
        discarded.Should().BeEquivalentTo(staged, options => options.Excluding(s => s.Status).Excluding(s => s.ClosedAt));
        staged.Status.Should().Be(OccupancyChangeSetStatus.Staged);
        staged.ClosedAt.Should().BeNull();
    }

    [Fact]
    public void OCC_B7_Discard_OnAnAlreadyDiscardedSet_IsANoOp_KeepingTheFirstClosingInstant()
    {
        var discarded = Stage(null, Add(50, 4)).Discard(Now.AddMinutes(5));

        discarded.Discard(Now.AddDays(1)).Should().BeSameAs(discarded);
    }

    [Fact]
    public void OCC_B7_Discard_OnACommittedSet_ThrowsInvalidOperationException()
    {
        var act = () => Restored(OccupancyChangeSetStatus.Committed).Discard(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Commit (XD01-318) ─────────────────────────────────────────────────────

    [Fact]
    public void Commit_OnAStagedSet_GivesACommittedSetClosedAtTheGivenInstant_WithEverythingElseKept()
    {
        var staged = Stage(PoleWith(2, 0), Add(50, 4));

        var committed = staged.Commit(Now.AddMinutes(9));

        committed.Status.Should().Be(OccupancyChangeSetStatus.Committed);
        committed.ClosedAt.Should().Be(Now.AddMinutes(9));
        committed.Should().BeEquivalentTo(staged, options => options.Excluding(s => s.Status).Excluding(s => s.ClosedAt));
        staged.Status.Should().Be(OccupancyChangeSetStatus.Staged, "the staged set itself is unchanged");
    }

    [Theory]
    [InlineData(OccupancyChangeSetStatus.Committed)]
    [InlineData(OccupancyChangeSetStatus.Discarded)]
    public void Commit_OnASetThatIsNoLongerStaged_ThrowsInvalidOperationException(OccupancyChangeSetStatus status)
    {
        var act = () => Restored(status).Commit(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── OCC-B8 ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public void OCC_B8_Stage_StoresThePolesOccupancyVersionAsTheBaseline(int version)
    {
        var set = Stage(PoleWith(regulated: 1, beyond: 0, version), Add(50, 4));

        set.BaselineVersion.Should().Be(version);
        set.IsStaleAgainst(version).Should().BeFalse();
        set.IsStaleAgainst(version + 1).Should().BeTrue();
    }

    [Fact]
    public void OCC_B8_Stage_ForAPoleWithNothingOnFile_StoresVersionZero()
    {
        var set = Stage(baseline: null, Add(50, 4));

        set.BaselineVersion.Should().Be(PoleOccupancy.UnsavedVersion);
        set.IsStaleAgainst(PoleOccupancy.UnsavedVersion).Should().BeFalse();
        set.IsStaleAgainst(PoleOccupancy.FirstVersion).Should().BeTrue("the pole got its first occupancy after staging");
    }

    [Fact]
    public void Stage_ReturnsAStagedSetCarryingWhatItWasGiven_WithAFreshId()
    {
        var first = Stage(null, Add(50, 4));
        var second = Stage(null, Add(50, 4));

        first.Should().BeEquivalentTo(new
        {
            OrderId = "ord-1", TargetId = "tgt-1", PoleFeatureId = Pole, PoleOwnerOrganizationId = Owner,
            Status = OccupancyChangeSetStatus.Staged, StagedBy = "crew-1", StagedAt = Now, ClosedAt = (DateTimeOffset?)null,
        });
        first.Id.Should().NotBe(Guid.Empty).And.NotBe(second.Id);
    }

    // ── Restore ───────────────────────────────────────────────────────────────

    [Fact]
    public void Restore_RebuildsASetInAnyStatus_WithoutCheckingItsChangesAgainstAPole()
    {
        var id = Guid.NewGuid();
        OccupancyChange[] changes = [OnEntry(OccupancyChangeKind.Remove, 999)];

        var restored = OccupancyChangeSet.Restore(
            id, "ord-1", "tgt-1", Pole, Owner, changes, 5, OccupancyChangeSetStatus.Committed, "crew-1", Now, Now.AddHours(2));

        restored.Should().BeEquivalentTo(new
        {
            Id = id, BaselineVersion = 5, Status = OccupancyChangeSetStatus.Committed, ClosedAt = (DateTimeOffset?)Now.AddHours(2),
        });
        restored.Changes.Should().Equal(changes);
    }

    [Fact]
    public void Restore_RejectsWhatNoSetCanBe()
    {
        Action Restoring(
            Guid? id = null, int baselineVersion = 1, OccupancyChangeSetStatus status = OccupancyChangeSetStatus.Staged,
            DateTimeOffset? closedAt = null, IReadOnlyCollection<OccupancyChange>? changes = null) =>
            () => OccupancyChangeSet.Restore(
                id ?? Guid.NewGuid(), "ord-1", "tgt-1", Pole, Owner, changes ?? OneChange, baselineVersion, status, "crew-1", Now, closedAt);

        Restoring(id: Guid.Empty).Should().Throw<ArgumentException>();
        Restoring(baselineVersion: -1).Should().Throw<ArgumentOutOfRangeException>();
        Restoring(status: (OccupancyChangeSetStatus)9).Should().Throw<ArgumentOutOfRangeException>();
        Restoring(status: OccupancyChangeSetStatus.Staged, closedAt: Now).Should().Throw<ArgumentException>();
        Restoring(status: OccupancyChangeSetStatus.Discarded, closedAt: null).Should().Throw<ArgumentException>();
        Restoring(changes: []).Should().Throw<ArgumentException>();
        Restoring(changes: [Add(50, 1), OnSlot(OccupancyChangeKind.Remove, 1)]).Should().Throw<ArgumentException>();
        Restoring(changes: [Add(50, 8)]).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => OccupancyChangeSet.Restore(Guid.NewGuid(), "ord-1", "tgt-1", Pole, Owner, null!, 1, OccupancyChangeSetStatus.Staged, "crew-1", Now, null)))
            .Should().Throw<ArgumentNullException>();
    }

    private static OccupancyChangeSet Restored(OccupancyChangeSetStatus status) => OccupancyChangeSet.Restore(
        Guid.NewGuid(), "ord-1", "tgt-1", Pole, Owner, OneChange, 1, status, "crew-1", Now,
        status == OccupancyChangeSetStatus.Staged ? null : Now.AddHours(1));
}
