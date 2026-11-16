using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.Tests.Calculation;

/// <summary>
/// In-memory collaborators for the XD01-301 points→kWh posting tests, shared by the
/// <see cref="InspectionLedgerPoster"/> tests and both calculation services' Validate tests so
/// those run the real poster and the real <see cref="FaturamentoLedgerService"/> end to end.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
