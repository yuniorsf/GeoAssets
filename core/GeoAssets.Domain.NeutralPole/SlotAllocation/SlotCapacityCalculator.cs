namespace GeoAssets.Domain.NeutralPole.SlotAllocation;

/// <summary>
/// Converts a pole's usable neutral-band height into a physical slot count, at the
/// Decreto 12.068/2024 standard 500mm vertical clearance per attachment.
/// </summary>
public static class SlotCapacityCalculator
{
    public const int StandardBandHeightMillimeters = 500;

    /// <summary>
    /// Whole slots that fit in <paramref name="usableHeightMillimeters"/> at
    /// <paramref name="bandHeightMillimeters"/> (defaults to the decree's 500mm standard) per
    /// slot — always rounds down, since a partial band can't hold a safe attachment.
    /// </summary>
    public static int CalculateMaxSlots(double usableHeightMillimeters, double bandHeightMillimeters = StandardBandHeightMillimeters)
    {
        if (usableHeightMillimeters < 0) throw new ArgumentOutOfRangeException(nameof(usableHeightMillimeters), "Must not be negative.");
        if (bandHeightMillimeters <= 0) throw new ArgumentOutOfRangeException(nameof(bandHeightMillimeters), "Must be positive.");

        return (int)Math.Floor(usableHeightMillimeters / bandHeightMillimeters);
    }
}
