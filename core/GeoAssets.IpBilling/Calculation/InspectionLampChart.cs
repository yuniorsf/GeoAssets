using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Calculation;

/// <summary>
/// The already-resolved wattage data one Fiscalização/Ofício conversion needs — a pure value so
/// <see cref="InspectionKwhConverter"/> stays free of I/O (XD01-301). Built by
/// <see cref="InspectionLedgerPoster"/> from the município's Distribuidora chart with the global
/// standard chart as fallback.
/// </summary>
public sealed class InspectionLampChart
{
    private readonly IReadOnlyDictionary<(LampTypeCode Code, int WattageW), decimal> _totalBilledW;

    /// <param name="totalBilledW">Billed watts (wattage + ballast loss) per resolvable
    /// <c>(lamp code, wattage)</c> pair.</param>
    /// <param name="standardWattsPerPoint">Mean billed watts of the applicable chart, used for
    /// points without lamp detail; <c>null</c> when no chart has any row.</param>
    public InspectionLampChart(
        IReadOnlyDictionary<(LampTypeCode Code, int WattageW), decimal> totalBilledW,
        decimal? standardWattsPerPoint)
    {
        ArgumentNullException.ThrowIfNull(totalBilledW);
        _totalBilledW = totalBilledW;
        StandardWattsPerPoint = standardWattsPerPoint;
    }

    public decimal? StandardWattsPerPoint { get; }

    public bool TryGetTotalBilledW(LampTypeCode code, int wattageW, out decimal totalBilledW) =>
        _totalBilledW.TryGetValue((code, wattageW), out totalBilledW);
}
