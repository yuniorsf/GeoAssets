namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>
/// Pure classification of a pole's occupancy into one <see cref="OccupancyClass"/> (XD01-314).
/// The two irregularities stay available as separate flags on <see cref="PoleOccupancy"/>
/// (<see cref="PoleOccupancy.HasUnidentifiedOccupant"/>, <see cref="PoleOccupancy.IsOverCapacity"/>);
/// the class is only the most severe state, in this fixed order:
/// <list type="number">
///   <item><description><see cref="OccupancyClass.Unidentified"/> — any unidentified occupant.</description></item>
///   <item><description><see cref="OccupancyClass.OverCapacity"/> — more than 6 occupied points.</description></item>
///   <item><description><see cref="OccupancyClass.High"/> — exactly 6 (full).</description></item>
///   <item><description><see cref="OccupancyClass.Low"/> — 1 to 5.</description></item>
///   <item><description><see cref="OccupancyClass.None"/> — 0.</description></item>
/// </list>
/// </summary>
public static class OccupancyClassifier
{
    /// <summary>Classifies a pole from its stored occupancy.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="occupancy"/> is null.</exception>
    public static OccupancyClass Classify(PoleOccupancy occupancy)
    {
        ArgumentNullException.ThrowIfNull(occupancy);
        return Classify(occupancy.OccupiedCount, occupancy.HasUnidentifiedOccupant);
    }

    /// <summary>
    /// Classifies a pole from its number of occupied attachment points and whether any of them
    /// has no identified occupant organization. A pole with no occupancy on file is
    /// <c>Classify(0, false)</c>, i.e. <see cref="OccupancyClass.None"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="occupiedCount"/> is negative.</exception>
    public static OccupancyClass Classify(int occupiedCount, bool hasUnidentifiedOccupant)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(occupiedCount);

        if (hasUnidentifiedOccupant)
            return OccupancyClass.Unidentified;

        return occupiedCount switch
        {
            0 => OccupancyClass.None,
            < PoleAttachmentSlotMatrix.MaxSlots => OccupancyClass.Low,
            PoleAttachmentSlotMatrix.MaxSlots => OccupancyClass.High,
            _ => OccupancyClass.OverCapacity,
        };
    }
}
