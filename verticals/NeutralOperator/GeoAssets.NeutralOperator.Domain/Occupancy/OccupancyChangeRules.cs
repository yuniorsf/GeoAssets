namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>
/// The checks one <see cref="OccupancyChange"/> must pass on its own, whatever the pole looks
/// like — shared by <see cref="OccupancyChangeSet"/> (when staging) and
/// <see cref="OccupancyChangeApplier"/> (when applying).
/// </summary>
internal static class OccupancyChangeRules
{
    /// <summary>Throws unless <paramref name="change"/> is well formed.</summary>
    /// <exception cref="ArgumentException">
    /// The change is null; an Add has no (or an empty) entry id; another kind names both a slot
    /// and an entry, or neither, or an empty entry id; an Add or Modify has no status.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The kind is not a defined value; a slot index is outside
    /// 1..<see cref="PoleAttachmentSlotMatrix.MaxSlots"/> (OCC-G3).
    /// </exception>
    public static void CheckShape(OccupancyChange? change)
    {
        if (change is null)
            throw new ArgumentException("A change is null.", nameof(change));

        if (!Enum.IsDefined(change.Kind))
            throw new ArgumentOutOfRangeException(nameof(change), change.Kind, "Unknown occupancy change kind.");

        if (change.SlotIndex is < 1 or > PoleAttachmentSlotMatrix.MaxSlots)
            throw new ArgumentOutOfRangeException(
                nameof(change), change.SlotIndex,
                $"A regulated slot index must be between 1 and {PoleAttachmentSlotMatrix.MaxSlots}; an entry beyond the regulated slots has none.");

        if (change.EntryId == Guid.Empty)
            throw new ArgumentException("A change carries an empty entry id.", nameof(change));

        if (change.Kind == OccupancyChangeKind.Add)
        {
            if (change.EntryId is null)
                throw new ArgumentException("An Add must carry the id of the entry it creates.", nameof(change));
        }
        else if (change.SlotIndex is null == change.EntryId is null)
        {
            throw new ArgumentException(
                $"A {change.Kind} must address exactly one of a regulated slot or an entry id.", nameof(change));
        }

        if (change.Kind is OccupancyChangeKind.Add or OccupancyChangeKind.Modify && change.Status is null)
            throw new ArgumentException($"A {change.Kind} must state the entry's status.", nameof(change));
    }
}
