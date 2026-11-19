namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>What one <see cref="OccupancyChange"/> does to a pole's occupancy (XD01-315).</summary>
public enum OccupancyChangeKind
{
    /// <summary>
    /// A new entry: on the free regulated slot named by <see cref="OccupancyChange.SlotIndex"/>,
    /// or — without a slot index — beyond the regulated slots (an over-capacity entry).
    /// </summary>
    Add = 0,

    /// <summary>The addressed entry is taken off the pole.</summary>
    Remove = 1,

    /// <summary>The addressed entry keeps its id and position and takes the change's payload.</summary>
    Modify = 2,

    /// <summary>
    /// The addressed entry loses its occupant organization and CNPJ and becomes
    /// <see cref="PoleAttachmentOccupantStatus.ClandestineUnauthorized"/>: a cable is there and
    /// nobody is on file for it.
    /// </summary>
    MarkUnidentified = 3,
}
