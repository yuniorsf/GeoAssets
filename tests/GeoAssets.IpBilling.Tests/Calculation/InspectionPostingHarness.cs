using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.Tests.Calculation;

/// <summary>
/// Wires a real <see cref="InspectionLedgerPoster"/> over a real
/// <see cref="FaturamentoLedgerService"/> and the in-memory collaborators above.
/// </summary>
internal sealed class InspectionPostingHarness
{
    public InMemoryFaturamentoRepository Faturamentos { get; } = new();
    public FakeDistribuidoraChart DistribuidoraChart { get; } = new();
    public FakeLevantamentoRepository Levantamentos { get; } = new();
    public FakeLampTypeRepository StandardChart { get; init; } = new((LampTypeCode.VS, 100, 17m));

    public InspectionLedgerPoster CreatePoster() => new(
        new FaturamentoLedgerService(Faturamentos), Faturamentos, DistribuidoraChart, StandardChart, Levantamentos);
}
