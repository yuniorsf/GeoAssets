namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>
/// What a pole's occupancy looks like at one point: its entries and the figures derived from
/// them (XD01-315). It is the result of <see cref="OccupancyChangeApplier.Apply(PoleOccupancy?, OccupancyChangeSet)"/>
/// — "the pole as it will be once these changes are committed" — and, through <see cref="From"/>,
/// the same picture of a pole's live occupancy, so the two can be shown side by side.
/// </summary>
public sealed record OccupancyProjection
{
    /// <summary>
    /// Every entry: regulated ones by slot index, then over-capacity ones by the ordinal order
    /// of their id's canonical text — <see cref="PoleOccupancy.Entries"/>' order.
    /// </summary>
    public required IReadOnlyList<PoleOccupancyEntry> Entries { get; init; }

    /// <summary>How many attachment points physically hold a cable; reservations are not counted.</summary>
    public required int OccupiedCount { get; init; }

    /// <summary>More than <see cref="PoleAttachmentSlotMatrix.MaxSlots"/> occupied points. Recorded, never refused.</summary>
    public required bool IsOverCapacity { get; init; }

    /// <summary>At least one occupied point has no occupant organization on file.</summary>
    public required bool HasUnidentifiedOccupant { get; init; }

    /// <summary>The most severe state, as <see cref="OccupancyClassifier"/> ranks them.</summary>
    public required OccupancyClass Class { get; init; }

    /// <summary>True when the pole is in any irregular situation.</summary>
    public bool IsIrregular => IsOverCapacity || HasUnidentifiedOccupant;

    /// <summary>A pole with nothing on it.</summary>
    public static OccupancyProjection Empty { get; } = new()
    {
        Entries = [],
        OccupiedCount = 0,
        IsOverCapacity = false,
        HasUnidentifiedOccupant = false,
        Class = OccupancyClass.None,
    };

    /// <summary>The picture of a stored occupancy; <see cref="Empty"/> for a pole with none on file.</summary>
    public static OccupancyProjection From(PoleOccupancy? occupancy) => occupancy is null
        ? Empty
        : new OccupancyProjection
        {
            Entries = occupancy.Entries,
            OccupiedCount = occupancy.OccupiedCount,
            IsOverCapacity = occupancy.IsOverCapacity,
            HasUnidentifiedOccupant = occupancy.HasUnidentifiedOccupant,
            Class = occupancy.Class,
        };
}
