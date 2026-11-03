namespace GeoAssets.IpBilling.FaturamentoLedger;

/// <summary>One (município, competência, kWh) row in a batch-load or recarga request (XD01-189).</summary>
public sealed record FaturamentoBatchItem(Guid MunicipioId, string Competencia, decimal KwhFaturado);
