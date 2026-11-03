namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Carries only the computed totals, not a <c>Models.Faturamento</c> — validating an Ofício
/// does not post to the Faturamento ledger yet; that accumulate-mode auto-posting is XD01-188's
/// deliverable, same as Fiscalização's (see <c>Models.Faturamento</c>'s own doc comment).
/// </summary>
public sealed record OficioValidationResult(
    string ServiceOrderId,
    string Status,
    int TotalAgregado,
    int TotalRetroativo);
