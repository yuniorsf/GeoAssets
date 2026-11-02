namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Orchestrates one Levantamento's <c>Carregado -&gt; Calculado -&gt; Validado</c> workflow —
/// the richer, per-transformer-line, retroactive-aware sibling of the generic
/// <see cref="Calculation.IpBillingCalculationService"/> that Phase 0 (XD01-176) built for a
/// simpler flat single-row calculation. Establishes the pattern Fiscalização/Ofício
/// (XD01-182/185) reuse.
/// </summary>
public interface ILevantamentoCalculationService
{
    /// <summary>
    /// Lands already-validated spreadsheet rows (XD01-180's REST layer owns the structured
    /// row-level validation, via <see cref="LevantamentoWorkbookRowParser"/>, before calling
    /// this) as transformer lines — no kWh/resolution computed yet, letting a human fix bad
    /// rows (by re-uploading) before <see cref="CalculateAsync"/> ever runs. Only valid while
    /// the order is still <c>Carregado</c>; re-uploading replaces whatever was there before.
    /// </summary>
    Task<int> UploadAsync(
        string serviceOrderId,
        IReadOnlyList<LevantamentoWorkbookRow> rows,
        CancellationToken ct = default);

    /// <summary>
    /// <c>Carregado -&gt; Calculado</c> (or an idempotent recompute while already
    /// <c>Calculado</c>): maps <paramref name="rows"/> into transformer lines, resolves each to
    /// a <c>GeoFeature</c> where possible, detects new-vs-existing status against the
    /// município's prior validated surveys, computes normal/retroactive/24h kWh, and persists
    /// the full breakdown.
    /// </summary>
    Task<LevantamentoCalculationResult> CalculateAsync(
        string serviceOrderId,
        IReadOnlyList<LevantamentoWorkbookRow> rows,
        DateOnly? asOf = null,
        string? performedBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="CalculateAsync"/>, but re-derives <c>rows</c> from whatever
    /// <see cref="UploadAsync"/> most recently persisted instead of taking them as a parameter —
    /// what the REST <c>/calculate</c> endpoint calls (it has no rows of its own to pass; the
    /// upload already landed them). Also what a same-status recompute re-reads from.
    /// </summary>
    Task<LevantamentoCalculationResult> CalculateStoredAsync(
        string serviceOrderId,
        DateOnly? asOf = null,
        string? performedBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// <c>Calculado -&gt; Validado</c>: posts the calculated total to the Faturamento ledger as
    /// the new monthly baseline (<c>FaturamentoPostingMode.Set</c>) and marks this Levantamento
    /// as the município's new "most recent validated" survey.
    /// </summary>
    Task<LevantamentoValidationResult> ValidateAsync(
        string serviceOrderId,
        string? performedBy = null,
        CancellationToken ct = default);
}
