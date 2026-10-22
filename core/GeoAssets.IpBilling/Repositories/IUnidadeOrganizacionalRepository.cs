using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Repositories;

/// <summary>Persistence abstraction for <see cref="UnidadeOrganizacional"/> reference rows.</summary>
public interface IUnidadeOrganizacionalRepository
{
    Task<UnidadeOrganizacional?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<UnidadeOrganizacional>> GetByMunicipioAsync(Guid municipioId, CancellationToken ct = default);

    Task AddAsync(UnidadeOrganizacional unidade, CancellationToken ct = default);

    Task UpdateAsync(UnidadeOrganizacional unidade, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
