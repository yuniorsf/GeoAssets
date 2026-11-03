using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.FaturamentoLedger;

/// <summary>Outcome of a batch-load or recarga request (XD01-189): rows actually applied vs. skipped.</summary>
public sealed record FaturamentoBatchResult(
    IReadOnlyList<Faturamento> Applied,
    IReadOnlyList<FaturamentoBatchSkip> Skipped);
