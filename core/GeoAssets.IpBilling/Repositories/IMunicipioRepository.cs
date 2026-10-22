using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Repositories;

/// <summary>
/// Persistence abstraction for <see cref="Municipio"/> — admin-managed reference data
/// (XD01-177 adds the REST API and grant-provisioning wiring on top of this).
/// </summary>
public interface IMunicipioRepository
{
    Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default);

    Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(Municipio municipio, CancellationToken ct = default);

    Task UpdateAsync(Municipio municipio, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
