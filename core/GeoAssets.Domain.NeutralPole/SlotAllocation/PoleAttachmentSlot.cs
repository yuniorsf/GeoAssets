namespace GeoAssets.Domain.NeutralPole.SlotAllocation;

/// <summary>One numbered attachment position on a pole's neutral band, and who (if anyone) currently occupies it.</summary>
public sealed record PoleAttachmentSlot(int SlotIndex, string? OperatorId)
{
    public bool IsOccupied => OperatorId is not null;
}
