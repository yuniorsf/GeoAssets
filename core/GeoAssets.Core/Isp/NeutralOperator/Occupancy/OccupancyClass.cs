namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>
/// The single occupancy state of a pole used for map theming (XD01-314), produced by
/// <see cref="OccupancyClassifier"/>. The numeric values grow with severity, so the most severe
/// of several classes is the greatest one.
///
/// "Occupied" counts every attachment point that physically holds a cable — every
/// <see cref="PoleOccupancyEntry"/> whose status is not
/// <see cref="PoleAttachmentOccupantStatus.ActiveReservation"/> — on a regulated slot or beyond them.
/// </summary>
public enum OccupancyClass
{
    /// <summary>No attachment point is occupied.</summary>
    None = 0,

    /// <summary>1 to 5 occupied points: the pole still has a free regulated slot.</summary>
    Low = 1,

    /// <summary>Exactly <see cref="PoleAttachmentSlotMatrix.MaxSlots"/> (6) occupied points: the
    /// pole is full, with nothing beyond its regulated capacity.</summary>
    High = 2,

    /// <summary>More than <see cref="PoleAttachmentSlotMatrix.MaxSlots"/> occupied points. An
    /// irregular situation in itself.</summary>
    OverCapacity = 3,

    /// <summary>At least one occupied point has no identified occupant organization. The most
    /// severe state: it wins over every count-based class, including
    /// <see cref="OverCapacity"/>.</summary>
    Unidentified = 4,
}
