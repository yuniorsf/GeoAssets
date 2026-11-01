namespace GeoAssets.Domain.NeutralPole.SlotAllocation;

/// <summary>
/// Which slot positions are occupied, without naming who occupies them — the shape safe to
/// return to any operator sharing the pole. See <see cref="PoleSlotAllocationBoard.GetOccupancySummary"/>'s
/// own doc comment for why this exists as a separate type from <see cref="PoleAttachmentSlot"/>.
/// </summary>
public sealed record SlotOccupancySummary(int SlotIndex, bool IsOccupied);
