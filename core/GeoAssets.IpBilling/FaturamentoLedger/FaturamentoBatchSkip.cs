namespace GeoAssets.IpBilling.FaturamentoLedger;

/// <summary>
/// One skipped row from a batch-load or recarga request (XD01-189), with the legacy-parity
/// reason text ("Faturamento já cadastrado" for batch load's existing-pair skip, "Faturamento
/// inexistente" for recarga's missing-pair skip).
/// </summary>
public sealed record FaturamentoBatchSkip(Guid MunicipioId, string Competencia, string Reason);
