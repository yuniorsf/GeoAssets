namespace GeoAssets.Domain.NeutralPole.SlotAllocation;

public enum SlotAllocationOutcome
{
    Allocated,

    /// <summary>The operator already holds a slot on this pole — allocation is idempotent, not additive.</summary>
    AlreadyAllocated,

    /// <summary>Every slot up to the pole's capacity is occupied by a different operator.</summary>
    PoleFull,
}
