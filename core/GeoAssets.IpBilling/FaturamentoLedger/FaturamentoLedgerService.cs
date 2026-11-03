using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.FaturamentoLedger;

/// <summary>
/// Default implementation of <see cref="IFaturamentoLedgerService"/>, a thin domain-expressive
/// wrapper over <see cref="IFaturamentoRepository"/>'s generic <c>PostOrderCalculationAsync</c>/
/// <c>OverwriteManualEntryAsync</c>.
///
/// <para>
/// <b>On the "same DB transaction" requirement (XD01-188):</b> this service does not open or
/// manage a transaction spanning the caller's own <c>IServiceOrderRepository</c> write — that
/// repository is backed by a separate <c>ServiceOrderDbContext</c>, a different EF Core context
/// from the <c>IpBillingDbContext</c> this service's repository uses (confirmed both point at
/// the same Postgres connection string in <c>GeoAssets.IpBilling.Server</c>'s <c>Program.cs</c>,
/// but as two independently-registered, independently-pooled-connection <c>AddDbContext</c>
/// registrations — not one shared transaction today). A true two-phase/shared-connection
/// transaction across those two contexts would need a dedicated infrastructure change (manually
/// sharing one <c>DbConnection</c>+<c>DbTransaction</c> across both context registrations, or
/// consolidating them) — a wider-blast-radius change than this ticket's reasonable scope,
/// exactly the kind of change this repo's established pattern (see XD01-292/XD01-294) says to
/// flag rather than slip in as a side effect of an unrelated ticket.
/// </para>
/// <para>
/// The actual safety property this service (and every caller wiring it in, following
/// <c>LevantamentoCalculationService.ValidateAsync</c>'s existing precedent) relies on instead is
/// <b>idempotent retry</b>: post to this ledger FIRST, <c>SaveChangesAsync</c>, and only THEN
/// transition the ServiceOrder to Validado. If the ledger write fails, nothing downstream has
/// committed yet (safe — the order stays Calculado, retry is clean). If the ledger write
/// succeeds but the subsequent order-transition fails, a retry of the same validation call is
/// safe — <c>PostOrderCalculationAsync</c>'s <c>(serviceOrderId, competencia)</c> dedup guard
/// means the retry will not double-post, and will complete the order transition. This ordering
/// specifically avoids the dangerous failure mode the parent epic calls out — a "Validado" order
/// with no posted money — at the cost of a transient, self-healing window where the ledger says
/// posted but the order hasn't caught up yet. This is NOT literal ACID rollback-of-both; it is
/// the strongest guarantee achievable without the larger cross-context transaction
/// infrastructure described above, and it is the same guarantee Levantamento's own
/// already-shipped Validate path has relied on since XD01-179.
/// </para>
/// </summary>
public sealed class FaturamentoLedgerService(IFaturamentoRepository faturamentoRepository) : IFaturamentoLedgerService
{
    public Task<Models.Faturamento> SetLevantamentoBaselineAsync(
        string serviceOrderId,
        Guid municipioId,
        string competencia,
        decimal totalKwh,
        DateTimeOffset postedAt,
        CancellationToken ct = default) =>
        faturamentoRepository.PostOrderCalculationAsync(
            serviceOrderId, municipioId, competencia, totalKwh, FaturamentoPostingMode.Set, postedAt, ct);

    public Task<Models.Faturamento> AccumulateInspectionDeltaAsync(
        string serviceOrderId,
        Guid municipioId,
        string competencia,
        decimal deltaKwh,
        DateTimeOffset postedAt,
        CancellationToken ct = default) =>
        faturamentoRepository.PostOrderCalculationAsync(
            serviceOrderId, municipioId, competencia, deltaKwh, FaturamentoPostingMode.Accumulate, postedAt, ct);

    public Task<Models.Faturamento> OverwriteManualEntryAsync(
        Guid municipioId,
        string competencia,
        decimal kwhFaturado,
        string enteredByReference,
        DateTimeOffset postedAt,
        CancellationToken ct = default) =>
        faturamentoRepository.OverwriteManualEntryAsync(
            municipioId, competencia, kwhFaturado, enteredByReference, postedAt, ct);
}
