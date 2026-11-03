using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.FaturamentoLedger;

/// <summary>
/// Client-side gateway to <c>GeoAssets.IpBilling.Server</c>'s <c>/api/ip-billing/faturamentos/*</c>
/// endpoints (XD01-189) — the Blazor-facing counterpart of <see cref="IFaturamentoManualEntryService"/>,
/// shaped around what the ledger page (XD01-190, in <c>GeoAssets.Shared</c>) actually calls.
/// Mirrors <c>Oficio.IOficioRestClient</c>'s shape. Lives here (not in <c>GeoAssets.Web</c>,
/// which implements it) because <c>GeoAssets.Shared</c> components inject it and cannot
/// reference <c>GeoAssets.Web</c>.
/// </summary>
public interface IFaturamentoRestClient
{
    /// <summary>Queries a município's ledger rows, optionally narrowed to one competência —
    /// the "consulta" acceptance criterion.</summary>
    Task<IReadOnlyList<Faturamento>> ConsultaAsync(
        Guid municipioId, string? competencia, CancellationToken ct = default);

    /// <summary>Manual single-record entry — insert-only, rejects (409) if the
    /// (município, competência) pair already exists.</summary>
    Task<Faturamento> ManualEntryAsync(
        Guid municipioId, string competencia, decimal kwhFaturado, string? performedBy, CancellationToken ct = default);

    /// <summary>Recarga for a single (município, competência) row — update-only; wraps a
    /// one-item call to the batch "recarga" endpoint (skip-if-missing, mirror-opposite of
    /// batch load) since this dialog corrects one row at a time.</summary>
    Task<FaturamentoBatchResult> RecargaAsync(
        Guid municipioId, string competencia, decimal kwhFaturado, string? performedBy, CancellationToken ct = default);

    /// <summary>Batch load (insert-only; skips and reports already-existing pairs). No Blazor
    /// dialog calls this yet (XD01-190's own ticket only names a manual-entry and a recarga
    /// dialog) — exposed here so the client already covers XD01-189's full REST surface for a
    /// future bulk-import UI.</summary>
    Task<FaturamentoBatchResult> BatchLoadAsync(
        IReadOnlyList<FaturamentoBatchItem> items, string? performedBy, CancellationToken ct = default);
}
