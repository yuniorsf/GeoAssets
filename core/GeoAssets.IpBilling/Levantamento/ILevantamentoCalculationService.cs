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
    /// <c>Carregado -&gt; Calculado</c>: maps <paramref name="rows"/> into transformer lines,
    /// resolves each to a <c>GeoFeature</c> where possible, detects new-vs-existing status
    /// against the município's prior validated surveys, computes normal/retroactive/24h kWh,
    /// and persists the full breakdown.
    /// </summary>
    Task<LevantamentoCalculationResult> CalculateAsync(
        string serviceOrderId,
        IReadOnlyList<LevantamentoWorkbookRow> rows,
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
