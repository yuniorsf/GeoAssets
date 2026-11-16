using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Repositories;

/// <summary>Read side of the <see cref="Distribuidora"/> reference data (XD01-301).</summary>
public interface IDistribuidoraReader
{
    Task<Distribuidora?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// One page of Distribuidoras, sorted by <see cref="Distribuidora.Name"/> then
    /// <see cref="Distribuidora.Id"/> (deterministic). <paramref name="page"/> is 1-based;
    /// implementations reject <paramref name="page"/> &lt; 1 or <paramref name="pageSize"/> &lt; 1
    /// with <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    Task<PagedResult<Distribuidora>> GetPageAsync(int page, int pageSize, CancellationToken ct = default);
}
