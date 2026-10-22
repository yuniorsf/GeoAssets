namespace GeoAssets.IpBilling.Calculation;

/// <summary>
/// Shared public-lighting billing constants — one definition referenced by every later
/// calculation engine (Phases 1-3: Levantamento/Fiscalização/Ofício, XD01-179/182/185)
/// instead of three drifting copies, per this ticket's deliverable description.
/// </summary>
public static class IpEnergyMath
{
    /// <summary>Daily burn time billed for a normal (non-24h) street light: 11h52m.</summary>
    public static readonly TimeSpan NormalLampDailyBurnTime = new(hours: 11, minutes: 52, seconds: 0);

    /// <summary>Daily burn time billed for a 24h-irregular lamp group: 12h08m.</summary>
    public static readonly TimeSpan Irregular24hDailyBurnTime = new(hours: 12, minutes: 8, seconds: 0);

    /// <summary><see cref="NormalLampDailyBurnTime"/> expressed in decimal hours, rounded to
    /// <see cref="RoundingDecimals"/> places (≈ 11.8667).</summary>
    public static decimal NormalLampDailyHours => Round((decimal)NormalLampDailyBurnTime.TotalHours);

    /// <summary><see cref="Irregular24hDailyBurnTime"/> expressed in decimal hours, rounded to
    /// <see cref="RoundingDecimals"/> places (≈ 12.1333).</summary>
    public static decimal Irregular24hDailyHours => Round((decimal)Irregular24hDailyBurnTime.TotalHours);

    /// <summary>
    /// Maximum number of days a retroactive charge may reach back — any billable event older
    /// than this, relative to the reference date, is excluded from a retroactive recalculation.
    /// </summary>
    public const int RetroactiveCapDays = 730;

    /// <summary>Decimal places every IP-billing calculation output is rounded to.</summary>
    public const int RoundingDecimals = 4;

    /// <summary>Rounds <paramref name="value"/> to <see cref="RoundingDecimals"/> decimal places,
    /// away from zero (the convention every later calculation engine in this epic uses).</summary>
    public static decimal Round(decimal value)
        => Math.Round(value, RoundingDecimals, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Whether <paramref name="eventDate"/> still falls within the <see cref="RetroactiveCapDays"/>-day
    /// retroactive window measured back from <paramref name="asOf"/> — inclusive of the boundary
    /// itself (exactly <see cref="RetroactiveCapDays"/> days back is still within the cap; one day
    /// further is not). A future <paramref name="eventDate"/> (after <paramref name="asOf"/>) is
    /// never within the cap.
    /// </summary>
    public static bool IsWithinRetroactiveCap(DateOnly asOf, DateOnly eventDate)
        => eventDate <= asOf && asOf.DayNumber - eventDate.DayNumber <= RetroactiveCapDays;
}
