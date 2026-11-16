using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.Tests.Calculation;

/// <summary>A Distribuidora chart reader that records every call it receives.</summary>
internal sealed class FakeDistribuidoraChart : IDistribuidoraLampWattageReader
{
    private readonly List<DistribuidoraLampWattage> _rows = [];

    public List<(Guid DistribuidoraId, LampTypeCode Code, int WattageW)> FindCalls { get; } = [];
    public int MeanCalls { get; private set; }

    public FakeDistribuidoraChart With(Guid distribuidoraId, LampTypeCode code, int wattageW, decimal ballastLossW)
    {
        _rows.Add(new DistribuidoraLampWattage
        {
            DistribuidoraId = distribuidoraId,
            Code = code,
            WattageW = wattageW,
            BallastLossW = ballastLossW,
        });
        return this;
    }

    public Task<PagedResult<DistribuidoraLampWattage>> GetPageAsync(
        Guid distribuidoraId, int page, int pageSize, CancellationToken ct = default) =>
        throw new NotSupportedException("The kWh conversion never pages the chart.");

    public Task<DistribuidoraLampWattage?> FindAsync(
        Guid distribuidoraId, LampTypeCode code, int wattageW, CancellationToken ct = default)
    {
        FindCalls.Add((distribuidoraId, code, wattageW));
        return Task.FromResult(_rows.FirstOrDefault(r =>
            r.DistribuidoraId == distribuidoraId && r.Code == code && r.WattageW == wattageW));
    }

    public Task<decimal?> GetMeanTotalBilledWAsync(Guid distribuidoraId, CancellationToken ct = default)
    {
        MeanCalls++;
        var rows = _rows.Where(r => r.DistribuidoraId == distribuidoraId).ToList();
        return Task.FromResult<decimal?>(rows.Count == 0 ? null : rows.Average(r => r.TotalBilledW));
    }
}
