namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Carries only the computed totals, not a <c>Models.Faturamento</c> — unlike
/// <c>Levantamento.LevantamentoValidationResult</c>, validating a Fiscalização does not post to
/// the Faturamento ledger yet; that accumulate-mode auto-posting is XD01-188's deliverable (see
/// <c>Models.Faturamento</c>'s own doc comment).
/// </summary>
public sealed record FiscalizacaoValidationResult(
    string ServiceOrderId,
    string Status,
    int TotalAgregado,
    int TotalRetroativo);
