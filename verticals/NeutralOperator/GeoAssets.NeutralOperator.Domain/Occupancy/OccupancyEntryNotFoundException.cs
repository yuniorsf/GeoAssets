namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>
/// Thrown when an <see cref="OccupancyChange"/> addresses an entry the pole's occupancy does not
/// hold: a regulated slot with nothing on it, or an entry id that is not on the pole (XD01-315).
/// A <see cref="KeyNotFoundException"/>, so code that only knows "the thing named is not there"
/// keeps working.
/// </summary>
public sealed class OccupancyEntryNotFoundException : KeyNotFoundException
{
    /// <summary>The regulated slot that holds no entry, when the change addressed a slot.</summary>
    public int? SlotIndex { get; }

    /// <summary>The entry id that is not on the pole, when the change addressed an entry.</summary>
    public Guid? EntryId { get; }

    public OccupancyEntryNotFoundException(string poleFeatureId, int slotIndex)
        : base($"Pole '{poleFeatureId}' has no entry on regulated slot {slotIndex}.")
    {
        SlotIndex = slotIndex;
    }

    public OccupancyEntryNotFoundException(string poleFeatureId, Guid entryId)
        : base($"Pole '{poleFeatureId}' has no entry '{entryId}'.")
    {
        EntryId = entryId;
    }

    public OccupancyEntryNotFoundException()
    {
    }

    public OccupancyEntryNotFoundException(string message) : base(message)
    {
    }

    public OccupancyEntryNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
