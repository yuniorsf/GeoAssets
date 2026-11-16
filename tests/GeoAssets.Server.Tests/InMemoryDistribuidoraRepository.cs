using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.Server.Tests;

/// <summary>
/// In-memory <see cref="IDistribuidoraRepository"/> honouring the port's contract (1-based
/// paging that rejects values below 1; name-then-id order) for the XD01-301 routing tests.
/// </summary>
internal sealed class InMemoryDistribuidoraRepository : IDistribuidoraRepository
{
    private readonly Dictionary<Guid, Distribuidora> _byId = [];

    public List<(int Page, int PageSize)> PageRequests { get; } = [];

    public Task<Distribuidora?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<PagedResult<Distribuidora>> GetPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        PageRequests.Add((page, pageSize));

        var items = _byId.Values
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(new PagedResult<Distribuidora> { Items = items, TotalCount = _byId.Count });
    }

    public Task AddAsync(Distribuidora distribuidora, CancellationToken ct = default)
    {
        _byId[distribuidora.Id] = distribuidora;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Distribuidora distribuidora, CancellationToken ct = default)
    {
        _byId[distribuidora.Id] = distribuidora;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _byId.Remove(id);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
