namespace GeoAssets.Domain.NeutralPole.SlotAllocation;

public sealed record SlotAllocationResult(SlotAllocationOutcome Outcome, int? SlotIndex)
{
    public bool Succeeded => Outcome is SlotAllocationOutcome.Allocated or SlotAllocationOutcome.AlreadyAllocated;
}
