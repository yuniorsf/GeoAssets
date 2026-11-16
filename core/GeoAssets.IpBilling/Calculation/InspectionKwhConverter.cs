namespace GeoAssets.IpBilling.Calculation;

/// <summary>
/// Pure point-count-to-kWh conversion for a validated Fiscalização/Ofício (XD01-301) — the rule
/// whose absence blocked Faturamento ledger posting in XD01-188.
///
/// <para><b>Per entry</b> (<see cref="InspectionPointEntry"/>), using <see cref="IpEnergyMath.Kwh"/>:
/// <c>kWh = Sign × Quantity × totalBilledW × dailyBurnHours × days ÷ 1000</c>.</para>
/// <list type="bullet">
/// <item><c>totalBilledW</c> — an entry WITH lamp detail uses the chart's billed watts (wattage +
/// ballast loss) for its exact <c>(lamp code, wattage)</c> and counts as <em>exact</em>; an entry
/// WITHOUT lamp detail uses <see cref="InspectionLampChart.StandardWattsPerPoint"/> (the applicable
/// chart's mean billed watts) and counts as <em>provisional</em>.</item>
/// <item><c>dailyBurnHours</c> — <see cref="IpEnergyMath.Irregular24hDailyHours"/> for a
/// 24h-irregular operation group, otherwise <see cref="IpEnergyMath.NormalLampDailyHours"/>.</item>
/// </list>
///
/// <para><b>Monthly kWh</b> sums every entry over <c>daysInCompetencia</c> — the kWh equivalent
/// of the line's <c>Agregado</c>. Signed; never clamped, like <c>Agregado</c> itself.</para>
///
/// <para><b>Retroactive kWh</b> sums only entries with
/// <see cref="InspectionPointEntry.CountsTowardRetroativo"/> — the kWh equivalent of the line's
/// <c>Retroativo</c> — over <c>retroactiveDays</c>, with the same wattage and burn hours as the
/// monthly figure. Signed and never floored at zero: a net removal back-credits, mirroring the
/// signed <c>Retroativo</c> count (unlike <c>Levantamento.LevantamentoRetroactiveCalculator</c>,
/// whose zero floor only guards its [FIX] exclusion subtraction, which does not exist here).</para>
///
/// <para><b>Rounding</b>: entries accumulate unrounded; <c>MonthlyKwh</c> and
/// <c>RetroactiveKwh</c> are each rounded once with <see cref="IpEnergyMath.Round"/>, and
/// <c>DeltaKwh = Round(MonthlyKwh + RetroactiveKwh)</c>.</para>
/// </summary>
public static class InspectionKwhConverter
{
    /// <summary>
    /// Converts <paramref name="entries"/> to kWh. See the type's own doc comment for the rule.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="daysInCompetencia"/> is not
    /// positive, or <paramref name="retroactiveDays"/> is negative or exceeds
    /// <see cref="IpEnergyMath.RetroactiveCapDays"/>.</exception>
    /// <exception cref="ArgumentException">An entry has a non-positive quantity, a sign other than
    /// ±1, or only one of lamp code/wattage.</exception>
    /// <exception cref="InspectionLampNotInChartException">An entry's lamp detail is not in
    /// <paramref name="chart"/>.</exception>
    /// <exception cref="InspectionStandardWattageUnavailableException">An entry has no lamp detail
    /// and <paramref name="chart"/> has no standard watts-per-point value.</exception>
    public static InspectionKwhResult Convert(
        IReadOnlyList<InspectionPointEntry> entries,
        InspectionLampChart chart,
        int daysInCompetencia,
        int retroactiveDays)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(daysInCompetencia);
        ArgumentOutOfRangeException.ThrowIfNegative(retroactiveDays);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retroactiveDays, IpEnergyMath.RetroactiveCapDays);

        var monthly = 0m;
        var retroactive = 0m;
        var exactPoints = 0;
        var provisionalPoints = 0;

        foreach (var entry in entries)
        {
            EnsureWellFormed(entry);

            decimal totalBilledW;
            if (entry.LampCode is { } code && entry.WattageW is { } wattageW)
            {
                if (!chart.TryGetTotalBilledW(code, wattageW, out totalBilledW))
                {
                    throw new InspectionLampNotInChartException(code, wattageW);
                }

                exactPoints += entry.Quantity;
            }
            else
            {
                totalBilledW = chart.StandardWattsPerPoint
                    ?? throw new InspectionStandardWattageUnavailableException();
                provisionalPoints += entry.Quantity;
            }

            var dailyHours = entry.IsIrregular24h
                ? IpEnergyMath.Irregular24hDailyHours
                : IpEnergyMath.NormalLampDailyHours;

            monthly += entry.Sign * IpEnergyMath.Kwh(entry.Quantity, totalBilledW, dailyHours, daysInCompetencia);
            if (entry.CountsTowardRetroativo)
            {
                retroactive += entry.Sign * IpEnergyMath.Kwh(entry.Quantity, totalBilledW, dailyHours, retroactiveDays);
            }
        }

        var monthlyKwh = IpEnergyMath.Round(monthly);
        var retroactiveKwh = IpEnergyMath.Round(retroactive);

        return new InspectionKwhResult(
            DeltaKwh: IpEnergyMath.Round(monthlyKwh + retroactiveKwh),
            MonthlyKwh: monthlyKwh,
            RetroactiveKwh: retroactiveKwh,
            IsProvisional: provisionalPoints > 0,
            ExactPoints: exactPoints,
            ProvisionalPoints: provisionalPoints,
            StandardWattsPerPoint: provisionalPoints > 0 ? chart.StandardWattsPerPoint : null,
            RetroactiveDays: retroactiveDays);
    }

    /// <summary>
    /// Days of back-billing for the window <c>[anchor, asOf)</c>, capped at
    /// <see cref="IpEnergyMath.RetroactiveCapDays"/> with the same boundary semantics as
    /// <see cref="IpEnergyMath.IsWithinRetroactiveCap"/> (exactly 730 days back is within the cap;
    /// anything older is cut to 730). <c>0</c> when there is no <paramref name="anchor"/> (nothing
    /// to retroact against) or the anchor is not before <paramref name="asOf"/>.
    /// </summary>
    public static int RetroactiveDays(DateOnly? anchor, DateOnly asOf)
    {
        if (anchor is not { } start || start >= asOf)
        {
            return 0;
        }

        return IpEnergyMath.IsWithinRetroactiveCap(asOf, start)
            ? asOf.DayNumber - start.DayNumber
            : IpEnergyMath.RetroactiveCapDays;
    }

    private static void EnsureWellFormed(InspectionPointEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Quantity <= 0)
        {
            throw new ArgumentException($"Entry quantity must be positive, was {entry.Quantity}.", nameof(entry));
        }

        if (entry.Sign is not (1 or -1))
        {
            throw new ArgumentException($"Entry sign must be +1 or -1, was {entry.Sign}.", nameof(entry));
        }

        if (entry.LampCode is null != entry.WattageW is null)
        {
            throw new ArgumentException("Entry lamp code and wattage must be both present or both absent.", nameof(entry));
        }
    }
}
