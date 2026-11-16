using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.Server.Tests;

/// <summary>
/// In-memory <see cref="IDistribuidoraLampWattageRepository"/> honouring the port's contract
/// (paging bounds, code/wattage/id order, upsert-in-place, null mean on an empty chart) for the
/// XD01-301 routing and integration tests.
/// </summary>
internal sealed class InMemoryDistribuidoraLampWattageRepository : IDistribuidoraLampWattageRepository
{
    private readonly List<DistribuidoraLampWattage> _rows = [];

    public List<(int Page, int PageSize)> PageRequests { get; } = [];

    public IReadOnlyList<DistribuidoraLampWattage> Rows => _rows;

    public Task<PagedResult<DistribuidoraLampWattage>> GetPageAsync(
        Guid distribuidoraId, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        PageRequests.Add((page, pageSize));

        var chart = _rows.Where(r => r.DistribuidoraId == distribuidoraId).ToList();
        var items = chart
            .OrderBy(r => r.Code)
            .ThenBy(r => r.WattageW)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(new PagedResult<DistribuidoraLampWattage> { Items = items, TotalCount = chart.Count });
    }

    public Task<DistribuidoraLampWattage?> FindAsync(
        Guid distribuidoraId, LampTypeCode code, int wattageW, CancellationToken ct = default) =>
        Task.FromResult(_rows.FirstOrDefault(r =>
            r.DistribuidoraId == distribuidoraId && r.Code == code && r.WattageW == wattageW));

    public Task<decimal?> GetMeanTotalBilledWAsync(Guid distribuidoraId, CancellationToken ct = default)
    {
        var chart = _rows.Where(r => r.DistribuidoraId == distribuidoraId).ToList();
        return Task.FromResult<decimal?>(chart.Count == 0 ? null : chart.Average(r => r.TotalBilledW));
    }

    public async Task<DistribuidoraLampWattage> UpsertAsync(DistribuidoraLampWattage row, CancellationToken ct = default)
    {
        var existing = await FindAsync(row.DistribuidoraId, row.Code, row.WattageW, ct);
        if (existing is not null)
        {
            existing.BallastLossW = row.BallastLossW;
            return existing;
        }

        _rows.Add(row);
        return row;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _rows.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
