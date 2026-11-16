using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Repositories;

/// <summary>
/// Persistence abstraction for a <see cref="Distribuidora"/>'s lamp wattage chart (XD01-301).
/// Writes are staged; <see cref="SaveChangesAsync"/> commits.
/// </summary>
public interface IDistribuidoraLampWattageRepository : IDistribuidoraLampWattageReader
{
    /// <summary>
    /// Inserts <paramref name="row"/>, or — when a row with the same
    /// <c>(DistribuidoraId, Code, WattageW)</c> already exists — updates that row's
    /// <see cref="DistribuidoraLampWattage.BallastLossW"/> in place (keeping its existing
    /// <see cref="DistribuidoraLampWattage.Id"/>). Returns the stored row.
    /// </summary>
    Task<DistribuidoraLampWattage> UpsertAsync(DistribuidoraLampWattage row, CancellationToken ct = default);

    /// <summary>Removes the chart row with this id; a no-op when it does not exist.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
