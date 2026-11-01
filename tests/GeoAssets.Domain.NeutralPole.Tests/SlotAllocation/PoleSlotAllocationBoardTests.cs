using FluentAssertions;
using GeoAssets.Domain.NeutralPole.SlotAllocation;
using Xunit;

namespace GeoAssets.Domain.NeutralPole.Tests.SlotAllocation;

public class PoleSlotAllocationBoardTests
{
    [Fact]
    public void Allocate_UnderCapacity_SucceedsAndAssignsLowestFreeSlot()
    {
        var board = new PoleSlotAllocationBoard(maxSlots: 3);

        var result = board.Allocate("isp-a");

        result.Outcome.Should().Be(SlotAllocationOutcome.Allocated);
        result.SlotIndex.Should().Be(1);
        board.OccupiedCount.Should().Be(1);
        board.AvailableCount.Should().Be(2);
    }

    [Fact]
    public void Allocate_SameOperatorTwice_IsIdempotentAndReturnsTheSameSlot()
    {
        var board = new PoleSlotAllocationBoard(maxSlots: 3);
        var first = board.Allocate("isp-a");

        var second = board.Allocate("isp-a");

        second.Outcome.Should().Be(SlotAllocationOutcome.AlreadyAllocated);
        second.SlotIndex.Should().Be(first.SlotIndex);
        board.OccupiedCount.Should().Be(1, "allocating the same operator again must not consume a second slot");
    }

    [Fact]
    public void Allocate_WhenAtCapacity_FailsWithPoleFull()
    {
        var board = new PoleSlotAllocationBoard(maxSlots: 2);
        board.Allocate("isp-a");
        board.Allocate("isp-b");

        var result = board.Allocate("isp-c");

        result.Outcome.Should().Be(SlotAllocationOutcome.PoleFull);
        result.SlotIndex.Should().BeNull();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Allocate_DefaultCapacity_IsSix()
    {
        var board = new PoleSlotAllocationBoard();

        board.MaxSlots.Should().Be(6, "Decreto 12.068/2024's stated range is 5-6 operators; 6 is this type's default, callers override to 5 per pole class");
    }

    [Fact]
    public void Release_FreesTheSlotForReallocation()
    {
        var board = new PoleSlotAllocationBoard(maxSlots: 1);
        board.Allocate("isp-a");

        var released = board.Release("isp-a");
        var reallocated = board.Allocate("isp-b");

        released.Should().BeTrue();
        reallocated.Outcome.Should().Be(SlotAllocationOutcome.Allocated);
    }

    [Fact]
    public void Release_OperatorWithNoSlot_ReturnsFalse()
    {
        var board = new PoleSlotAllocationBoard();

        board.Release("isp-a").Should().BeFalse();
    }

    [Fact]
    public void Constructor_ZeroOrNegativeMaxSlots_Throws()
    {
        var act = () => new PoleSlotAllocationBoard(maxSlots: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetOccupancySummary_ReportsOccupancyWithoutNamingTheOccupant()
    {
        // SlotOccupancySummary has no OperatorId property at all (unlike PoleAttachmentSlot) —
        // a competing operator reading this can see slot 1 is occupied, never by whom.
        var board = new PoleSlotAllocationBoard(maxSlots: 3);
        board.Allocate("competitor-isp");

        var summary = board.GetOccupancySummary();

        summary.Should().HaveCount(3);
        summary.Single(s => s.SlotIndex == 1).IsOccupied.Should().BeTrue();
        summary.Where(s => s.SlotIndex != 1).Should().OnlyContain(s => !s.IsOccupied);
    }

    [Fact]
    public void GetAllocations_ExposesFullOperatorDetail_ForAdminUseOnly()
    {
        var board = new PoleSlotAllocationBoard(maxSlots: 2);
        board.Allocate("isp-a");

        var allocations = board.GetAllocations();

        allocations.Single(a => a.SlotIndex == 1).OperatorId.Should().Be("isp-a");
        allocations.Single(a => a.SlotIndex == 2).OperatorId.Should().BeNull();
    }
}
