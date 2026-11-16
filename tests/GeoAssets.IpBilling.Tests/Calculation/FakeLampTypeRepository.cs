using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.Tests.Calculation;

/// <summary>The global (standard) lamp chart.</summary>
internal sealed class FakeLampTypeRepository(params (LampTypeCode Code, int WattageW, decimal BallastLossW)[] options)
    : ILampTypeRepository
{
    private readonly List<LampType> _types = options
        .GroupBy(o => o.Code)
        .Select(g =>
        {
            var type = new LampType { Code = g.Key, Name = g.Key.ToString() };
            type.WattageOptions.AddRange(g.Select(o => new LampWattageOption
            {
                LampTypeId = type.Id,
                WattageW = o.WattageW,
                BallastLossW = o.BallastLossW,
            }));
            return type;
        })
        .ToList();

    public Task<LampType?> GetByCodeAsync(LampTypeCode code, CancellationToken ct = default) =>
        Task.FromResult(_types.FirstOrDefault(t => t.Code == code));

    public Task<IReadOnlyList<LampType>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LampType>>(_types);
}
