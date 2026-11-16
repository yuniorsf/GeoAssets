using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Calculation;

/// <summary>
/// One homogeneous group of public-lighting points from a Fiscalização/Ofício line, normalized
/// so <see cref="InspectionKwhConverter"/> needs no knowledge of either flow's operation-group
/// enum (XD01-301).
/// </summary>
/// <param name="Quantity">Number of points — always positive; direction lives in <paramref name="Sign"/>.</param>
/// <param name="Sign"><c>+1</c> when the operation group adds billable load, <c>-1</c> when it removes it.</param>
/// <param name="IsIrregular24h"><c>true</c> for the 24h-irregular operation groups (billed at
/// <see cref="IpEnergyMath.Irregular24hDailyHours"/>), <c>false</c> for normal-lamp groups
/// (<see cref="IpEnergyMath.NormalLampDailyHours"/>).</param>
/// <param name="CountsTowardRetroativo">Whether the operation group is part of the line's
/// <c>Retroativo</c> formula, i.e. whether these points are also back-billed.</param>
/// <param name="LampCode">Lamp type, when the line carries lamp detail for these points; <c>null</c>
/// (together with <paramref name="WattageW"/>) for points known only as a count.</param>
/// <param name="WattageW">Nominal lamp wattage in watts; non-null exactly when <paramref name="LampCode"/> is.</param>
public sealed record InspectionPointEntry(
    int Quantity,
    int Sign,
    bool IsIrregular24h,
    bool CountsTowardRetroativo,
    LampTypeCode? LampCode = null,
    int? WattageW = null)
{
    /// <summary><c>true</c> when these points carry lamp detail and convert exactly.</summary>
    public bool HasLampDetail => LampCode is not null && WattageW is not null;
}
