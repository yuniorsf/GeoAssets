namespace GeoAssets.IpBilling.Calculation;

/// <summary>Outcome of <see cref="InspectionKwhConverter.Convert"/> (XD01-301).</summary>
/// <param name="DeltaKwh"><c>Round(MonthlyKwh + RetroactiveKwh)</c> — the signed figure posted to
/// the Faturamento ledger.</param>
/// <param name="MonthlyKwh">Signed kWh for the competência itself, rounded.</param>
/// <param name="RetroactiveKwh">Signed back-billed kWh over <paramref name="RetroactiveDays"/>, rounded.</param>
/// <param name="IsProvisional"><c>true</c> when at least one point had no lamp detail
/// (<paramref name="ProvisionalPoints"/> &gt; 0).</param>
/// <param name="ExactPoints">Unsigned count of points converted from their real lamp wattage.</param>
/// <param name="ProvisionalPoints">Unsigned count of points converted with
/// <paramref name="StandardWattsPerPoint"/>.</param>
/// <param name="StandardWattsPerPoint">The watts-per-point actually applied to provisional
/// points; <c>null</c> when there were none.</param>
/// <param name="RetroactiveDays">Days of back-billing applied (0 to
/// <see cref="IpEnergyMath.RetroactiveCapDays"/>).</param>
public sealed record InspectionKwhResult(
    decimal DeltaKwh,
    decimal MonthlyKwh,
    decimal RetroactiveKwh,
    bool IsProvisional,
    int ExactPoints,
    int ProvisionalPoints,
    decimal? StandardWattsPerPoint,
    int RetroactiveDays);
