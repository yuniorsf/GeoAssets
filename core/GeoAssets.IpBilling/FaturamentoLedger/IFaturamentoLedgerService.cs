using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.FaturamentoLedger;

/// <summary>
/// Domain-expressive write path onto the <see cref="Models.Faturamento"/> ledger (XD01-188),
/// giving each caller a clearly-named method instead of a generic
/// <c>(mode: Set|Accumulate|Overwrite)</c> parameter — distinguishing SET (a validated
/// Levantamento recomputes the whole monthly baseline from scratch), ACCUMULATE (a validated
/// Fiscalização/Ofício adds a delta to the running total), and OVERWRITE (manual entry/batch
/// load/recarga — XD01-189's legacy-parity screens, not yet built) exactly as the parent
/// epic's Phase 4 scope requires. Every method is idempotent per
/// <c>(serviceOrderId, competencia)</c> except <see cref="OverwriteManualEntryAsync"/>, which has
/// no <c>ServiceOrderId</c> to key on (see its own doc comment).
///
/// Callers must call <c>SaveChangesAsync</c> (via whatever repository this service is backed
/// by — currently the same <see cref="Repositories.IFaturamentoRepository"/> instance) to commit;
/// this service only stages the change, same as the repository it wraps, so a caller can
/// sequence the ledger write relative to its own order-status transition exactly as the parent
/// epic's synchronous-posting requirement demands. See this class's own doc comment for why a
/// true cross-DbContext atomic transaction is not implemented.
/// </summary>
public interface IFaturamentoLedgerService
{
    /// <summary>SET: a validated Levantamento recomputes the whole monthly baseline.</summary>
    Task<Models.Faturamento> SetLevantamentoBaselineAsync(
        string serviceOrderId,
        Guid municipioId,
        string competencia,
        decimal totalKwh,
        DateTimeOffset postedAt,
        CancellationToken ct = default);

    /// <summary>ACCUMULATE: a validated Fiscalização/Ofício adds a delta to the running total.</summary>
    Task<Models.Faturamento> AccumulateInspectionDeltaAsync(
        string serviceOrderId,
        Guid municipioId,
        string competencia,
        decimal deltaKwh,
        DateTimeOffset postedAt,
        CancellationToken ct = default);

    /// <summary>OVERWRITE: manual entry/batch load/recarga posts an external ground-truth figure.</summary>
    Task<Models.Faturamento> OverwriteManualEntryAsync(
        Guid municipioId,
        string competencia,
        decimal kwhFaturado,
        string enteredByReference,
        DateTimeOffset postedAt,
        CancellationToken ct = default);
}
