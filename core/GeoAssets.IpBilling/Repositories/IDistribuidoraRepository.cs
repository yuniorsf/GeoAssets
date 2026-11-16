using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Repositories;

/// <summary>
/// Persistence abstraction for <see cref="Distribuidora"/> — admin-managed reference data
/// (XD01-301). Writes are staged; <see cref="SaveChangesAsync"/> commits, same as the other
/// IpBilling repositories.
/// </summary>
public interface IDistribuidoraRepository : IDistribuidoraReader
{
    Task AddAsync(Distribuidora distribuidora, CancellationToken ct = default);

    Task UpdateAsync(Distribuidora distribuidora, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
