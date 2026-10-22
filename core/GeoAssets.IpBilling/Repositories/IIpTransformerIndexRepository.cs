using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Repositories;

/// <summary>Persistence abstraction for <see cref="IpTransformerIndex"/> lookup rows.</summary>
public interface IIpTransformerIndexRepository
{
    Task<IpTransformerIndex?> GetAsync(
        string municipioIbgeCode, string codigoInstalacao, CancellationToken ct = default);

    Task<IReadOnlyList<IpTransformerIndex>> GetByMunicipioAsync(
        string municipioIbgeCode, CancellationToken ct = default);

    Task AddAsync(IpTransformerIndex index, CancellationToken ct = default);

    Task UpdateAsync(IpTransformerIndex index, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
