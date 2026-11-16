using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Repositories;

/// <summary>
/// Read side of one <see cref="Distribuidora"/>'s lamp wattage chart (XD01-301) — everything the
/// Fiscalização/Ofício point-to-kWh conversion needs.
/// </summary>
public interface IDistribuidoraLampWattageReader
{
    /// <summary>
    /// One page of <paramref name="distribuidoraId"/>'s chart, sorted by
    /// <see cref="DistribuidoraLampWattage.Code"/>, then <see cref="DistribuidoraLampWattage.WattageW"/>,
    /// then <see cref="DistribuidoraLampWattage.Id"/> (deterministic). <paramref name="page"/> is
    /// 1-based; implementations reject <paramref name="page"/> &lt; 1 or
    /// <paramref name="pageSize"/> &lt; 1 with <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    Task<PagedResult<DistribuidoraLampWattage>> GetPageAsync(
        Guid distribuidoraId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>The chart row for exactly <c>(code, wattageW)</c>, or <c>null</c> when
    /// <paramref name="distribuidoraId"/>'s chart has no such row.</summary>
    Task<DistribuidoraLampWattage?> FindAsync(
        Guid distribuidoraId, LampTypeCode code, int wattageW, CancellationToken ct = default);

    /// <summary>
    /// Arithmetic mean of <see cref="DistribuidoraLampWattage.TotalBilledW"/> over every row of
    /// <paramref name="distribuidoraId"/>'s chart (unrounded), or <c>null</c> when the chart has
    /// no rows.
    /// </summary>
    Task<decimal?> GetMeanTotalBilledWAsync(Guid distribuidoraId, CancellationToken ct = default);
}
