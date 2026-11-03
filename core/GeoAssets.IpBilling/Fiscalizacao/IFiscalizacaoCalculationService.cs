namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Orchestrates one Fiscalização's <c>Carregado -&gt; Calculado -&gt; Validado</c> workflow —
/// Phase 2's reuse of the pattern <c>Levantamento.ILevantamentoCalculationService</c>
/// established in Phase 1 (XD01-182), simplified: no transformer resolution and no
/// cross-time new-vs-existing detection, since a Fiscalização line's totals are pure
/// per-operation-group count arithmetic (<see cref="FiscalizacaoLineCalculator"/>).
/// </summary>
public interface IFiscalizacaoCalculationService
{
    /// <summary>
    /// Lands already-validated spreadsheet rows as lines — no Agregado/Retroativo computed
    /// yet, letting a human fix bad rows (by re-uploading) before <see cref="CalculateAsync"/>
    /// ever runs. Only valid while the order is still <c>Carregado</c>; re-uploading replaces
    /// whatever was there before.
    /// </summary>
    Task<int> UploadAsync(
        string serviceOrderId,
        IReadOnlyList<FiscalizacaoWorkbookRow> rows,
        CancellationToken ct = default);

    /// <summary>
    /// <c>Carregado -&gt; Calculado</c> (or an idempotent recompute while already
    /// <c>Calculado</c>): maps <paramref name="rows"/> into lines, computes each line's
    /// Agregado/Retroativo via <see cref="FiscalizacaoLineCalculator"/>, and persists the result.
    /// </summary>
    Task<FiscalizacaoCalculationResult> CalculateAsync(
        string serviceOrderId,
        IReadOnlyList<FiscalizacaoWorkbookRow> rows,
        string? performedBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="CalculateAsync"/>, but re-derives rows from whatever
    /// <see cref="UploadAsync"/> most recently persisted instead of taking them as a parameter —
    /// what the REST <c>/calculate</c> endpoint calls.
    /// </summary>
    Task<FiscalizacaoCalculationResult> CalculateStoredAsync(
        string serviceOrderId,
        string? performedBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// <c>Calculado -&gt; Validado</c>: marks this Fiscalização as validated. Deliberately does
    /// <em>not</em> post to the Faturamento ledger — accumulate-mode auto-posting from a
    /// validated Fiscalização is XD01-188's deliverable, not this ticket's.
    /// </summary>
    Task<FiscalizacaoValidationResult> ValidateAsync(
        string serviceOrderId,
        string? performedBy = null,
        CancellationToken ct = default);
}
